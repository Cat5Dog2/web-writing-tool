using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed partial class MajorScreenFlowTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task MajorScreens_CompleteSmokeFlow()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MajorScreens_CompleteSmokeFlow));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-keyword-{suffix}";
        var title = $"E2E記事 {suffix}";

        try
        {
            await LoginAsync(page);
            var articleId = await CreateArticleAsync(page, keyword, title);
            await EditGeneratedContentAsync(page);

            var headingId = await fixture.GetFirstHeadingIdAsync(articleId);
            await EnqueueOutlineGenerationAsync(page, articleId, keyword, title);
            await EnqueueHeadingBodyGenerationAsync(page, articleId, headingId);

            var siteId = await fixture.SeedWordpressSiteForArticleOwnerAsync(articleId);
            await fixture.MarkArticleCompletedAsync(articleId, "<h2>E2E見出し</h2><p>E2E本文</p>");
            await VerifyWordpressPostFlowAsync(page, articleId, siteId, title);

            await CreateAndDeleteUserAsync(page, suffix);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    private static async Task LoginAsync(IPage page)
    {
        await page.GotoAsync("/login");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "ログイン" })).ToBeVisibleAsync();

        await page.Locator("#email").FillAsync(E2ETestFixture.AdminEmail);
        await page.Locator("#password").FillAsync(E2ETestFixture.AdminPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成" })).ToBeVisibleAsync();
    }

    private static async Task<Guid> CreateArticleAsync(IPage page, string keyword, string title)
    {
        await page.GotoAsync("/articles/create");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "記事作成" })).ToBeVisibleAsync();
        await WaitForInteractiveRenderAsync(page);

        await FillAndChangeAsync(page.Locator("#keyword"), keyword);
        await FillAndChangeAsync(page.Locator("#title"), title);
        await page.Locator("#outline-method").SelectOptionAsync("Keyword");
        await page.Locator("#search-mode").SetCheckedAsync(false);
        await page.GetByRole(AriaRole.Button, new() { Name = "構成を作成" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" })).ToBeVisibleAsync();
        await WaitForInteractiveRenderAsync(page);

        var match = ArticleUrlPattern().Match(page.Url);
        Assert.True(match.Success, $"Article detail URL was expected but current URL was {page.Url}.");
        return Guid.Parse(match.Groups["id"].Value);
    }

    private static async Task EditGeneratedContentAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加" }).ClickAsync();
        await Expect(page.GetByText("見出しを追加しました。")).ToBeVisibleAsync();

        await FillAndChangeAsync(page.Locator("#heading-title"), "E2E見出し");
        await FillAndChangeAsync(page.Locator("#heading-body"), "E2E本文です。ブラウザ経由で保存される本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存" }).ClickAsync();
        await Expect(page.GetByText("本文を保存しました。")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "HTML変換" }).ClickAsync();
        await Expect(page.GetByText("HTMLへ変換しました。")).ToBeVisibleAsync();
    }

    private static async Task EnqueueOutlineGenerationAsync(
        IPage page,
        Guid articleId,
        string keyword,
        string title)
    {
        var response = await PostJsonAsync(
            page,
            $"/api/articles/{articleId}/generation/outline",
            new
            {
                keyword,
                title,
                h2Count = 2,
                h3Count = 0,
                outlineMethod = "Ai",
                generationModel = "gemini-3.5-flash",
                searchMode = false,
                isDomesticOnly = true,
                tone = "Normal",
                suggestedKeywords = (string?)null,
                relatedKeywords = (string?)null,
                learningType = "None",
                learningText = (string?)null,
                additionalPrompt = (string?)null
            });

        Assert.Equal(202, response.Status);
        using var payload = JsonDocument.Parse(response.Body);
        Assert.Equal("OutlineGeneration", payload.RootElement.GetProperty("jobType").GetString());
        Assert.Equal("Queued", payload.RootElement.GetProperty("status").GetString());
    }

    private static async Task EnqueueHeadingBodyGenerationAsync(IPage page, Guid articleId, Guid headingId)
    {
        var response = await PostJsonAsync(
            page,
            $"/api/articles/{articleId}/generation/headings/{headingId}/body",
            new
            {
                generationModel = "gemini-3.5-flash",
                targetLength = 200,
                useWebSearch = false,
                additionalPrompt = "E2E smoke"
            });

        Assert.Equal(202, response.Status);
        using var payload = JsonDocument.Parse(response.Body);
        Assert.Equal("BodyGeneration", payload.RootElement.GetProperty("jobType").GetString());
        Assert.Equal("Queued", payload.RootElement.GetProperty("status").GetString());
    }

    private static async Task VerifyWordpressPostFlowAsync(
        IPage page,
        Guid articleId,
        Guid siteId,
        string title)
    {
        await page.GotoAsync("/articles");
        await WaitForInteractiveRenderAsync(page);
        await FillAndChangeAsync(page.Locator("#search-q"), title);
        await page.GetByRole(AriaRole.Button, new() { Name = "検索" }).ClickAsync();

        var articleRow = page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = title });
        await Expect(articleRow).ToHaveCountAsync(1);
        await Expect(articleRow.GetByRole(AriaRole.Button, new() { Name = "投稿" })).ToBeEnabledAsync();

        var response = await PostJsonAsync(
            page,
            $"/api/articles/{articleId}/wordpress-posts",
            new
            {
                wordpressSiteId = siteId,
                title,
                htmlBody = "<h2>E2E見出し</h2><p>E2E本文</p>",
                categoryId = 7,
                status = "Draft"
            });

        Assert.Equal(202, response.Status);
        using var payload = JsonDocument.Parse(response.Body);
        Assert.Equal("WordpressPost", payload.RootElement.GetProperty("jobType").GetString());
        Assert.Equal("Queued", payload.RootElement.GetProperty("status").GetString());
    }

    private static async Task CreateAndDeleteUserAsync(IPage page, string suffix)
    {
        var email = $"delete-target-{suffix}@example.test";
        await page.GotoAsync("/admin/users");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "ユーザー管理" })).ToBeVisibleAsync();
        await WaitForInteractiveRenderAsync(page);

        await FillAndChangeAsync(page.Locator("#new-email"), email);
        await FillAndChangeAsync(page.Locator("#new-display-name"), $"Delete Target {suffix}");
        await FillAndChangeAsync(page.Locator("#new-password"), "Change-this-e2e-user-123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "追加" }).ClickAsync();
        await Expect(page.GetByText("ユーザーを作成しました。")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = email })).ToHaveCountAsync(1);

        var userRow = page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = email });
        await userRow.GetByRole(AriaRole.Button, new() { Name = "削除" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync(email);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "削除" }).ClickAsync();

        await Expect(page.GetByText("ユーザーを削除しました。")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = email })).ToHaveCountAsync(0);
    }

    private static async Task<ApiResponse> PostJsonAsync(IPage page, string path, object body)
    {
        return await page.EvaluateAsync<ApiResponse>(
            """
            async ({ path, body }) => {
                const tokenResponse = await fetch('/api/security/antiforgery-token', { credentials: 'same-origin' });
                const token = await tokenResponse.json();
                const response = await fetch(path, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: {
                        'content-type': 'application/json',
                        [token.headerName]: token.requestToken
                    },
                    body: JSON.stringify(body)
                });
                return {
                    status: response.status,
                    body: await response.text()
                };
            }
            """,
            new { path, body });
    }

    private static async Task FillAndChangeAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value);
        await locator.DispatchEventAsync("change");
        await Expect(locator).ToHaveValueAsync(value);
    }

    private static async Task WaitForInteractiveRenderAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(750);
    }

    private sealed class ApiResponse
    {
        public int Status { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    [GeneratedRegex("/articles/(?<id>[0-9a-fA-F-]{36})")]
    private static partial Regex ArticleUrlPattern();
}
