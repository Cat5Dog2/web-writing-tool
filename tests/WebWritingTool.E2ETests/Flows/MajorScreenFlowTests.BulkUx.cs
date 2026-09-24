using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed partial class MajorScreenFlowTests
{
    [Fact]
    public async Task BulkUx_InvalidCount_FocusesVisibleFieldErrorOnMobile()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(BulkUx_InvalidCount_FocusesVisibleFieldErrorOnMobile));
        var page = session.Page;
        var email = await fixture.SeedStandardUserAsync($"bulk-validation-{Guid.NewGuid():N}@example.test", "Validation");
        await page.SetViewportSizeAsync(390, 844);
        await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
        await FillAndChangeAsync(page.Locator("#bulk-lines"), "家庭菜園");
        await page.Locator(".article-page details summary").First.ClickAsync();
        await FillAndChangeAsync(page.Locator("#bulk-h2"), "0");
        await page.Locator("#bulk-submit").ClickAsync();
        await Expect(page.Locator("#bulk-h2")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#bulk-h2")).ToBeFocusedAsync();
        await Expect(page.Locator("#bulk-h2-error")).ToContainTextAsync("H2");
        Assert.True(await page.Locator("#bulk-h2-error").EvaluateAsync<bool>("el => el.getBoundingClientRect().top >= 0 && el.getBoundingClientRect().bottom <= innerHeight"));
        await FillAndChangeAsync(page.Locator("#bulk-h2"), "1");
        await page.Locator("#bulk-submit").ClickAsync();
        await Expect(page.Locator("#bulk-lines")).ToHaveCountAsync(0);
        await Expect(page.Locator(".article-table tbody tr")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task BulkUx_NewBatch_KeepsStoppedArticleAndHistoryReachable()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(BulkUx_NewBatch_KeepsStoppedArticleAndHistoryReachable));
        var page = session.Page;
        var email = await fixture.SeedStandardUserAsync($"bulk-history-{Guid.NewGuid():N}@example.test", "History");
        await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
        await FillAndChangeAsync(page.Locator("#bulk-lines"), "停止する記事");
        await page.Locator("#bulk-submit").ClickAsync();
        var panel = page.GetByTestId("bulk-generation-progress");
        await panel.GetByRole(AriaRole.Button, new() { Name = "停止", Exact = true }).ClickAsync();
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "未完了部分から再開", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
        await FillAndChangeAsync(page.Locator("#bulk-lines"), "次に登録する記事");
        await page.Locator("#bulk-submit").ClickAsync();
        await Expect(page.Locator("#bulk-lines")).ToHaveCountAsync(0);
        await Expect(panel.GetByRole(AriaRole.Link, new() { Name = "停止する記事", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("#bulk-batch option")).ToHaveCountAsync(2);
        var firstBatch = await page.Locator("#bulk-batch option").Last.GetAttributeAsync("value");
        await page.Locator("#bulk-batch").SelectOptionAsync(firstBatch!);
        await Expect(page.Locator("#bulk-batch")).ToHaveValueAsync(firstBatch!);
        await page.ReloadAsync();
        await WaitForInteractiveRenderAsync(page);
        var stoppedRow = page.Locator(".article-table tr").Filter(new() { HasText = "停止する記事" });
        await Expect(stoppedRow).ToContainTextAsync("自動生成を停止中");
        await page.SetViewportSizeAsync(390, 844);
        var retry = panel.GetByRole(AriaRole.Button, new() { Name = "未完了部分から再開", Exact = true });
        await Expect(retry).ToBeVisibleAsync();
        Assert.True(await retry.EvaluateAsync<bool>("el => el.getBoundingClientRect().height >= 44"));
        await stoppedRow.GetByRole(AriaRole.Button, new() { Name = "再開", Exact = true }).ClickAsync();
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "未完了部分から再開", Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task BulkUx_PreviewContents_StaysOnArticleAndScrollsToHeading()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(BulkUx_PreviewContents_StaysOnArticleAndScrollsToHeading));
        var page = session.Page;
        var id = await fixture.SeedResearchArticleAsync($"目次確認 {Guid.NewGuid():N}");
        await LoginAsync(page);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await page.Locator("#heading-title").FillAsync("保存の方法");
        await page.Locator("#heading-body").FillAsync("本文から目次への移動を確認します。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true }).ClickAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GetByRole(AriaRole.Navigation, new() { Name = "記事の目次" })
            .GetByRole(AriaRole.Link, new() { Name = "保存の方法", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/articles/{id}/preview#preview-section-1$"));
        await Expect(page.Locator("#preview-section-1")).ToBeInViewportAsync();
    }
}
