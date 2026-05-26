using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.E2ETests.Support;

public sealed partial class E2ETestFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin-e2e@example.test";
    public const string AdminPassword = "Change-this-e2e-password-123!";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("web_writing_tool_e2e")
        .WithUsername("web_writing_tool")
        .WithPassword("web_writing_tool_e2e")
        .Build();

    private Process? appProcess;
    private StreamWriter? appLogWriter;
    private IPlaywright? playwright;
    private IBrowser? browser;

    public Uri BaseAddress { get; private set; } = null!;

    public string RepoRoot { get; } = FindRepoRoot();

    public string TestResultsDirectory => Path.Combine(RepoRoot, "test-results", "e2e");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(TestResultsDirectory);
        await postgres.StartAsync();
        await ApplyMigrationsAsync();
        StartApplication();
        await WaitForApplicationAsync();

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.CloseAsync();
        }

        playwright?.Dispose();
        await StopApplicationAsync();
        await postgres.DisposeAsync().AsTask();
    }

    public async Task<E2EPageSession> CreateSessionAsync(string testName)
    {
        if (browser is null)
        {
            throw new InvalidOperationException("Playwright browser has not been initialized.");
        }

        var safeName = CreateSafeName(testName);
        var sessionDirectory = Path.Combine(TestResultsDirectory, safeName);
        Directory.CreateDirectory(sessionDirectory);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            Locale = "ja-JP",
            TimezoneId = "Asia/Tokyo",
            RecordVideoDir = sessionDirectory,
            ViewportSize = new ViewportSize
            {
                Width = 1280,
                Height = 900
            }
        });

        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        return new E2EPageSession(
            context,
            await context.NewPageAsync(),
            Path.Combine(sessionDirectory, "trace.zip"),
            sessionDirectory);
    }

    public async Task<Guid> SeedWordpressSiteForArticleOwnerAsync(Guid articleId)
    {
        await using var dbContext = CreateDbContext();
        var article = await dbContext.Articles.SingleAsync(item => item.Id == articleId);
        var site = new WordpressSite
        {
            UserId = article.UserId,
            SiteName = "E2E WordPress",
            BaseUrl = "http://127.0.0.1:65535",
            LoginId = "wp-e2e-user",
            EncryptedApplicationPassword = "not-used-by-post-job-registration",
            DefaultCategoryId = 7,
            DefaultCategoryName = "E2E",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WordpressSites.Add(site);
        await dbContext.SaveChangesAsync();
        return site.Id;
    }

    public async Task MarkArticleCompletedAsync(Guid articleId, string htmlBody)
    {
        await using var dbContext = CreateDbContext();
        var article = await dbContext.Articles.SingleAsync(item => item.Id == articleId);
        article.Status = ArticleStatus.Completed;
        article.Body = "## E2E見出し\n\nE2E本文";
        article.HtmlBody = htmlBody;
        article.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task<Guid> GetFirstHeadingIdAsync(Guid articleId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ArticleHeadings
            .Where(heading => heading.ArticleId == articleId)
            .OrderBy(heading => heading.DisplayOrder)
            .Select(heading => heading.Id)
            .FirstAsync();
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private void StartApplication()
    {
        var port = GetFreeTcpPort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}");

        var configuration = Environment.GetEnvironmentVariable("E2E_DOTNET_CONFIGURATION");
        if (string.IsNullOrWhiteSpace(configuration))
        {
            configuration = "Debug";
        }

        var logPath = Path.Combine(TestResultsDirectory, "web-app.log");
        appLogWriter = new StreamWriter(logPath, append: false)
        {
            AutoFlush = true
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = GetDotnetExecutable(),
            WorkingDirectory = RepoRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot, "src", "WebWritingTool.Web", "WebWritingTool.Web.csproj"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseAddress.ToString());

        SetAppEnvironment(startInfo);

        appProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        appProcess.OutputDataReceived += (_, args) => WriteAppLog(args.Data);
        appProcess.ErrorDataReceived += (_, args) => WriteAppLog(args.Data);

        if (!appProcess.Start())
        {
            throw new InvalidOperationException("Failed to start the web application process.");
        }

        appProcess.BeginOutputReadLine();
        appProcess.BeginErrorReadLine();
    }

    private void SetAppEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Test";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseAddress.ToString();
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = postgres.GetConnectionString();
        startInfo.Environment["Security__RequireHttps"] = "false";
        startInfo.Environment["BackgroundJobs__Enabled"] = "false";
        startInfo.Environment["AdminSeed__Email"] = AdminEmail;
        startInfo.Environment["AdminSeed__Password"] = AdminPassword;
        startInfo.Environment["AdminSeed__DisplayName"] = "E2E Admin";
        startInfo.Environment["AiProviders__Gemini__ApiKey"] = "e2e-gemini-key";
        startInfo.Environment["SearchProviders__Tavily__ApiKey"] = "e2e-tavily-key";
        startInfo.Environment["SearchProviders__X__BearerToken"] = "e2e-x-token";
        startInfo.Environment["SearchCache__Policy"] = "dev";
        startInfo.Environment["Wordpress__TimeoutSeconds"] = "60";
        startInfo.Environment["Notifications__Provider"] = "Discord";
        startInfo.Environment["Notifications__TimeoutSeconds"] = "30";
    }

    private async Task WaitForApplicationAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        var healthUrl = new Uri(BaseAddress, "/health/live");
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (appProcess is not null && appProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"The web application exited before it became ready. ExitCode={appProcess.ExitCode}");
            }

            try
            {
                using var response = await client.GetAsync(healthUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The web application did not become ready within 90 seconds.");
    }

    private async Task StopApplicationAsync()
    {
        if (appProcess is not null)
        {
            if (!appProcess.HasExited)
            {
                appProcess.Kill(entireProcessTree: true);
                await appProcess.WaitForExitAsync();
            }

            appProcess.Dispose();
        }

        await (appLogWriter?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    private void WriteAppLog(string? line)
    {
        if (line is null || appLogWriter is null)
        {
            return;
        }

        lock (appLogWriter)
        {
            appLogWriter.WriteLine(line);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetDotnetExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("E2E_DOTNET_EXECUTABLE");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WebWritingTool.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be found.");
    }

    private static string CreateSafeName(string value)
    {
        var safeName = SafeNamePattern().Replace(value, "_").Trim('_');
        return string.IsNullOrWhiteSpace(safeName) ? "e2e" : safeName;
    }

    [GeneratedRegex("[^a-zA-Z0-9_.-]+")]
    private static partial Regex SafeNamePattern();
}
