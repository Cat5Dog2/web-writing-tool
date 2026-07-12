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
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "記事作成" })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成" })).ToBeVisibleAsync();
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
            await page.Locator("#bulk-outline-method").SelectOptionAsync("Keyword");
            await page.Locator("#bulk-search").SetCheckedAsync(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "登録" }).ClickAsync();

            await Expect(page.GetByText("2件の記事を登録しました。")).ToBeVisibleAsync();

            await SearchArticleAsync(page, title);
            await Expect(page.GetByText(title)).ToBeVisibleAsync();

            await SearchArticleAsync(page, keywordOnly);
            await Expect(page.GetByText(keywordOnly)).ToBeVisibleAsync();
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
            await EnqueueOutlineGenerationAsync(page, articleId, keyword, title);

            Assert.Equal(1, await fixture.GetJobCountAsync(articleId, JobType.OutlineGeneration));
        }
        catch
        {
            await session.CaptureFailureScreenshotAsync();
            throw;
        }
    }

    [Fact]
    public async Task E2E005_TitleCandidates_RegistersGenerationJobAndPinsCurrentDisabledUi()
    {
        await using var session = await fixture.CreateSessionAsync(
            nameof(E2E005_TitleCandidates_RegistersGenerationJobAndPinsCurrentDisabledUi));
        var page = session.Page;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var keyword = $"e2e-title-keyword-{suffix}";
        var title = $"E2Eタイトル候補元 {suffix}";

        try
        {
            await LoginAsync(page);
            await page.GotoAsync("/articles/create");
            await WaitForInteractiveRenderAsync(page);
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "記事タイトル候補を出す" })).ToBeDisabledAsync();

            var articleId = await CreateArticleAsync(page, keyword, title);
            var response = await PostJsonAsync(
                page,
                $"/api/articles/{articleId}/generation/title-candidates",
                new
                {
                    keyword,
                    titleMethod = "Ai",
                    generationModel = "gemini-3.5-flash",
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
            await page.GetByRole(AriaRole.Button, new() { Name = "検索" }).ClickAsync();

            await Expect(page.GetByText("該当する記事はありません")).ToBeVisibleAsync();
            await Expect(page.GetByText(scenario.ArticleTitle)).ToHaveCountAsync(0);

            await page.GotoAsync($"/articles/{scenario.ArticleId}");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" })).ToBeVisibleAsync();
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
            var profileValue = await page.Locator("#writing-profile option")
                .Filter(new LocatorFilterOptions { HasText = siteName })
                .GetAttributeAsync("value");
            Assert.False(string.IsNullOrWhiteSpace(profileValue));
            await page.Locator("#writing-profile").SelectOptionAsync(profileValue);
            await page.Locator("#outline-method").SelectOptionAsync("Keyword");
            await page.Locator("#search-mode").SetCheckedAsync(false);
            await page.GetByRole(AriaRole.Button, new() { Name = "構成を作成" }).ClickAsync();

            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" })).ToBeVisibleAsync();
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
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成" })).ToBeVisibleAsync();

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

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "記事を作成" })).ToBeVisibleAsync();
    }

    private static async Task SearchArticleAsync(IPage page, string query)
    {
        await page.GotoAsync("/articles");
        await WaitForInteractiveRenderAsync(page);
        await FillAndChangeAsync(page.Locator("#search-q"), query);
        await page.GetByRole(AriaRole.Button, new() { Name = "検索" }).ClickAsync();
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

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "生成結果編集" })).ToBeVisibleAsync();
        await WaitForInteractiveRenderAsync(page);

        var match = ArticleUrlPattern().Match(page.Url);
        Assert.True(match.Success, $"Article detail URL was expected but current URL was {page.Url}.");
        return Guid.Parse(match.Groups["id"].Value);
    }

    private static async Task EditGeneratedContentAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "H2追加" }).ClickAsync();
        await Expect(page.GetByText("見出しを追加しました。")).ToBeVisibleAsync();

        await FillAndChangeAsync(page.Locator("#heading-title"), "E2E見出し");
        await FillAndChangeAsync(page.Locator("#heading-body"), "E2E本文です。ブラウザ経由で保存される本文です。");
        await page.GetByRole(AriaRole.Button, new() { Name = "本文を保存" }).ClickAsync();
        await Expect(page.GetByText("本文を保存しました。")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "HTML変換" }).ClickAsync();
        await Expect(page.GetByText("HTMLへ変換しました。")).ToBeVisibleAsync();
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
                generationModel = "gemini-3.5-flash",
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
                generationModel = "gemini-3.5-flash",
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
        await page.GetByRole(AriaRole.Button, new() { Name = "検索" }).ClickAsync();

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
