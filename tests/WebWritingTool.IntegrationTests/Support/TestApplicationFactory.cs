using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.IntegrationTests.Support;

internal sealed class TestApplicationFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Security:RequireHttps"] = "false",
                ["AiProviders:Gemini:ApiKey"] = "test-gemini-key",
                ["SearchProviders:Tavily:ApiKey"] = "test-tavily-key",
                ["SearchProviders:X:BearerToken"] = "test-x-token",
                ["SearchCache:Policy"] = "dev",
                ["Wordpress:AllowedSchemes:0"] = "https",
                ["Wordpress:TimeoutSeconds"] = "60",
                ["Notifications:Provider"] = "Discord",
                ["Notifications:TimeoutSeconds"] = "30"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.RemoveAll<IHostedService>();
            services.RemoveAll<IUrlSafetyValidator>();
            services.RemoveAll<IWordpressClient>();
            services.AddSingleton<IUrlSafetyValidator, TestUrlSafetyValidator>();
            services.AddSingleton<IWordpressClient, TestWordpressClient>();
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });
        });
    }

    private sealed class TestUrlSafetyValidator : IUrlSafetyValidator
    {
        public Task<UrlSafetyValidationResult> ValidateHttpsPublicUrlAsync(
            string? value,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.IsLoopback)
            {
                return Task.FromResult(UrlSafetyValidationResult.Failure("URL is not a public HTTPS URL."));
            }

            return Task.FromResult(UrlSafetyValidationResult.Success(uri));
        }
    }

    private sealed class TestWordpressClient : IWordpressClient
    {
        public Task<WordpressConnectionTestResult> TestConnectionAsync(
            WordpressSiteConnection connection,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WordpressConnectionTestResult(
                true,
                "OK",
                DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
            WordpressSiteConnection connection,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WordpressCategoryDto> categories =
            [
                new WordpressCategoryDto(1, "Default", "default")
            ];
            return Task.FromResult(categories);
        }

        public Task<WordpressPostResult> CreatePostAsync(
            WordpressPostRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WordpressPostResult(
                true,
                1,
                $"{request.Connection.BaseUrl.TrimEnd('/')}/post/1",
                null,
                null));
        }
    }
}
