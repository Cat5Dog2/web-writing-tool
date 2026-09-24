using Microsoft.Playwright;
using WebWritingTool.Domain.Jobs;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed partial class MajorScreenFlowTests
{
    [Fact]
    public async Task EditorUx_SaveAndPreview_SavesMetadataAndBodyAndRefreshesPreview()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_SaveAndPreview_SavesMetadataAndBodyAndRefreshesPreview));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "記事情報を編集", Exact = true }).ClickAsync();
        await page.Locator("#title").FillAsync("保存とプレビューを一度で行う記事");
        await page.Locator("#heading-title").FillAsync("最初の見出し");
        await page.Locator("#heading-body").FillAsync("プレビューへ反映する未保存の本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/articles/{id}/preview$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "保存とプレビューを一度で行う記事", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".article-preview-body")).ToContainTextAsync("プレビューへ反映する未保存の本文です。");
        await page.GetByRole(AriaRole.Link, new() { Name = "編集に戻る", Exact = true }).First.ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("プレビューへ反映する未保存の本文です。");
        await page.Locator("#heading-body").FillAsync("次に保存した本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.GotoAsync($"/articles/{id}/preview");
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("プレビューの更新が必要です");
        await Expect(page.Locator(".article-preview-body")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "編集に戻る", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true }).ClickAsync();
        await Expect(page.Locator(".article-preview-body")).ToContainTextAsync("次に保存した本文です。");
    }

    [Fact]
    public async Task EditorUx_SaveAndPreview_InvalidMetadataKeepsUnsavedInput()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_SaveAndPreview_InvalidMetadataKeepsUnsavedInput));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "記事情報を編集", Exact = true }).ClickAsync();
        await Expect(page.Locator("#article-meta")).ToBeFocusedAsync();
        await page.Locator("#keyword").FillAsync("");
        await Expect(page.Locator("#keyword")).ToHaveValueAsync("");
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("未保存の変更があります");
        await page.Locator("#heading-body").FillAsync("保存失敗でも失わない本文");
        var preview = page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true });
        await preview.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("1から200文字で入力してください。");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/articles/{id}$"));
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("保存失敗でも失わない本文");
        await Expect(preview).ToBeEnabledAsync();
        await page.Locator("#keyword").FillAsync("修正したキーワード");
        await preview.ClickAsync();
        await Expect(page.Locator(".article-preview-body")).ToContainTextAsync("保存失敗でも失わない本文");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorUx_MetadataFocus_DelayedInitializationRespectsCurrentFocus(bool editingKeyword)
    {
        await using var session = await fixture.CreateSessionAsync($"{nameof(EditorUx_MetadataFocus_DelayedInitializationRespectsCurrentFocus)}_{editingKeyword}");
        var page = session.Page;
        await OpenEditorWithHeadingAsync(page);
        // Delay the actual JS call so the user can select text before panel focus arrives.
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"/js/appDialog(?:\.[a-z0-9]+)?\.js$"), route => route.FulfillAsync(new()
        {
            ContentType = "text/javascript",
            Body = """
                export * from './appDialog.js?focus-regression';
                import { focus as originalFocus } from './appDialog.js?focus-regression';
                export async function focus(...args) {
                    await new Promise(resolve => { window.releaseEditorFocus = resolve; });
                    originalFocus(...args);
                    window.editorFocusCompleted = true;
                }
                """
        }));
        await page.GetByRole(AriaRole.Button, new() { Name = "記事情報を編集", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("typeof window.releaseEditorFocus === 'function'");
        var keyword = page.Locator("#keyword");
        await Expect(keyword).ToHaveValueAsync("暮らしを整えるための実践ガイド");
        if (editingKeyword) await keyword.SelectTextAsync();
        await page.EvaluateAsync("window.releaseEditorFocus()");
        await page.WaitForFunctionAsync("window.editorFocusCompleted === true");
        if (!editingKeyword)
        {
            await Expect(page.Locator("#article-meta")).ToBeFocusedAsync();
            await keyword.SelectTextAsync();
        }
        await page.Keyboard.PressAsync("Delete");
        await Expect(keyword).ToHaveValueAsync("");
        await Expect(keyword).ToBeFocusedAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("未保存の変更があります");
    }

    [Fact]
    public async Task EditorUx_SaveAndPreview_EmptyArticleStaysEditableAfterConversionFailure()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_SaveAndPreview_EmptyArticleStaysEditableAfterConversionFailure));
        var page = session.Page;
        var id = await fixture.SeedResearchArticleAsync($"空の記事 {Guid.NewGuid():N}");
        await LoginAsync(page);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        var preview = page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true });
        await preview.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("本文がありません");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/articles/{id}$"));
        await Expect(preview).ToBeEnabledAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task EditorUx_Regenerate_ConfirmsTargetAndCancelPreservesBody()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_Regenerate_ConfirmsTargetAndCancelPreservesBody));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.Locator("#heading-title").FillAsync("置き換え対象の見出し");
        await page.Locator("#heading-body").FillAsync("再生成前の大切な本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.ReloadAsync();
        await WaitForInteractiveRenderAsync(page);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "本文を再生成", Exact = true })).ToBeHiddenAsync();
        await page.Locator(".article-generation-tools > summary").ClickAsync();
        await page.Locator(".research-disclosure > summary").ClickAsync();
        var headingId = (await page.Locator("#research-heading option").Nth(1).GetAttributeAsync("value"))!;
        await page.Locator("#research-heading").SelectOptionAsync(headingId);
        var regenerate = page.GetByRole(AriaRole.Button, new() { Name = "本文を再生成", Exact = true });
        await regenerate.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "本文を再生成" });
        await Expect(dialog).ToContainTextAsync("置き換え対象の見出し");
        await Expect(dialog).ToContainTextAsync("保存済み本文を置き換えます");
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(regenerate).ToBeFocusedAsync();
        Assert.Equal(0, await fixture.GetJobCountAsync(id, JobType.BodyGeneration));
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("再生成前の大切な本文");
        await regenerate.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "再生成する", Exact = true }).ClickAsync();
        await Expect(page.Locator(".generation-status")).ToContainTextAsync("実行待ち");
        Assert.Equal(1, await fixture.GetJobCountAsync(id, JobType.BodyGeneration));
        await Expect(page.Locator("#heading-body")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task EditorUx_GenerateAfterSavingNewBody_StillRequiresReplacementConfirmation()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_GenerateAfterSavingNewBody_StillRequiresReplacementConfirmation));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.Locator("#heading-body").FillAsync("空の見出しに手作業で書いた本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を生成", Exact = true }).ClickAsync();
        var unsaved = page.GetByRole(AriaRole.Dialog, new() { Name = "未保存の変更" });
        await unsaved.GetByRole(AriaRole.Button, new() { Name = "保存して続ける", Exact = true }).ClickAsync();
        var replace = page.GetByRole(AriaRole.Dialog, new() { Name = "本文を再生成" });
        await Expect(replace).ToContainTextAsync("記事全体の本文");
        await replace.GetByRole(AriaRole.Button, new() { Name = "キャンセル", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("空の見出しに手作業で書いた本文");
        Assert.Equal(0, await fixture.GetJobCountAsync(id, JobType.BodyGeneration));
        await page.ReloadAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("空の見出しに手作業で書いた本文");
    }

    [Fact]
    public async Task EditorUx_MobileBodyAndActionsStayReachableAndTocShowsHierarchy()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(EditorUx_MobileBodyAndActionsStayReachableAndTocShowsHierarchy));
        var page = session.Page;
        var id = await OpenEditorWithHeadingAsync(page);
        await page.Locator("#heading-title").FillAsync("親の見出し");
        await page.Locator("#heading-body").FillAsync("編集する本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.GetByRole(AriaRole.Button, new() { Name = "H3追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH3");
        await page.Locator("#heading-title").FillAsync("子の見出し");
        await page.Locator("#heading-body").FillAsync("階層を持つ本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");

        foreach (var (width, height) in new[] { (320, 740), (390, 844), (667, 375), (1280, 900) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.GotoAsync($"/articles/{id}");
            await WaitForInteractiveRenderAsync(page);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            if (width < 600)
                Assert.True(await page.Locator("#heading-body").EvaluateAsync<bool>("el => el.getBoundingClientRect().top < innerHeight - 100"));
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "本文を再生成", Exact = true })).ToBeHiddenAsync();
            var preview = page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true });
            await Expect(preview).ToBeInViewportAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, $"editor-ux-{width}.png") });
            await page.Locator(".article-editor-danger").ScrollIntoViewIfNeededAsync();
            if (width < 769)
            {
                await Expect(preview).ToBeInViewportAsync();
                Assert.True(await preview.EvaluateAsync<bool>("el => el.getBoundingClientRect().height >= 44"));
                Assert.True(await page.Locator(".article-editor-danger").EvaluateAsync<bool>("el => el.getBoundingClientRect().bottom <= document.querySelector('.article-editor-actions').getBoundingClientRect().top"));
            }
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true }).ClickAsync();
        var toc = page.GetByRole(AriaRole.Navigation, new() { Name = "記事の目次" });
        await Expect(toc.Locator("ol ol")).ToContainTextAsync("子の見出し");
        await page.SetViewportSizeAsync(320, 740);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "editor-ux-preview-mobile.png"), FullPage = true });
        await page.SetViewportSizeAsync(1280, 900);
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "editor-ux-preview-desktop.png"), FullPage = true });
        await toc.GetByRole(AriaRole.Link, new() { Name = "子の見出しを編集", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("子の見出し");
    }

    private async Task<Guid> OpenEditorWithHeadingAsync(IPage page)
    {
        var id = await fixture.SeedResearchArticleAsync("暮らしを整えるための実践ガイド");
        await LoginAsync(page);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH2");
        return id;
    }
}
