using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.Infrastructure.Notifications;
using WebWritingTool.Infrastructure.Search;
using WebWritingTool.Infrastructure.Wordpress;
using WebWritingTool.Web;

namespace WebWritingTool.UnitTests.Generation;

public class DummyModeConfigurationTests
{
    [Fact]
    public void GuestMode_WithRealApisConfigured_UsesOnlyDummyClientsAndDoesNotAffectOtherScopes()
    {
        using var services = CreateServices("false");
        using var guest = services.CreateScope();
        using var regular = services.CreateScope();
        guest.ServiceProvider.GetRequiredService<ExternalApiExecutionContext>().EnableGuestMode();

        Assert.IsType<DummyTextGenerationClient>(guest.ServiceProvider.GetRequiredService<IAiTextGenerationClient>());
        Assert.IsType<DummySearchClient>(guest.ServiceProvider.GetRequiredService<IWebSearchClient>());
        Assert.IsType<DummySearchClient>(guest.ServiceProvider.GetRequiredService<IXFullArchiveSearchClient>());
        Assert.IsType<DummyExternalApiClient>(guest.ServiceProvider.GetRequiredService<IWordpressClient>());
        Assert.IsType<DummyExternalApiClient>(guest.ServiceProvider.GetRequiredService<IDiscordNotificationClient>());
        Assert.True(guest.ServiceProvider.GetRequiredService<SearchDataMode>().IsDummy);

        Assert.IsType<GeminiTextGenerationClient>(regular.ServiceProvider.GetRequiredService<IAiTextGenerationClient>());
        Assert.IsType<TavilyWebSearchClient>(regular.ServiceProvider.GetRequiredService<IWebSearchClient>());
        Assert.IsType<XFullArchiveSearchClient>(regular.ServiceProvider.GetRequiredService<IXFullArchiveSearchClient>());
        Assert.IsType<WordpressClient>(regular.ServiceProvider.GetRequiredService<IWordpressClient>());
        Assert.IsType<DiscordNotificationClient>(regular.ServiceProvider.GetRequiredService<IDiscordNotificationClient>());
        Assert.False(regular.ServiceProvider.GetRequiredService<SearchDataMode>().IsDummy);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public async Task Configuration_SelectsClientsAndChecksOnlyRequiredCredentials(string? value, bool dummy)
    {
        using var services = CreateServices(value);
        Assert.Equal(dummy ? typeof(DummyTextGenerationClient) : typeof(GeminiTextGenerationClient),
            services.GetRequiredService<IAiTextGenerationClient>().GetType());
        Assert.Equal(dummy ? typeof(DummySearchClient) : typeof(TavilyWebSearchClient),
            services.GetRequiredService<IWebSearchClient>().GetType());
        Assert.Equal(dummy ? typeof(DummySearchClient) : typeof(XFullArchiveSearchClient),
            services.GetRequiredService<IXFullArchiveSearchClient>().GetType());
        Assert.Equal(dummy ? typeof(DummyExternalApiClient) : typeof(WordpressClient),
            services.GetRequiredService<IWordpressClient>().GetType());
        Assert.Equal(dummy ? typeof(DummyExternalApiClient) : typeof(DiscordNotificationClient),
            services.GetRequiredService<IDiscordNotificationClient>().GetType());
        var report = await services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains("deps"));
        Assert.Equal(dummy ? HealthStatus.Healthy : HealthStatus.Degraded, report.Status);
    }

    [Fact]
    public async Task DummyMode_ReturnsSamplesAndDoesNotReportPostsOrNotificationsAsSent()
    {
        using var services = CreateServices("true");
        var web = await services.GetRequiredService<IWebSearchClient>().SearchAsync(
            new WebSearchRequest("家庭菜園", "Japan", "ja", 5, true, null, "basic", null, null, null, null));
        Assert.Equal(5, web.Count);
        Assert.All(web, result => Assert.Contains("サンプル", result.Title));
        var x = services.GetRequiredService<IXFullArchiveSearchClient>();
        var posts = await x.SearchAsync(new XFullArchiveSearchRequest(
            "家庭菜園", "ja", null, null, 3, false, true, true, null, true));
        Assert.Equal(3, posts.Count);
        Assert.All(posts, post => Assert.Contains("サンプル", post.Text));
        Assert.Empty(await x.RehydrateAsync(new XPostRehydrationRequest(["123"])));

        var wordpress = services.GetRequiredService<IWordpressClient>();
        var connection = new WordpressSiteConnection("https://example.invalid", "", "");
        Assert.False((await wordpress.TestConnectionAsync(connection)).Success);
        Assert.Empty(await wordpress.GetCategoriesAsync(connection));
        var post = await wordpress.CreatePostAsync(new WordpressPostRequest(connection, "記事", "<p>本文</p>", null, "draft"));
        Assert.False(post.Success);
        Assert.Null(post.PostId);
        Assert.Null(post.PostUrl);
        Assert.Equal(ExternalIntegrationErrorCodes.ValidationError, post.ErrorCode);
        var notification = await services.GetRequiredService<IDiscordNotificationClient>().SendAsync(
            new DiscordNotificationRequest("https://example.invalid", "通知", []));
        Assert.False(notification.Success);
        Assert.Equal(ExternalIntegrationErrorCodes.ValidationError, notification.ErrorCode);
    }

    private static ServiceProvider CreateServices(string? useMocks)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused",
            ["Wordpress:AllowedSchemes:0"] = "https",
            ["ExternalApis:UseMocks"] = useMocks
        });
        // Webの実際のDI登録を検証する。ホストは起動せず、DBにも外部APIにも接続しない。
        var registration = typeof(WebAssemblyReference).Assembly
            .GetType("WebWritingTool.Web.Configuration.ServiceCollectionExtensions")!
            .GetMethod("AddInfrastructureServices", BindingFlags.Public | BindingFlags.Static)!;
        registration.Invoke(null, [builder.Services, builder.Configuration, builder.Environment]);
        return builder.Services.BuildServiceProvider();
    }
}
