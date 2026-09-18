using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed class GuestE2ETestFixture() : E2ETestFixture(false, enableGuestJobs: true);

[Trait("Category", "E2E")]
public sealed class GuestLoginFlowTests(GuestE2ETestFixture fixture) : IClassFixture<GuestE2ETestFixture>
{
    [Fact]
    public async Task Guest_WithGlobalMocksDisabled_CanSearchAndGenerateSamples()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(Guest_WithGlobalMocksDisabled_CanSearchAndGenerateSamples));
        var page = session.Page;
        try
        {
            await page.GotoAsync("/login");
            await page.GetByRole(AriaRole.Button, new() { Name = "ゲストとして試す" }).ClickAsync();
            await page.WaitForURLAsync("**/articles");
            await Expect(page.GetByText("ゲストモード", new() { Exact = true })).ToBeVisibleAsync();
            await page.GotoAsync("/articles/create", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var create = page.GetByRole(AriaRole.Button, new() { Name = "構成を作成", Exact = true });
            await Expect(page.Locator("#generation-model option")).Not.ToHaveCountAsync(0);
            await page.Locator("#keyword").FillAsync("家庭菜園 ゲスト体験");
            await page.Locator("#keyword").DispatchEventAsync("change");
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "記事タイトル候補を出す" })).ToBeEnabledAsync();
            await page.Locator("#title").FillAsync("ゲストの記事");
            await page.Locator("#title").DispatchEventAsync("change");
            await page.Locator("#search-mode").SetCheckedAsync(true);
            await create.ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" }))
                .ToBeVisibleAsync(new() { Timeout = 30000 });

            var web = page.GetByRole(AriaRole.Button, new() { Name = "Web検索を実行" });
            await Expect(web).ToBeEnabledAsync();
            await web.ClickAsync();
            await Expect(page.GetByTestId("research-web-results")).ToContainTextAsync("サンプル", new() { Timeout = 30000 });
            await Expect(web).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "X検索を実行" }).ClickAsync();
            await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル投稿", new() { Timeout = 30000 });
            var outline = page.GetByRole(AriaRole.Button, new() { Name = "構成を生成", Exact = true });
            await Expect(outline).ToBeEnabledAsync();
            await outline.ClickAsync();

            var body = page.GetByRole(AriaRole.Button, new() { Name = "本文を生成", Exact = true });
            await Expect(body).ToBeEnabledAsync(new() { Timeout = 30000 });
            await body.ClickAsync();
            await Expect(page.Locator("#heading-body")).ToHaveValueAsync(
                new System.Text.RegularExpressions.Regex("ダミー"), new() { Timeout = 30000 });
            await Expect(body).ToBeEnabledAsync();
            await page.ReloadAsync();
            await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル投稿");
            await page.GetByRole(AriaRole.Button, new() { Name = "ログアウト", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/login");
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }
}
