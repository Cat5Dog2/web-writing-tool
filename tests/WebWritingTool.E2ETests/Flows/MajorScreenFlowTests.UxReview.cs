using Microsoft.Playwright;
using System.Text.Json;
using WebWritingTool.Domain.Jobs;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

public sealed partial class MajorScreenFlowTests
{
    [Fact]
    public async Task UxReview_CreateSubmitsOutlineJobWithoutSecondAction()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(UxReview_CreateSubmitsOutlineJobWithoutSecondAction));
        var page = session.Page;
        await LoginAsync(page);
        await page.GotoAsync("/articles/create");
        await WaitForInteractiveRenderAsync(page);
        await FillAndChangeAsync(page.Locator("#keyword"), $"構成確認 {Guid.NewGuid():N}");
        await FillAndChangeAsync(page.Locator("#title"), "構成を一度で作成");
        await page.GetByRole(AriaRole.Button, new() { Name = "詳細設定を開く", Exact = true }).ClickAsync();
        await FillAndChangeAsync(page.Locator("#h2-count"), "2");
        await FillAndChangeAsync(page.Locator("#h3-count"), "3");
        await page.Locator("#tone").SelectOptionAsync("Friendly");
        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-create-mobile.png"), FullPage = true });
        await page.GetByRole(AriaRole.Button, new() { Name = "構成を作成", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "構成を一度で作成" })).ToBeVisibleAsync();
        var id = Guid.Parse(ArticleUrlPattern().Match(page.Url).Groups["id"].Value);
        var jobId = await fixture.GetLatestJobIdAsync(id, JobType.OutlineGeneration);
        Assert.NotNull(jobId);
        using var payload = JsonDocument.Parse(await fixture.GetJobPayloadAsync(jobId.Value));
        Assert.Equal(2, payload.RootElement.GetProperty("h2Count").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("h3Count").GetInt32());
        Assert.Equal("Friendly", payload.RootElement.GetProperty("tone").GetString());
        Assert.Equal(1, await fixture.GetJobCountAsync(id, JobType.OutlineGeneration));
    }

    [Fact]
    public async Task UxReview_UnsavedHeadingSupportsCancelSaveAndDiscard()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(UxReview_UnsavedHeadingSupportsCancelSaveAndDiscard));
        var page = session.Page;
        var id = await fixture.SeedResearchArticleAsync($"未保存確認 {Guid.NewGuid():N}");
        await LoginAsync(page);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync("新しいH2");
        await FillAndChangeAsync(page.Locator("#heading-body"), "保存する大切な本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "未保存の変更" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "キャンセル", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("保存する大切な本文");
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "保存して続ける", Exact = true }).ClickAsync();
        await Expect(page.Locator(".article-outline-item")).ToHaveCountAsync(2);
        await page.Locator(".article-outline-item").First.ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("保存する大切な本文");
        await FillAndChangeAsync(page.Locator("#heading-body"), "破棄する変更");
        await page.Locator(".article-outline-item").Last.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "変更を破棄", Exact = true }).ClickAsync();
        await page.Locator(".article-outline-item").First.ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("保存する大切な本文");
        await page.ReloadAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("保存する大切な本文");
        await WaitForInteractiveRenderAsync(page);
        await page.Locator("#heading-body").FillAsync("画面移動前にも保存する本文");
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("未保存の変更があります");
        await page.GetByRole(AriaRole.Link, new() { Name = "一覧へ戻る", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("画面移動前にも保存する本文");
        await page.GetByRole(AriaRole.Link, new() { Name = "記事一覧", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "保存して続ける", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/articles");
        await page.GotoAsync($"/articles/{id}");
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("画面移動前にも保存する本文");
    }

    [Fact]
    public async Task UxReview_PostDialogKeepsKeyboardFocusAndRestoresTrigger()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(UxReview_PostDialogKeepsKeyboardFocusAndRestoresTrigger));
        var page = session.Page;
        var scenario = await fixture.SeedWordpressPostScenarioAsync(Guid.NewGuid().ToString("N"));
        await LoginAsync(page);
        await SearchArticleAsync(page, scenario.ArticleTitle);
        var trigger = page.GetByRole(AriaRole.Button, new() { Name = "投稿", Exact = true });
        await trigger.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "WordPress投稿", Exact = true });
        await Expect(dialog).ToBeVisibleAsync();
        for (var index = 0; index < 8; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)"));
        }
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task UxReview_SettingsValidationIdentifiesFields()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(UxReview_SettingsValidationIdentifiesFields));
        var page = session.Page;
        await LoginAsync(page);
        await page.GotoAsync("/settings");
        await WaitForInteractiveRenderAsync(page);
        await FillAndChangeAsync(page.Locator("#discord-webhook-url"), "https://discord.com/api/webhooks/e2e-token/e2e-secret");
        await page.Locator("#discord-enabled").SetCheckedAsync(true);
        await page.GetByRole(AriaRole.Button, new() { Name = "保存", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Discord通知設定を保存しました。")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "登録", Exact = true }).ClickAsync();
        await Expect(page.Locator("#site-name-error")).ToContainTextAsync("サイト名");
        await Expect(page.Locator("#base-url-error")).ToContainTextAsync("HTTPS");
        await Expect(page.Locator("#site-name")).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator("html")).ToHaveJSPropertyAsync("scrollWidth", 390);
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-settings-mobile.png"), FullPage = true });
    }

    [Fact]
    public async Task UxReview_MobileEditorAndPreviewKeepContentReachable()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(UxReview_MobileEditorAndPreviewKeepContentReachable));
        var page = session.Page;
        var id = await fixture.SeedResearchArticleAsync($"画面確認 {Guid.NewGuid():N}");
        await LoginAsync(page);
        await page.GotoAsync($"/articles/{id}");
        await WaitForInteractiveRenderAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加", Exact = true }).ClickAsync();
        const string heading = "共通する長い見出しの接頭辞でも末尾の違いを確認できる見出し・その一";
        await page.Locator("#heading-title").FillAsync(heading);
        await page.Locator("#heading-body").FillAsync("編集とプレビューの往復を確認する本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-save-state")).ToHaveTextAsync("保存済み");
        await page.EvaluateAsync("window.scrollTo({ top: 0, left: 0, behavior: 'instant' })");
        await page.WaitForFunctionAsync("window.scrollY === 0");
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-editor-desktop.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        await page.ReloadAsync();
        await Expect(page.Locator("#heading-body")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#heading-body").EvaluateAsync<bool>("el => el.getBoundingClientRect().top + scrollY < 1100"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-editor-mobile.png"), FullPage = true });
        await page.GetByRole(AriaRole.Button, new() { Name = "見出し構成（1）", Exact = true }).ClickAsync();
        await Expect(page.Locator(".article-outline-title")).ToHaveCSSAsync("white-space", "normal");
        await page.Locator(".article-outline-item").ClickAsync();
        await Expect(page.Locator("#heading-editor")).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "HTML変換", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "プレビュー", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "記事の目次" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "編集に戻る", Exact = true })).ToHaveCountAsync(2);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-preview-mobile.png"), FullPage = true });
        await page.GetByRole(AriaRole.Link, new() { Name = heading + "を編集", Exact = true }).ClickAsync();
        await Expect(page.Locator("#heading-title")).ToHaveValueAsync(heading);
        await page.SetViewportSizeAsync(667, 375);
        await Expect(page.Locator(".navbar-toggler")).ToBeVisibleAsync();
        Assert.True(await page.Locator(".sidebar").EvaluateAsync<bool>("el => el.getBoundingClientRect().height < 100"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.TestResultsDirectory, "ux-editor-landscape.png"), FullPage = true });
    }
}
