using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed partial class MajorScreenFlowTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task E2E003_BulkCreate_WithEmptyInput_PreventsSubmissionWithoutSuccess(string input)
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E003_BulkCreate_WithEmptyInput_PreventsSubmissionWithoutSuccess)
            + (input.Length == 0 ? "_empty" : "_whitespace"));
        var page = session.Page;
        var email = await fixture.SeedStandardUserAsync(
            $"bulk-empty-{Guid.NewGuid():N}@example.test", "Bulk Empty");

        try
        {
            await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
            await WaitForInteractiveRenderAsync(page);
            var articleTitles = page.Locator(".article-table tbody a.fw-semibold");
            var originalTitles = await articleTitles.AllTextContentsAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
            await FillAndChangeAsync(page.Locator("#bulk-lines"), input);
            await Expect(page.Locator("#bulk-submit")).ToBeDisabledAsync();
            await Expect(page.Locator(".alert-success")).ToHaveCountAsync(0);
            Assert.Equal(originalTitles, await articleTitles.AllTextContentsAsync());

            await page.ReloadAsync();
            await WaitForInteractiveRenderAsync(page);
            Assert.Equal(originalTitles, await articleTitles.AllTextContentsAsync());
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E003_BulkCreate_WithRejectedLine_ShowsPartialSuccessAndCreatesOnlyValidArticles()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E003_BulkCreate_WithRejectedLine_ShowsPartialSuccessAndCreatesOnlyValidArticles));
        var page = session.Page;
        var prefix = $"e2e-partial-{Guid.NewGuid():N}";
        var keywordOnly = $"{prefix}-first";
        var invalidKeyword = $"{prefix}-invalid";
        var titledKeyword = $"{prefix}-second";
        var title = $"部分成功の記事 {prefix}";
        var email = await fixture.SeedStandardUserAsync($"{prefix}@example.test", "Bulk Partial Success");

        try
        {
            await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
            await WaitForInteractiveRenderAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
            await FillAndChangeAsync(
                page.Locator("#bulk-lines"),
                $"{keywordOnly}\n{invalidKeyword}|タイトル|余分\n{titledKeyword}|{title}");
            await page.Locator("summary").Filter(new() { HasText = "記事の構成と生成設定" }).ClickAsync();
            await page.Locator("#bulk-search").SetCheckedAsync(false);
            await page.Locator("#bulk-submit").ClickAsync();

            await Expect(page.Locator(".alert-success")).ToHaveTextAsync("2件の記事を登録しました。自動生成を開始しました。");
            await Expect(page.Locator(".alert-warning")).ToHaveTextAsync(
                "2 行目: 入力形式は「キーワード」または「キーワード|タイトル」です。");
            await Expect(page.Locator("#bulk-lines")).ToHaveValueAsync($"{invalidKeyword}|タイトル|余分");
            await Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "一括作成", Exact = true })).ToBeVisibleAsync();
            await Expect(page.Locator(".article-table tbody a.fw-semibold").Filter(
                new LocatorFilterOptions { HasText = prefix })).ToHaveCountAsync(2);

            var rows = page.Locator(".article-table tbody tr");
            await Expect(rows).ToHaveCountAsync(2);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = keywordOnly })).ToHaveCountAsync(1);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = title })).ToHaveCountAsync(1);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = invalidKeyword })).ToHaveCountAsync(0);

            await FillAndChangeAsync(page.Locator("#bulk-lines"), $"{invalidKeyword}|修正したタイトル");
            await page.Locator("#bulk-submit").ClickAsync();
            await Expect(page.Locator(".alert-success")).ToContainTextAsync("1件の記事を登録しました。");
            await SearchArticleAsync(page, prefix);
            await Expect(rows).ToHaveCountAsync(3);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = keywordOnly })).ToHaveCountAsync(1);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = title })).ToHaveCountAsync(1);
            await Expect(rows.Filter(new LocatorFilterOptions { HasText = "修正したタイトル" })).ToHaveCountAsync(1);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E003_BulkCreate_WithOnlyRejectedLines_PreservesInputForCorrection()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E003_BulkCreate_WithOnlyRejectedLines_PreservesInputForCorrection));
        var page = session.Page;
        var email = await fixture.SeedStandardUserAsync($"bulk-rejected-{Guid.NewGuid():N}@example.test", "Bulk Rejected");
        const string input = "不正|タイトル|余分\n   \n|タイトルのみ";
        try
        {
            await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
            await WaitForInteractiveRenderAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
            await FillAndChangeAsync(page.Locator("#bulk-lines"), input);
            await page.Locator("#bulk-submit").ClickAsync();
            await Expect(page.Locator(".alert-danger")).ToContainTextAsync("登録できる記事がありませんでした。");
            await Expect(page.Locator("#bulk-lines")).ToHaveValueAsync(input);
            await Expect(page.Locator(".alert-warning")).ToContainTextAsync("1 行目:");
            await Expect(page.Locator(".alert-warning")).Not.ToContainTextAsync("0 行目:");
            await Expect(page.Locator(".alert-success")).ToHaveCountAsync(0);
            await Expect(page.Locator(".article-table tbody a.fw-semibold")).ToHaveCountAsync(0);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E006_HeadingSelection_RendersH2AndH3LevelBadges()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E006_HeadingSelection_RendersH2AndH3LevelBadges));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N");

        try
        {
            await LoginAsync(page);
            var articleId = await CreateArticleAsync(page, $"e2e-badges-{suffix}", $"見出しバッジ確認 {suffix}");
            await CancelInitialOutlineAsync(page, articleId);
            var levelBadge = page.Locator(".article-heading-editor .article-section-header .text-bg-light");
            var headingTitle = page.Locator("#heading-title");

            await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
            await Expect(headingTitle).ToHaveValueAsync("新しいH2");
            await Expect(levelBadge).ToHaveTextAsync("H2");

            await page.GetByRole(AriaRole.Button, new() { Name = "H3追加", Exact = true }).ClickAsync();
            await Expect(headingTitle).ToHaveValueAsync("新しいH3");
            await Expect(levelBadge).ToHaveTextAsync("H3");

            var h2 = page.Locator(".article-outline-tree button").Filter(new LocatorFilterOptions { HasText = "新しいH2" });
            var h3 = page.Locator(".article-outline-tree button").Filter(new LocatorFilterOptions { HasText = "新しいH3" });
            await h2.ClickAsync();
            await Expect(headingTitle).ToHaveValueAsync("新しいH2");
            await Expect(levelBadge).ToHaveTextAsync("H2");
            await h3.ClickAsync();
            await Expect(headingTitle).ToHaveValueAsync("新しいH3");
            await Expect(levelBadge).ToHaveTextAsync("H3");

            await page.ReloadAsync();
            await WaitForInteractiveRenderAsync(page);
            await h3.ClickAsync();
            await Expect(headingTitle).ToHaveValueAsync("新しいH3");
            await Expect(levelBadge).ToHaveTextAsync("H3");
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }
}
