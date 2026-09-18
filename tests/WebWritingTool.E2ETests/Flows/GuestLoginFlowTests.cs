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
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" })).ToBeEnabledAsync();
            await page.Locator("#title").FillAsync("ゲストの記事");
            await page.Locator("#title").DispatchEventAsync("change");
            await page.Locator("#search-mode").SetCheckedAsync(true);
            await create.ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" }))
                .ToBeVisibleAsync(new() { Timeout = 30000 });

            await page.Locator("summary").Filter(new() { HasText = "検索・参考情報" }).ClickAsync();
            var web = page.GetByRole(AriaRole.Button, new() { Name = "Web検索を実行" });
            await Expect(web).ToBeEnabledAsync();
            await web.ClickAsync();
            await Expect(page.GetByTestId("research-web-results")).ToContainTextAsync("サンプル", new() { Timeout = 30000 });
            await Expect(web).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "X検索を実行" }).ClickAsync();
            await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル投稿", new() { Timeout = 30000 });
            var outline = page.GetByRole(AriaRole.Button, new() { Name = "構成を生成", Exact = true });
            await Expect(outline).ToBeDisabledAsync();

            var body = page.GetByRole(AriaRole.Button, new() { Name = "本文を生成", Exact = true });
            await Expect(body).ToBeEnabledAsync(new() { Timeout = 30000 });
            await body.ClickAsync();
            await Expect(page.Locator("#heading-body")).ToHaveValueAsync(
                new System.Text.RegularExpressions.Regex("ダミー"), new() { Timeout = 30000 });
            await Expect(body).ToBeEnabledAsync();
            await page.ReloadAsync();
            await page.Locator("summary").Filter(new() { HasText = "検索・参考情報" }).ClickAsync();
            await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル投稿");
            await page.GotoAsync("/settings");
            await Expect(page.GetByText("認証情報の入力は不要です。", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(page.Locator("#app-pass, #discord-webhook-url")).ToHaveCountAsync(0);
            await page.GotoAsync("/account");
            await Expect(page.GetByText("残り約", new() { Exact = false })).ToBeVisibleAsync();
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
