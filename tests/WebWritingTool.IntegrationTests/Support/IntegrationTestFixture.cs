using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.IntegrationTests.Support;

public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string?> previousEnvironmentValues = [];

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("web_writing_tool_tests")
        .WithUsername("web_writing_tool")
        .WithPassword("web_writing_tool_tests")
        .Build();

    internal TestApplicationFactory Factory { get; private set; } = null!;

    internal string ConnectionString => postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        SetTestEnvironmentVariables();
        await ApplyMigrationsAsync();
        Factory = new TestApplicationFactory(postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();
        RestoreEnvironmentVariables();
        await postgres.DisposeAsync().AsTask();
    }

    public HttpClient CreateAnonymousClient()
    {
        return Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string userId, params string[] roles)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserNameHeader, $"{userId}@example.test");
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        }

        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/security/antiforgery-token");
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Add(token.HeaderName, token.RequestToken);

        return client;
    }

    public async Task SeedUserAsync(string userId, string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await dbContext.Users.AnyAsync(user => user.Id == userId))
        {
            dbContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                DisplayName = email,
                IsEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
        }

        await dbContext.SaveChangesAsync();

        foreach (var roleName in roles)
        {
            var role = await dbContext.Roles.FirstOrDefaultAsync(role => role.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole(roleName)
                {
                    NormalizedName = roleName.ToUpperInvariant()
                };
                dbContext.Roles.Add(role);
                await dbContext.SaveChangesAsync();
            }

            var hasRole = await dbContext.UserRoles.AnyAsync(
                userRole => userRole.UserId == userId && userRole.RoleId == role.Id);
            if (!hasRole)
            {
                dbContext.UserRoles.Add(new IdentityUserRole<string>
                {
                    UserId = userId,
                    RoleId = role.Id
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SeedUserWithPasswordAsync(
        string userId,
        string email,
        string password,
        params string[] roles)
    {
        await SeedUserAsync(userId, email, roles);

        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        Assert.NotNull(user);

        var result = await userManager.AddPasswordAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Code)));
    }

    public async Task<bool> CheckPasswordAsync(string userId, string password)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && await userManager.CheckPasswordAsync(user, password);
    }

    public async Task<Guid> SeedArticleAsync(string userId, string keyword)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var article = new Article
        {
            UserId = userId,
            Keyword = keyword,
            Title = $"{keyword} title",
            Status = ArticleStatus.Draft,
            Tags = ["test"],
            GenerationModel = "gemini-3.5-flash",
            OutlineMethod = "Keyword",
            NotificationMode = "None",
            IsDomesticOnly = true
        };

        dbContext.Articles.Add(article);
        await dbContext.SaveChangesAsync();
        return article.Id;
    }

    private async Task ApplyMigrationsAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    private void SetTestEnvironmentVariables()
    {
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        SetEnvironmentVariable("ConnectionStrings__DefaultConnection", postgres.GetConnectionString());
        SetEnvironmentVariable("Security__RequireHttps", "false");
        SetEnvironmentVariable("AiProviders__Gemini__ApiKey", "test-gemini-key");
        SetEnvironmentVariable("SearchProviders__Tavily__ApiKey", "test-tavily-key");
        SetEnvironmentVariable("SearchProviders__X__BearerToken", "test-x-token");
        SetEnvironmentVariable("SearchCache__Policy", "dev");
        SetEnvironmentVariable("Wordpress__AllowedSchemes__0", "https");
        SetEnvironmentVariable("Wordpress__TimeoutSeconds", "60");
        SetEnvironmentVariable("Notifications__Provider", "Discord");
        SetEnvironmentVariable("Notifications__TimeoutSeconds", "30");
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        previousEnvironmentValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var item in previousEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }

    private sealed record AntiforgeryTokenResponse(
        string HeaderName,
        string FormFieldName,
        string RequestToken);
}
