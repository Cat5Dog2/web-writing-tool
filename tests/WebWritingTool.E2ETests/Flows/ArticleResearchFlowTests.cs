using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed class ResearchE2ETestFixture() : E2ETestFixture(true);

[Trait("Category", "E2E")]
public sealed class ArticleResearchFlowTests : IClassFixture<ResearchE2ETestFixture>
{
    private readonly ResearchE2ETestFixture fixture;

    public ArticleResearchFlowTests(ResearchE2ETestFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DummyResearch_SearchDisplaysSamplesAndGenerationUsesThem()
    {
        var keyword = "家庭菜園 " + Guid.NewGuid().ToString("N")[..8];
        var manualQuery = keyword + " 手動で選んだ水やり資料";
        var articleId = await fixture.SeedResearchArticleAsync(keyword);
        await using var session = await fixture.CreateSessionAsync(nameof(DummyResearch_SearchDisplaysSamplesAndGenerationUsesThem));
        var page = session.Page;
        try
        {
            await page.GotoAsync("/login");
            await page.Locator("#email").FillAsync(E2ETestFixture.AdminEmail);
            await page.Locator("#password").FillAsync(E2ETestFixture.AdminPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "ログイン", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/articles");
            await page.GotoAsync($"/articles/{articleId}");
            var webButton = page.GetByRole(AriaRole.Button, new() { Name = "Web検索を実行" });
            await Expect(webButton).ToBeEnabledAsync();
            await Expect(page.Locator("#research-query")).ToHaveValueAsync(keyword);
            await page.Locator("#research-query").FillAsync(manualQuery);
            await page.Locator("#research-count").FillAsync("3");
            await page.Locator("#research-count").BlurAsync();
            await webButton.ClickAsync();
            await Expect(page.GetByTestId("research-web-results")).ToContainTextAsync("Web検索結果（3 件）", new() { Timeout = 30000 });
            await Expect(page.GetByTestId("research-web-results").GetByText("手動取得", new() { Exact = true })).ToHaveCountAsync(3);
            await Expect(webButton).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "X検索を実行" }).ClickAsync();
            await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル投稿", new() { Timeout = 30000 });
            await Expect(webButton).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "構成を生成", Exact = true }).ClickAsync();
            var bodyButton = page.GetByRole(AriaRole.Button, new() { Name = "本文を生成", Exact = true });
            await Expect(bodyButton).ToBeEnabledAsync(new() { Timeout = 30000 });
            var scopeLabels = await page.Locator("#research-heading option").AllTextContentsAsync();
            Assert.Contains(scopeLabels, label => label.StartsWith("H2 "));
            Assert.Contains(scopeLabels, label => label.StartsWith("H3 "));
            Assert.All(scopeLabels.Skip(1), label => Assert.Matches("^H[23] ", label));
            await bodyButton.ClickAsync();
            await Expect(page.Locator("#heading-body")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("参考情報（動作確認用）"), new() { Timeout = 30000 });
            await Expect(page.Locator("#heading-body")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(manualQuery));
            await Expect(bodyButton).ToBeEnabledAsync();
            await page.SetViewportSizeAsync(390, 844);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "research-mobile.png"), FullPage = true });
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }
}
