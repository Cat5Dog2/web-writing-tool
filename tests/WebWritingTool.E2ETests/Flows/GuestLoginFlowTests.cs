using Microsoft.Playwright;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed class GuestE2ETestFixture() : E2ETestFixture(false, enableGuestJobs: true);

[Trait("Category", "E2E")]
public sealed class GuestLoginFlowTests(GuestE2ETestFixture fixture) : IClassFixture<GuestE2ETestFixture>
{
    [Fact]
    public async Task Guest_BulkCreate_AutomaticallyCompletesArticlesWithWebAndX()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(Guest_BulkCreate_AutomaticallyCompletesArticlesWithWebAndX));
        var page = session.Page;
        try
        {
            await page.GotoAsync("/login");
            await page.GetByRole(AriaRole.Button, new() { Name = "ゲストとして試す" }).ClickAsync();
            await page.WaitForURLAsync("**/articles");
            await page.GotoAsync("/articles", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Button, new() { Name = "一括作成", Exact = true }).ClickAsync();
            await page.Locator("#bulk-lines").FillAsync("家庭菜園\n整理整頓|一括ゲスト整理");
            await Expect(page.Locator("#bulk-generation-scope")).ToHaveValueAsync("FullArticle");
            await page.Locator("#bulk-x-search").SetCheckedAsync(true);
            await page.Locator("summary").Filter(new() { HasText = "記事の構成と生成設定" }).ClickAsync();
            await page.Locator("#bulk-h2").FillAsync("1");
            await page.Locator("#bulk-h2").DispatchEventAsync("change");
            await page.Locator("#bulk-h3").FillAsync("0");
            await page.Locator("#bulk-h3").DispatchEventAsync("change");
            await page.Locator("#bulk-post-settings summary").ClickAsync();
            await Expect(page.Locator("#bulk-auto-post")).ToBeDisabledAsync();
            await page.Locator("#bulk-submit").ClickAsync();
            await Expect(page.Locator(".alert-success")).ToContainTextAsync("2件の記事を登録しました。");
            await Expect(page.GetByTestId("bulk-generation-progress")).ToContainTextAsync("完了 2 件", new() { Timeout = 60000 });

            var articleLinks = await page.Locator(".article-table tbody a.fw-semibold").AllAsync();
            Assert.Equal(2, articleLinks.Count);
            var urls = new List<string>();
            foreach (var link in articleLinks)
            {
                urls.Add((await link.GetAttributeAsync("href"))!);
            }

            foreach (var url in urls)
            {
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
                await Expect(page.Locator(".article-outline-tree button")).ToHaveCountAsync(1,
                    new() { Timeout = 30000 });
                await Expect(page.Locator(".article-outline-tree button")).ToContainTextAsync("H2");
                await Expect(page.GetByTestId("bulk-generation-progress")).ToContainTextAsync("記事完成");
                await Expect(page.Locator(".article-editor-page h1")).Not.ToHaveTextAsync("生成結果編集");
                await page.SetViewportSizeAsync(320, 740);
                await Expect(page.Locator(".bulk-complete-heading")).ToContainTextAsync("記事完成・保存済み");
                Assert.True(await page.GetByTestId("bulk-generation-progress").EvaluateAsync<bool>("el => el.getBoundingClientRect().height < 160"));
                Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
                await page.SetViewportSizeAsync(1280, 900);
                await Expect(page.Locator("#heading-body")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("ダミー"));
                await Expect(page.GetByRole(AriaRole.Button, new() { Name = "本文を生成", Exact = true })).ToBeEnabledAsync();
                await page.Locator("summary").Filter(new() { HasText = "検索・参考情報" }).ClickAsync();
                await Expect(page.GetByTestId("research-web-results")).ToContainTextAsync("サンプル");
                await Expect(page.GetByTestId("research-x-results")).ToContainTextAsync("サンプル");
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
                await Expect(page.Locator(".article-outline-tree button")).ToHaveCountAsync(1);
                await Expect(page.GetByTestId("bulk-generation-progress")).ToContainTextAsync("記事完成");
            }
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

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
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "ゲストの記事" }))
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
