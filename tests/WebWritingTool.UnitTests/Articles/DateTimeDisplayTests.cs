using System.Globalization;
using System.Reflection;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Web.Components.Articles;
using WebWritingTool.Web.Components.Pages;
using ArticlesPage = WebWritingTool.Web.Components.Pages.Articles;

namespace WebWritingTool.UnitTests.Articles;

public class DateTimeDisplayTests
{
    [Theory]
    [InlineData("2026-09-24T01:47:51+00:00", "2026/9/24 10時", "2026/9/24 10:47")]
    [InlineData("2026-09-24T15:00:00+00:00", "2026/9/25 00時", "2026/9/25 00:00")]
    [InlineData("2026-12-31T15:05:00+00:00", "2027/1/1 00時", "2027/1/1 00:05")]
    [InlineData("2026-09-23T18:47:51-07:00", "2026/9/24 10時", "2026/9/24 10:47")]
    public void PageDates_DisplayTheSameInstantInJapanTime(
        string timestamp, string expectedArticleDate, string expectedDetailedDate)
    {
        var value = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);

        Assert.Equal(expectedArticleDate, FormatPageDate(typeof(ArticlesPage), value));
        Assert.Equal(expectedDetailedDate, FormatPageDate(typeof(Settings), value));
        Assert.Equal(expectedDetailedDate, FormatPageDate(typeof(AdminUsers), value));
    }

    [Fact]
    public void AdminDate_WithoutLastLogin_DisplaysPlaceholder()
    {
        Assert.Equal("-", FormatPageDate(typeof(AdminUsers), null));
    }

    [Fact]
    public async Task ResearchDates_RenderWebAndXAcquisitionTimesInJapanTime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider, AuthenticatedUser>();
        services.AddScoped<ExternalApiExecutionContext>();
        services.AddSingleton<IArticleResearchService, ResearchData>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ArticleResearchPanel.ArticleId)] = Guid.NewGuid(),
                [nameof(ArticleResearchPanel.Keyword)] = "日時表示の確認"
            });
            var result = await renderer.RenderComponentAsync<ArticleResearchPanel>(parameters);
            return WebUtility.HtmlDecode(result.ToHtmlString());
        });

        Assert.Contains("取得：2027/01/01 00:05", html);
        Assert.Contains("取得：2026/09/24 10:47", html);
    }

    private static string FormatPageDate(Type componentType, DateTimeOffset? value)
    {
        var formatter = componentType.GetMethod("ToDisplayDate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(formatter);
        return Assert.IsType<string>(formatter.Invoke(null, [value]));
    }

    private sealed class AuthenticatedUser : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "date-display-user")], "Test"))));
    }

    private sealed class ResearchData : IArticleResearchService
    {
        public Task<ArticleResearchResponse?> GetAsync(
            ArticleActor actor, Guid articleId, Guid? headingId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ArticleResearchResponse?>(new ArticleResearchResponse(true,
                [new ResearchWebResult("Web資料", "https://example.test/", "資料の要約",
                    new DateTimeOffset(2026, 12, 31, 15, 5, 0, TimeSpan.Zero))],
                [new ResearchXPost("sample-post", null, "X資料", null, null,
                    new DateTimeOffset(2026, 9, 24, 1, 47, 51, TimeSpan.Zero))], null));

        public Task<JobServiceResult<JobAcceptedResponse>> EnqueueAsync(
            ArticleActor actor, Guid articleId, string source, ArticleResearchRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AiReferenceSource>> GetSelectedReferencesAsync(
            string userId, Guid articleId, ResearchSourceSelection sources, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AiReferenceSource>> GetReferencesAsync(
            string userId, Guid articleId, Guid? headingId, bool searchWeb, string? query = null,
            bool? domesticOnly = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
