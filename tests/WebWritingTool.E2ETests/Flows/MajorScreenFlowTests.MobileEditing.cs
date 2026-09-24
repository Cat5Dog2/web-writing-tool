using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed partial class MajorScreenFlowTests
{
    [Fact]
    public async Task MobileEditing_DeleteHeading_ConfirmsVisibleTargetAndPreservesSibling()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_DeleteHeading_ConfirmsVisibleTargetAndPreservesSibling));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await SaveMobileHeadingAsync(page, "削除対象の親", "親の本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "H3追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH3");
        await SaveMobileHeadingAsync(page, "配下の子", "子の本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH2");
        await SaveMobileHeadingAsync(page, "残す見出し", "残す本文");
        await page.SetViewportSizeAsync(320, 740);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "削除", Exact = true })).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "見出しの操作", Exact = true }).ClickAsync();
        foreach (var name in new[] { "見出しの操作", "上へ移動", "下へ移動", "削除" })
            Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).EvaluateAsync<bool>("el => el.getBoundingClientRect().height >= 44"));
        await page.Locator(".article-heading-editor").GetByRole(AriaRole.Button, new() { Name = "削除", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "見出しを削除", Exact = true });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToContainTextAsync("削除対象の親");
        await Expect(dialog).ToContainTextAsync("配下のH3 1件");
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("親の本文");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "見出しの操作", Exact = true })).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "見出しの操作", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "削除", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "削除する", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("残す見出し");
        await page.ReloadAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("残す本文");
        await Expect(page.Locator(".heading-position")).ToHaveTextAsync("1 / 1");
    }

    [Fact]
    public async Task MobileEditing_AdjacentHeadings_PreserveUnsavedGuardAndBoundaries()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_AdjacentHeadings_PreserveUnsavedGuardAndBoundaries));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await SaveMobileHeadingAsync(page, "最初の見出し", "最初の本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH2");
        await SaveMobileHeadingAsync(page, "次の見出し", "次の本文");
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        var previous = page.GetByRole(AriaRole.Button, new() { Name = "前の見出し", Exact = true });
        var next = page.GetByRole(AriaRole.Button, new() { Name = "次の見出し", Exact = true });
        await Expect(previous).ToBeDisabledAsync();
        await Expect(page.Locator(".heading-position")).ToHaveTextAsync("1 / 2");
        await page.Locator("#heading-body").FillAsync("移動前に保存する本文");
        await next.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "未保存の変更" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "キャンセル", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("移動前に保存する本文");
        await Expect(page.Locator(".heading-position")).ToHaveTextAsync("1 / 2");
        await next.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "保存して続ける", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("次の本文");
        await Expect(next).ToBeDisabledAsync();
        await Expect(page.Locator(".heading-position")).ToHaveTextAsync("2 / 2");
        await previous.ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("移動前に保存する本文");
        await page.ReloadAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("移動前に保存する本文");
    }

    [Fact]
    public async Task MobileEditing_LandscapeFocus_ShowsBodyAndRestoresNavigationWithoutLosingInput()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_LandscapeFocus_ShowsBodyAndRestoresNavigationWithoutLosingInput));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.SetViewportSizeAsync(667, 375);
        await page.GotoAsync($"/articles/{id}");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "全体を表示", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".navbar-toggler")).ToBeHiddenAsync();
        Assert.True(await page.Locator("#heading-body").EvaluateAsync<bool>("el => el.getBoundingClientRect().top < document.querySelector('.article-editor-actions').getBoundingClientRect().top - 80"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "mobile-editing-landscape.png") });
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator(".navbar-toggler")).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(667, 375);
        await Expect(page.Locator(".navbar-toggler")).ToBeHiddenAsync();
        await page.Locator("#heading-body").FillAsync("集中表示でも保持する入力");
        await page.GetByRole(AriaRole.Button, new() { Name = "全体を表示", Exact = true }).ClickAsync();
        await Expect(page.Locator(".navbar-toggler")).ToBeVisibleAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("集中表示でも保持する入力");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文に集中", Exact = true }).ClickAsync();
        await Expect(page.Locator(".navbar-toggler")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "変更をまとめて保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.GotoAsync("/articles");
        await Expect(page.Locator(".navbar-toggler")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task MobileEditing_SaveFeedback_StaysVisibleOnSuccessAndFailure()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_SaveFeedback_StaysVisibleOnSuccessAndFailure));
        var page = session.Page;
        await OpenEditorWithHeadingAsync(page);
        await page.SetViewportSizeAsync(390, 844);
        await page.Locator("#heading-body").FillAsync("スマホで保存する本文");
        await page.Locator(".article-editor-danger").ScrollIntoViewIfNeededAsync();
        await Expect(page.Locator(".editor-save-state")).ToBeInViewportAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("未保存の変更があります");
        var save = page.GetByRole(AriaRole.Button, new() { Name = "変更をまとめて保存", Exact = true });
        await save.ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await Expect(page.Locator(".editor-save-state")).ToBeInViewportAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "記事情報を編集", Exact = true }).ClickAsync();
        await page.Locator("#keyword").FillAsync("");
        await page.Locator("#heading-body").FillAsync("失敗しても失わない入力");
        await save.ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存に失敗しました");
        await Expect(page.Locator(".editor-save-state")).ToBeInViewportAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true })).ToBeInViewportAsync(new() { Ratio = 1 });
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("失敗しても失わない入力");
        await File.WriteAllTextAsync(Path.Combine(fixture.TestResultsDirectory, "mobile-editing-save-layout.json"), await page.EvaluateAsync<string>("""
            JSON.stringify({ width: innerWidth, height: innerHeight, scrollY,
                viewport: { height: visualViewport.height, offsetTop: visualViewport.offsetTop, scale: visualViewport.scale },
                bar: document.querySelector('.article-editor-actions').getBoundingClientRect().toJSON(),
                buttons: document.querySelector('.editor-save-buttons').getBoundingClientRect().toJSON() })
            """));
        Assert.True(await page.Locator(".editor-save-buttons").EvaluateAsync<bool>("el => el.getBoundingClientRect().bottom <= innerHeight"));
        await page.GetByRole(AriaRole.Button, new() { Name = "エラーを確認", Exact = true }).ClickAsync();
        await Expect(page.Locator("#editor-error")).ToBeFocusedAsync();
        await Expect(page.Locator("#editor-error")).ToBeInViewportAsync();
        await page.Locator("#keyword").FillAsync("修正したキーワード");
        await save.ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "エラーを確認", Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MobileEditing_ErrorFocus_StopsEarlierSmoothScroll()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_ErrorFocus_StopsEarlierSmoothScroll));
        var page = session.Page;
        await OpenEditorWithHeadingAsync(page);
        await page.SetViewportSizeAsync(390, 844);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"/js/appDialog(?:\.[a-z0-9]+)?\.js$"), route => route.FulfillAsync(new()
        {
            ContentType = "text/javascript",
            Body = """
                export * from './appDialog.js?scroll-regression';
                import { focus as originalFocus } from './appDialog.js?scroll-regression';
                export async function focus(id, ...args) {
                    if (id === 'editor-error')
                        await new Promise(resolve => { window.releaseErrorFocus = resolve; });
                    originalFocus(id, ...args);
                    if (id === 'editor-error') window.errorFocusCompleted = true;
                }
                """
        }));
        await page.GetByRole(AriaRole.Button, new() { Name = "記事情報を編集", Exact = true }).ClickAsync();
        await Expect(page.Locator("#article-meta")).ToBeFocusedAsync();
        await page.Locator("#keyword").FillAsync("");
        await page.GetByRole(AriaRole.Button, new() { Name = "変更をまとめて保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存に失敗しました");
        await page.GetByRole(AriaRole.Button, new() { Name = "エラーを確認", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("typeof window.releaseErrorFocus === 'function'");
        // Resume error focus while the error is still visible and an earlier scroll is moving away.
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                window.scrollTo({ top: 0, behavior: 'instant' });
                await new Promise(requestAnimationFrame);
                window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'smooth' });
                while (scrollY === 0) await new Promise(requestAnimationFrame);
                const rect = document.querySelector('#editor-error').getBoundingClientRect();
                const visible = rect.top >= 0 && rect.bottom <= innerHeight;
                document.addEventListener('scrollend', () => { window.errorScrollEnded = true; }, { once: true });
                window.releaseErrorFocus();
                return visible;
            }
            """));
        await page.WaitForFunctionAsync("window.errorFocusCompleted === true && window.errorScrollEnded === true");
        await Expect(page.Locator("#editor-error")).ToBeFocusedAsync();
        await Expect(page.Locator("#editor-error")).ToBeInViewportAsync(new() { Ratio = 1 });
        await Expect(page.Locator("#editor-error")).ToContainTextAsync("1から200文字で入力してください。");
    }

    [Theory]
    [InlineData(410, 0)]
    [InlineData(300, 60)]
    public async Task MobileEditing_VisualViewport_KeepsInputAndSaveAboveKeyboard(int visibleHeight, int offsetTop)
    {
        await using var session = await fixture.CreateSessionAsync($"{nameof(MobileEditing_VisualViewport_KeepsInputAndSaveAboveKeyboard)}_{visibleHeight}");
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.AddInitScriptAsync("""
            const viewport = new EventTarget();
            Object.assign(viewport, { height: innerHeight, width: innerWidth, offsetTop: 0, offsetLeft: 0, scale: 1 });
            Object.defineProperty(window, 'visualViewport', { get: () => viewport });
            """);
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await page.Locator("#heading-body").FillAsync("キーボード表示中にも維持する本文");
        await page.EvaluateAsync("v => { visualViewport.height = v.visibleHeight; visualViewport.offsetTop = v.offsetTop; visualViewport.dispatchEvent(new Event('resize')); }", new { visibleHeight, offsetTop });
        await page.WaitForFunctionAsync("""
            () => {
                const actions = document.querySelector('.article-editor-actions').getBoundingClientRect();
                const input = document.querySelector('#heading-body').getBoundingClientRect();
                return actions.bottom <= visualViewport.height + visualViewport.offsetTop + 1
                    && actions.top >= visualViewport.offsetTop
                    && input.top >= visualViewport.offsetTop - 1 && input.bottom <= actions.top;
            }
            """);
        await Expect(page.Locator("#heading-body")).ToBeFocusedAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("キーボード表示中にも維持する本文");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, $"mobile-editing-viewport-{visibleHeight}.png") });
        await page.EvaluateAsync("() => { visualViewport.offsetTop = 90; visualViewport.dispatchEvent(new Event('scroll')); }");
        await page.WaitForFunctionAsync("document.querySelector('.article-editor-actions').getBoundingClientRect().bottom <= visualViewport.height + 91");
        await page.EvaluateAsync("() => { visualViewport.height = innerHeight; visualViewport.offsetTop = 0; visualViewport.dispatchEvent(new Event('resize')); }");
        await page.Locator("#heading-body").BlurAsync();
        await page.WaitForFunctionAsync("Math.abs(document.querySelector('.article-editor-actions').getBoundingClientRect().bottom - innerHeight) < 2");
        await page.GetByRole(AriaRole.Button, new() { Name = "変更をまとめて保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
    }

    [Fact]
    public async Task MobileEditing_ViewportFallbackAndZoom_PreserveEditingAndNavigation()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(MobileEditing_ViewportFallbackAndZoom_PreserveEditingAndNavigation));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.AddInitScriptAsync("""
            Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => undefined });
            """);
        await page.SetViewportSizeAsync(390, 370);
        await page.GotoAsync($"/articles/{id}");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "全体を表示", Exact = true })).ToBeVisibleAsync();
        await page.Locator("#heading-body").FillAsync("表示領域APIがない端末でも保存する本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "変更をまとめて保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator(".navbar-toggler")).ToBeVisibleAsync();
        page = await page.Context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.AddInitScriptAsync("""
            const viewport = new EventTarget();
            Object.assign(viewport, { height: 422, width: 195, offsetTop: 90, offsetLeft: 0, scale: 2 });
            Object.defineProperty(window, 'visualViewport', { get: () => viewport });
            """);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("表示領域APIがない端末でも保存する本文");
        await Expect(page.Locator(".article-editor-page")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("editor-keyboard-open"));
        Assert.Equal("0px", await page.Locator(".article-editor-actions").EvaluateAsync<string>("el => getComputedStyle(el).bottom"));
        Assert.DoesNotContain("user-scalable=no", await page.Locator("meta[name=viewport]").GetAttributeAsync("content"));
        Assert.DoesNotContain("maximum-scale", await page.Locator("meta[name=viewport]").GetAttributeAsync("content"));
    }

    private static async Task SaveMobileHeadingAsync(IPage page, string title, string body)
    {
        await page.Locator("#heading-title").FillAsync(title);
        await page.Locator("#heading-body").FillAsync(body);
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
    }
}
