using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.E2ETests.Support;

public sealed partial class E2ETestFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin-e2e@example.test";
    public const string AdminPassword = "Change-this-e2e-password-123!";
    public const string StandardUserPassword = "Change-this-e2e-user-123!";

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

    public async Task<SeededArticleAccessScenario> SeedArticleAccessScenarioAsync(string suffix)
    {
        await using var dbContext = CreateDbContext();

        var owner = CreateStandardUser(
            $"owner-{suffix}@example.test",
            $"E2E Owner {suffix}");
        var viewer = CreateStandardUser(
            $"viewer-{suffix}@example.test",
            $"E2E Viewer {suffix}");

        dbContext.Users.AddRange(owner, viewer);

        var userRoleId = await dbContext.Roles
            .Where(role => role.NormalizedName == ApplicationRoles.User.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleAsync();

        dbContext.UserRoles.AddRange(
            new IdentityUserRole<string>
            {
                UserId = owner.Id,
                RoleId = userRoleId
            },
            new IdentityUserRole<string>
            {
                UserId = viewer.Id,
                RoleId = userRoleId
            });

        var article = new Article
        {
            UserId = owner.Id,
            Keyword = $"e2e-auth-keyword-{suffix}",
            Title = $"E2E所有者限定記事 {suffix}",
            Status = ArticleStatus.Draft,
            Tags = ["e2e-auth"],
            Memo = "他ユーザーから見えないことを検証するE2Eデータ",
            GenerationModel = "gemini-3.5-flash",
            OutlineMethod = "Keyword",
            SearchMode = false,
            IsDomesticOnly = true,
            NotificationMode = "None"
        };

        dbContext.Articles.Add(article);
        await dbContext.SaveChangesAsync();

        return new SeededArticleAccessScenario(
            owner.Email!,
            viewer.Email!,
            article.Id,
            article.Title!);
    }

    public async Task<SeededArticleSearchScenario> SeedArticleSearchScenarioAsync(string suffix)
    {
        await using var dbContext = CreateDbContext();
        var adminUserId = await GetUserIdByEmailAsync(dbContext, AdminEmail);

        var matching = CreateArticle(
            adminUserId,
            $"e2e-search-keyword-{suffix}",
            $"E2E検索対象記事 {suffix}");
        matching.Tags = ["e2e-search", suffix];
        matching.Memo = "検索で表示される記事";

        var other = CreateArticle(
            adminUserId,
            $"e2e-other-keyword-{suffix}",
            $"E2E検索対象外記事 {suffix}");
        other.Tags = ["e2e-search-other", suffix];
        other.Memo = "検索で表示されない記事";

        dbContext.Articles.AddRange(matching, other);
        await dbContext.SaveChangesAsync();

        return new SeededArticleSearchScenario(matching.Title!, other.Title!);
    }

    public async Task<SeededWordpressPostScenario> SeedWordpressPostScenarioAsync(string suffix)
    {
        await using var dbContext = CreateDbContext();
        var adminUserId = await GetUserIdByEmailAsync(dbContext, AdminEmail);

        var article = CreateArticle(
            adminUserId,
            $"e2e-wordpress-keyword-{suffix}",
            $"E2E WordPress投稿記事 {suffix}");
        article.Status = ArticleStatus.Completed;
        article.Body = "## E2E見出し\n\nE2E本文";
        article.HtmlBody = "<h2>E2E見出し</h2><p>E2E本文</p>";
        article.CompletedAt = DateTimeOffset.UtcNow;

        dbContext.Articles.Add(article);
        await dbContext.SaveChangesAsync();

        return new SeededWordpressPostScenario(article.Id, article.Title!);
    }

    public async Task<int> GetJobCountAsync(Guid articleId, JobType jobType)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ArticleGenerationJobs.CountAsync(
            job => job.ArticleId == articleId && job.JobType == jobType);
    }

    public async Task<string?> GetArticleWritingProfileSnapshotJsonAsync(Guid articleId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Articles
            .Where(article => article.Id == articleId)
            .Select(article => article.WritingProfileSnapshotJson)
            .SingleAsync();
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

    private static async Task<string> GetUserIdByEmailAsync(ApplicationDbContext dbContext, string email)
    {
        return await dbContext.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private static Article CreateArticle(string userId, string keyword, string title)
    {
        return new Article
        {
            UserId = userId,
            Keyword = keyword,
            Title = title,
            Status = ArticleStatus.Draft,
            Tags = [],
            GenerationModel = "gemini-3.5-flash",
            OutlineMethod = "Keyword",
            SearchMode = false,
            IsDomesticOnly = true,
            NotificationMode = "None"
        };
    }

    private static ApplicationUser CreateStandardUser(string email, string displayName)
    {
        var normalizedEmail = email.ToUpperInvariant();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, StandardUserPassword);
        return user;
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

public sealed record SeededArticleAccessScenario(
    string OwnerEmail,
    string ViewerEmail,
    Guid ArticleId,
    string ArticleTitle);

public sealed record SeededArticleSearchScenario(
    string MatchingTitle,
    string OtherTitle);

public sealed record SeededWordpressPostScenario(
    Guid ArticleId,
    string ArticleTitle);
