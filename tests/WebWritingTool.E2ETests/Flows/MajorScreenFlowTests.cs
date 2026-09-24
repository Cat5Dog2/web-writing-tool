using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.E2ETests.Support;
using static Microsoft.Playwright.Assertions;

namespace WebWritingTool.E2ETests.Flows;

[Collection(E2ETestCollection.Name)]
[Trait("Category", "E2E")]
public sealed partial class MajorScreenFlowTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task E2E001_Login_WithAdminCredentials_NavigatesToArticleList()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E001_Login_WithAdminCredentials_NavigatesToArticleList));
        var page = session.Page;

        try
        {
            await LoginAsync(page);

            Assert.Contains("/articles", page.Url, StringComparison.Ordinal);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "記事一覧" })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成", Exact = true })).ToBeVisibleAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E002_ArticleListSearch_FiltersMatchingArticles()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E002_ArticleListSearch_FiltersMatchingArticles));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var scenario = await fixture.SeedArticleSearchScenarioAsync(suffix);

        try
        {
            await LoginAsync(page);
            await SearchArticleAsync(page, scenario.MatchingTitle);

            await Expect(page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = scenario.MatchingTitle }))
                .ToHaveCountAsync(1);
            await Expect(page.GetByText(scenario.OtherTitle)).ToHaveCountAsync(0);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E003_BulkCreate_CreatesMultipleArticles()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E003_BulkCreate_CreatesMultipleArticles));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keywordOnly = $"e2e-bulk-keyword-{suffix}";
        var titledKeyword = $"e2e-bulk-titled-{suffix}";
        var title = $"E2E一括登録タイトル {suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles");
            await WaitForInteractiveRenderAsync(page);

            await page.GetByRole(AriaRole.Button, new() { Name = "一括作成" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "一括作成" })).ToBeVisibleAsync();
            await FillAndChangeAsync(page.Locator("#bulk-lines"), $"{keywordOnly}\n{titledKeyword}|{title}");
            await page.Locator("summary").Filter(new() { HasText = "記事の構成と生成設定" }).ClickAsync();
            await page.Locator("#bulk-search").SetCheckedAsync(false);
            await page.Locator("#bulk-submit").ClickAsync();

            await Expect(page.GetByText("2件の記事を登録しました。")).ToBeVisibleAsync();

            await SearchArticleAsync(page, title);
            await Expect(page.Locator(".article-table").GetByText(title)).ToBeVisibleAsync();

            await SearchArticleAsync(page, keywordOnly);
            await Expect(page.Locator(".article-table").GetByText(keywordOnly)).ToBeVisibleAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E004_CreateArticle_ThenRegistersOutlineGenerationJob()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E004_CreateArticle_ThenRegistersOutlineGenerationJob));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-create-keyword-{suffix}";
        var title = $"E2E記事作成 {suffix}";

        try
        {
            await LoginAsync(page);
            var articleId = await CreateArticleAsync(page, keyword, title);

            Assert.Equal(1, await fixture.GetJobCountAsync(articleId, JobType.OutlineGeneration));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005_TitleCandidatesApi_RegistersGenerationJob()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005_TitleCandidatesApi_RegistersGenerationJob));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-keyword-{suffix}";
        var title = $"E2Eタイトル候補元 {suffix}";

        try
        {
            await LoginAsync(page);
            var articleId = await CreateArticleAsync(page, keyword, title);
            var response = await PostJsonAsync(
                page,
                $"/api/articles/{articleId}/generation/title-candidates",
                new
                {
                    keyword,
                    titleMethod = "Ai",
                    generationModel = "gemini-3.8-flash",
                    candidateCount = 3,
                    suggestedKeywords = (string?)null,
                    relatedKeywords = (string?)null,
                    additionalPrompt = (string?)null
                });

            Assert.Equal(202, response.Status);
            using var payload = JsonDocument.Parse(response.Body);
            Assert.Equal("TitleGeneration", payload.RootElement.GetProperty("jobType").GetString());
            Assert.Equal("Queued", payload.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, await fixture.GetJobCountAsync(articleId, JobType.TitleGeneration));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005B_TitleCandidatesButton_EnablesAfterKeywordAndRegistersJob()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005B_TitleCandidatesButton_EnablesAfterKeywordAndRegistersJob));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-button-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            var titleCandidateButton = page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" });
            await Expect(titleCandidateButton).ToBeDisabledAsync();

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await Expect(titleCandidateButton).ToBeEnabledAsync();

            await titleCandidateButton.ClickAsync();

            // Waiting for the "queued" status text (instead of an arbitrary delay) proves the
            // draft article and the generation job both exist server-side before we query the DB.
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var articleId = await fixture.FindArticleIdByKeywordAsync(keyword);
            Assert.NotNull(articleId);
            Assert.Equal(1, await fixture.GetJobCountAsync(articleId.Value, JobType.TitleGeneration));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005C_TitleCandidatesThenSubmit_ReusesSameDraftArticle()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005C_TitleCandidatesThenSubmit_ReusesSameDraftArticle));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-reuse-{suffix}";
        var title = $"E2Eタイトル候補後の送信 {suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var draftArticleId = await fixture.FindArticleIdByKeywordAsync(keyword);
            Assert.NotNull(draftArticleId);

            // "構成を作成" stays disabled while a candidate generation is in flight (the DB
            // concurrency fix), so the generation has to finish before submitting is possible.
            var pendingJobId = await fixture.GetLatestJobIdAsync(draftArticleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(pendingJobId);
            await fixture.MarkJobSucceededAsync(
                pendingJobId!.Value,
                """{"candidates":[{"title":"E2E候補A","reason":"確認用の候補"}]}""");

            var candidatesDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(candidatesDialog).ToBeVisibleAsync();
            await candidatesDialog.GetByRole(AriaRole.Button, new() { Name = "閉じる" }).ClickAsync();
            await Expect(candidatesDialog).ToBeHiddenAsync();

            await FillAndChangeAsync(page.Locator("#title"), title);
            await page.Locator("#outline-method").SelectOptionAsync("Keyword");
            await page.Locator("#search-mode").SetCheckedAsync(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "構成を作成" }).ClickAsync();

            await Expect(page.Locator(".article-editor-page h1")).ToHaveTextAsync(title);
            var match = ArticleUrlPattern().Match(page.Url);
            Assert.True(match.Success, $"Article detail URL was expected but current URL was {page.Url}.");
            Assert.Equal(draftArticleId!.Value, Guid.Parse(match.Groups["id"].Value));

            Assert.Equal(1, await fixture.CountArticlesByKeywordAsync(keyword));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005D_TitleCandidatesGenerating_DisablesSubmitButton()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005D_TitleCandidatesGenerating_DisablesSubmitButton));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-race-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();

            // The submit button must be disabled as soon as draft creation starts: both handlers
            // share the same ApplicationDbContext, so letting them run concurrently crashes the
            // circuit (see the code review that flagged this race).
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "構成を作成" })).ToBeDisabledAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005E_TitleCandidatesRegenerate_SyncsKeywordToDraft()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005E_TitleCandidatesRegenerate_SyncsKeywordToDraft));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var firstKeyword = $"e2e-title-sync-a-{suffix}";
        var secondKeyword = $"e2e-title-sync-b-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), firstKeyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var articleId = await fixture.FindArticleIdByKeywordAsync(firstKeyword);
            Assert.NotNull(articleId);
            var firstJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(firstJobId);

            // The button stays disabled for as long as the first generation is in flight (by
            // design, to avoid the DB race the code review found), so completing it and closing
            // the modal is required before a second, keyword-changed attempt can start.
            await fixture.MarkJobSucceededAsync(
                firstJobId!.Value,
                """{"candidates":[{"title":"E2E候補A","reason":"確認用の候補"}]}""");

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByText("E2E候補A")).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "閉じる" }).ClickAsync();
            await Expect(dialog).ToBeHiddenAsync();

            await FillAndChangeAsync(page.Locator("#keyword"), secondKeyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();

            // Reaching the "queued" status text again proves the draft update (which the job
            // handler's prompt context depends on for keyword, topic risk and strict mode) has
            // already committed, since a failed update would stop before this point.
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            Assert.Equal(secondKeyword, await fixture.GetArticleKeywordAsync(articleId!.Value));
            Assert.Equal(1, await fixture.CountArticlesByKeywordAsync(secondKeyword));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005F_TitleCandidatesModal_RendersSelectsAndRespectsClose()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005F_TitleCandidatesModal_RendersSelectsAndRespectsClose));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-close-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var articleId = await fixture.FindArticleIdByKeywordAsync(keyword);
            Assert.NotNull(articleId);
            var firstJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(firstJobId);

            // The background worker is disabled for E2E, so we simulate a successful job the
            // same way the worker would finish it, and let the page's own polling pick it up.
            await fixture.MarkJobSucceededAsync(
                firstJobId!.Value,
                """{"candidates":[{"title":"E2E候補タイトル","reason":"確認用の候補"}]}""");

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByText("E2E候補タイトル")).ToBeVisibleAsync();
            for (var index = 0; index < 10; index++)
            {
                await page.Keyboard.PressAsync(index < 5 ? "Tab" : "Shift+Tab");
                Assert.True(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)"));
            }

            await dialog.GetByRole(AriaRole.Button, new() { Name = "再生成" }).ClickAsync();
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();

            var secondJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(secondJobId);
            Assert.NotEqual(firstJobId!.Value, secondJobId!.Value);

            await fixture.MarkJobSucceededAsync(
                secondJobId!.Value,
                """{"candidates":[{"title":"E2E再生成候補","reason":"確認用の候補"}]}""");

            // Wait for the app to actually observe the second generation's completion (the
            // button re-enabling) instead of a blind sleep, so a slow environment can't make
            // this pass before the poll loop has picked up the result. The modal must stay
            // closed because the user already dismissed it for this generation.
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" })).ToBeEnabledAsync();
            await Expect(dialog).ToBeHiddenAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005G_TitleCandidatesRegenerate_JobPayloadMatchesKeywordAtClickTime()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005G_TitleCandidatesRegenerate_JobPayloadMatchesKeywordAtClickTime));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var firstKeyword = $"e2e-title-snap-a-{suffix}";
        var snapshotKeyword = $"e2e-title-snap-b-{suffix}";
        var editedAfterClickKeyword = $"e2e-title-snap-c-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), firstKeyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var articleId = await fixture.FindArticleIdByKeywordAsync(firstKeyword);
            Assert.NotNull(articleId);
            var firstJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(firstJobId);

            await fixture.MarkJobSucceededAsync(
                firstJobId!.Value,
                """{"candidates":[{"title":"E2E候補A","reason":"確認用の候補"}]}""");

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "閉じる" }).ClickAsync();
            await Expect(dialog).ToBeHiddenAsync();

            // Set the keyword the click below must snapshot, then edit it again while the draft
            // save is in flight: GenerateTitleCandidatesAsync captures its snapshot synchronously
            // before its first await, so this edit can never land ahead of that capture. Holding a
            // Postgres row lock on the article pins the edit inside that exact save-in-flight
            // window deterministically, instead of racing the click against however fast this
            // environment's DB round-trip happens to be (see the code review that found the
            // article/job keyword mismatch when the form kept changing under an in-flight save).
            await FillAndChangeAsync(page.Locator("#keyword"), snapshotKeyword);

            await using (await fixture.LockArticleRowForUpdateAsync(articleId!.Value))
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
                await Expect(page.GetByText("下書きを更新しています…")).ToBeVisibleAsync();

                // The server's SaveChangesAsync is blocked on the row lock above, so this edit is
                // guaranteed to land before the draft update (and the job payload built after it)
                // can be produced.
                await FillAndChangeAsync(page.Locator("#keyword"), editedAfterClickKeyword);

                // Confirm the edit above actually reached the server before releasing the lock,
                // without a blind sleep: Blazor Server processes circuit messages strictly in the
                // order they were dispatched, so once this later, unrelated toggle's own effect is
                // observed, the keyword change sent earlier over the same connection is guaranteed
                // to have already been applied.
                await page.GetByRole(AriaRole.Button, new() { Name = "詳細設定を開く" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Button, new() { Name = "詳細設定を閉じる" })).ToBeVisibleAsync();
            }

            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var secondJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(secondJobId);
            Assert.NotEqual(firstJobId!.Value, secondJobId!.Value);

            Assert.Equal(snapshotKeyword, await fixture.GetArticleKeywordAsync(articleId!.Value));
            Assert.Equal(snapshotKeyword, await fixture.GetJobPayloadKeywordAsync(secondJobId!.Value));

            // Clean up so the circuit isn't left polling: let the second generation finish.
            await fixture.MarkJobSucceededAsync(
                secondJobId!.Value,
                """{"candidates":[{"title":"E2E候補B","reason":"確認用の候補"}]}""");
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "閉じる" }).ClickAsync();
            await Expect(dialog).ToBeHiddenAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005H_TitleCandidatesApplySelected_SuppressesReopenAfterRegenerate()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005H_TitleCandidatesApplySelected_SuppressesReopenAfterRegenerate));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-apply-{suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" }).ClickAsync();
            await Expect(page.GetByText("タイトル候補を生成しています…（生成待ち）")).ToBeVisibleAsync();

            var articleId = await fixture.FindArticleIdByKeywordAsync(keyword);
            Assert.NotNull(articleId);
            var firstJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(firstJobId);

            await fixture.MarkJobSucceededAsync(
                firstJobId!.Value,
                """{"candidates":[{"title":"E2E候補適用前","reason":"確認用の候補"}]}""");

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByText("E2E候補適用前")).ToBeVisibleAsync();

            // Mirrors the review's repro: start a second generation without closing the modal,
            // then apply a candidate from the still-open modal while that generation is running.
            await dialog.GetByRole(AriaRole.Button, new() { Name = "再生成" }).ClickAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "選択して反映" }).ClickAsync();

            await Expect(page.Locator("#title")).ToHaveValueAsync("E2E候補適用前");
            await Expect(dialog).ToBeHiddenAsync();

            var secondJobId = await fixture.GetLatestJobIdAsync(articleId!.Value, JobType.TitleGeneration);
            Assert.NotNull(secondJobId);
            Assert.NotEqual(firstJobId!.Value, secondJobId!.Value);

            await fixture.MarkJobSucceededAsync(
                secondJobId!.Value,
                """{"candidates":[{"title":"E2E候補適用後","reason":"確認用の候補"}]}""");

            // Wait for the app to observe the second generation's completion (button re-enabling)
            // before asserting the modal is still hidden, instead of racing a blind sleep.
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "タイトル候補を生成" })).ToBeEnabledAsync();
            await Expect(dialog).ToBeHiddenAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E006And007_EditGeneratedContentAndConvertHtml_ShowsPreview()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E006And007_EditGeneratedContentAndConvertHtml_ShowsPreview));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-edit-keyword-{suffix}";
        var title = $"E2E編集記事 {suffix}";

        try
        {
            await LoginAsync(page);
            var articleId = await CreateArticleAsync(page, keyword, title);
            await CancelInitialOutlineAsync(page, articleId);
            await EditGeneratedContentAsync(page);

            await page.GotoAsync($"/articles/{articleId}/preview");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "E2E見出し" })).ToBeVisibleAsync();
            await Expect(page.GetByText("E2E本文です。ブラウザ経由で保存される本文です。")).ToBeVisibleAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E008_WordpressPostDialog_RegistersPostJob()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E008_WordpressPostDialog_RegistersPostJob));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var scenario = await fixture.SeedWordpressPostScenarioAsync(suffix);

        try
        {
            await LoginAsync(page);
            await CreateWordpressSiteViaSettingsAsync(page, suffix);
            await SearchArticleAsync(page, scenario.ArticleTitle);

            var articleRow = page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = scenario.ArticleTitle });
            await articleRow.GetByRole(AriaRole.Button, new() { Name = "投稿" }).ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByRole(AriaRole.Heading, new() { Name = "WordPress投稿" })).ToBeVisibleAsync();
            await Expect(dialog.Locator("#post-title")).ToHaveValueAsync(scenario.ArticleTitle);
            await Expect(dialog.Locator("#post-status")).ToHaveValueAsync("Draft");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "投稿ジョブ登録" }).ClickAsync();

            await Expect(page.GetByText("WordPress投稿ジョブを登録しました。")).ToBeVisibleAsync();
            Assert.Equal(1, await fixture.GetJobCountAsync(scenario.ArticleId, JobType.WordpressPost));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E009_NotificationSettings_SavesAndSendsTestNotification()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E009_NotificationSettings_SavesAndSendsTestNotification));
        var page = session.Page;

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/settings");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "設定" })).ToBeVisibleAsync();
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(
                page.Locator("#discord-webhook-url"),
                "https://discord.com/api/webhooks/e2e-token/e2e-secret");
            await page.Locator("#discord-enabled").SetCheckedAsync(true);
            await page.GetByRole(AriaRole.Button, new() { Name = "保存" }).ClickAsync();
            await Expect(page.GetByText("Discord通知設定を保存しました。")).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "送信テスト" }).ClickAsync();
            await Expect(page.GetByText("通知を送信しました。")).ToBeVisibleAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

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
            await CancelInitialOutlineAsync(page, articleId);
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

    [Fact]
    public async Task E2E010_Authorization_NonAdminUserOpensAnotherUsersArticle_ShowsNotFound()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E010_Authorization_NonAdminUserOpensAnotherUsersArticle_ShowsNotFound));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var scenario = await fixture.SeedArticleAccessScenarioAsync(suffix);

        try
        {
            await LoginAsync(page, scenario.ViewerEmail, E2ETestFixture.StandardUserPassword);

            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "ユーザー管理" })).ToHaveCountAsync(0);

            await page.GotoAsync("/articles");
            await WaitForInteractiveRenderAsync(page);
            await FillAndChangeAsync(page.Locator("#search-q"), scenario.ArticleTitle);
            await page.GetByRole(AriaRole.Button, new() { Name = "検索", Exact = true }).ClickAsync();

            await Expect(page.GetByText("検索条件に一致する記事がありません")).ToBeVisibleAsync();
            await Expect(page.GetByText(scenario.ArticleTitle)).ToHaveCountAsync(0);

            await page.GotoAsync($"/articles/{scenario.ArticleId}");
            await Expect(page.Locator(".article-editor-page h1")).ToHaveTextAsync("生成結果編集");
            await Expect(page.GetByText("記事が見つかりません。")).ToBeVisibleAsync();
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E011_WritingProfileSettings_CanBeSelectedWhenCreatingArticle()
    {
        await using var session = await fixture.CreateSessionAsync(nameof(E2E011_WritingProfileSettings_CanBeSelectedWhenCreatingArticle));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var siteName = $"E2E Writing Profile {suffix}";
        var keyword = $"e2e-profile-keyword-{suffix}";
        var title = $"E2Eプロフィール記事 {suffix}";

        try
        {
            await LoginAsync(page);
            await CreateWordpressSiteViaSettingsAsync(
                page,
                suffix,
                siteName,
                siteAdminProfile: $"管理人プロフィール {suffix}",
                writingCharacter: $"語り手キャラ {suffix}",
                readerPersona: $"読者ペルソナ {suffix}");

            await page.GotoAsync("/articles/create");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "記事作成" })).ToBeVisibleAsync();
            await WaitForInteractiveRenderAsync(page);

            await FillAndChangeAsync(page.Locator("#keyword"), keyword);
            await FillAndChangeAsync(page.Locator("#title"), title);
            await page.GetByRole(AriaRole.Button, new() { Name = "詳細設定を開く" }).ClickAsync();
            var profileValue = await page.Locator("#writing-profile option")
                .Filter(new LocatorFilterOptions { HasText = siteName })
                .GetAttributeAsync("value");
            Assert.False(string.IsNullOrWhiteSpace(profileValue));
            await page.Locator("#writing-profile").SelectOptionAsync(profileValue);
            await page.Locator("#outline-method").SelectOptionAsync("Keyword");
            await page.Locator("#search-mode").SetCheckedAsync(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "構成を作成" }).ClickAsync();

            await Expect(page.Locator(".article-editor-page h1")).ToHaveTextAsync(title);
            var match = ArticleUrlPattern().Match(page.Url);
            Assert.True(match.Success, $"Article detail URL was expected but current URL was {page.Url}.");

            var snapshotJson = await fixture.GetArticleWritingProfileSnapshotJsonAsync(Guid.Parse(match.Groups["id"].Value));
            Assert.NotNull(snapshotJson);
            Assert.Contains($"管理人プロフィール {suffix}", snapshotJson, StringComparison.Ordinal);
            Assert.Contains($"語り手キャラ {suffix}", snapshotJson, StringComparison.Ordinal);
            Assert.Contains($"読者ペルソナ {suffix}", snapshotJson, StringComparison.Ordinal);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E012_AccountPasswordChange_UpdatesCredentialsAndKeepsSession()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E012_AccountPasswordChange_UpdatesCredentialsAndKeepsSession));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = await fixture.SeedPasswordChangeUserAsync(suffix);
        const string newPassword = "Changed-e2e-password-456!";

        try
        {
            await LoginAsync(page, email, E2ETestFixture.StandardUserPassword);
            await page.GotoAsync("/account");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "アカウント" })).ToBeVisibleAsync();

            await page.Locator("#password-current-password").FillAsync(E2ETestFixture.StandardUserPassword);
            await page.Locator("#password-new-password").FillAsync(newPassword);
            await page.Locator("#password-confirm-new-password").FillAsync(newPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "パスワードを変更する" }).ClickAsync();

            await Expect(page.GetByText("パスワードを変更しました。")).ToBeVisibleAsync();
            await page.GotoAsync("/articles");
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成", Exact = true })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "ログアウト" }).ClickAsync();
            await page.Locator("#email").FillAsync(email);
            await page.Locator("#password").FillAsync(E2ETestFixture.StandardUserPassword);
            await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();
            await Expect(page.GetByText("メールアドレスまたはパスワードが正しくありません。"))
                .ToBeVisibleAsync();

            await LoginAsync(page, email, newPassword);
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    private static async Task LoginAsync(IPage page)
    {
        await LoginAsync(page, E2ETestFixture.AdminEmail, E2ETestFixture.AdminPassword);
    }

    private static async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/login");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "ログイン" })).ToBeVisibleAsync();

        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成", Exact = true })).ToBeVisibleAsync();
    }

    private static async Task SearchArticleAsync(IPage page, string query)
    {
        await page.GotoAsync("/articles");
        await WaitForInteractiveRenderAsync(page);
        await FillAndChangeAsync(page.Locator("#search-q"), query);
        await page.GetByRole(AriaRole.Button, new() { Name = "検索", Exact = true }).ClickAsync();
        await WaitForInteractiveRenderAsync(page);
    }

    private static async Task<string> CreateWordpressSiteViaSettingsAsync(
        IPage page,
        string suffix,
        string? siteName = null,
        string? siteAdminProfile = null,
        string? writingCharacter = null,
        string? readerPersona = null)
    {
        var resolvedSiteName = siteName ?? $"E2E WordPress {suffix}";
        await page.GotoAsync("/settings");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "設定" })).ToBeVisibleAsync();
        await WaitForInteractiveRenderAsync(page);

        await FillAndChangeAsync(page.Locator("#site-name"), resolvedSiteName);
        await FillAndChangeAsync(page.Locator("#base-url"), "https://example.com");
        await FillAndChangeAsync(page.Locator("#login-id"), $"wp-e2e-{suffix}");
        await FillAndChangeAsync(page.Locator("#app-pass"), $"app-pass-{suffix}");
        await FillAndChangeAsync(page.Locator("#default-category-id"), "7");
        await FillAndChangeAsync(page.Locator("#default-category-name"), "E2E");

        if (siteAdminProfile is not null || writingCharacter is not null || readerPersona is not null)
            await page.Locator("summary").Filter(new() { HasText = "ライティング設定（任意）" }).ClickAsync();

        if (siteAdminProfile is not null)
        {
            await FillAndChangeAsync(page.Locator("#site-admin-profile"), siteAdminProfile);
        }

        if (writingCharacter is not null)
        {
            await FillAndChangeAsync(page.Locator("#writing-character"), writingCharacter);
        }

        if (readerPersona is not null)
        {
            await FillAndChangeAsync(page.Locator("#reader-persona"), readerPersona);
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "登録" }).ClickAsync();
        await Expect(page.GetByText("WordPressサイトを登録しました。")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = resolvedSiteName }))
            .ToHaveCountAsync(1);
        return resolvedSiteName;
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

        await Expect(page.Locator(".article-editor-page h1")).ToBeVisibleAsync();
        await Expect(page.Locator(".article-editor-page h1")).ToHaveTextAsync(title);
        await WaitForInteractiveRenderAsync(page);

        var match = ArticleUrlPattern().Match(page.Url);
        Assert.True(match.Success, $"Article detail URL was expected but current URL was {page.Url}.");
        return Guid.Parse(match.Groups["id"].Value);
    }

    private static async Task CancelInitialOutlineAsync(IPage page, Guid articleId)
    {
        // This fixture leaves workers stopped to test job registration. Cancel its queued
        // outline before exercising manual editing, which must not race generation.
        var jobId = new Uri(page.Url).Query.Split("job=")[1].Split('&')[0];
        var canceled = await PostJsonAsync(page, $"/api/jobs/{jobId}/cancel", new { });
        Assert.Equal(200, canceled.Status);
        await page.GotoAsync($"/articles/{articleId}");
        await WaitForInteractiveRenderAsync(page);
    }

    private static async Task EditGeneratedContentAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "見出しを追加しました。" })).ToBeVisibleAsync();

        await FillAndChangeAsync(page.Locator("#heading-title"), "E2E見出し");
        await FillAndChangeAsync(page.Locator("#heading-body"), "E2E本文です。ブラウザ経由で保存される本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "本文を保存しました。" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "保存してプレビュー", Exact = true }).ClickAsync();
        await Expect(page.Locator(".article-preview-body")).ToContainTextAsync("E2E本文です。");
        await page.GetByRole(AriaRole.Link, new() { Name = "編集に戻る", Exact = true }).First.ClickAsync();
        await Expect(page.Locator("#heading-body")).ToHaveValueAsync("E2E本文です。ブラウザ経由で保存される本文です。");
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
                generationModel = "gemini-3.8-flash",
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
                generationModel = "gemini-3.8-flash",
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
        await page.GetByRole(AriaRole.Button, new() { Name = "検索", Exact = true }).ClickAsync();

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
