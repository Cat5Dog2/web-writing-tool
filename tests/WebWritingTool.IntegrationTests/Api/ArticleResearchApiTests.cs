using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class ArticleResearchApiTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task DummySearch_ReturnsSamplesAndPreservesRealCache()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        var realPostId = Guid.NewGuid().ToString("N");
        using (var seedScope = fixture.Factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.SearchResults.Add(new SearchResult
            {
                UserId = userId,
                ArticleId = articleId,
                Query = "家庭菜園",
                Url = "https://example.org/real",
                Title = "通常モードの資料",
                Snippet = "実キャッシュの本文",
                CacheExpiresAt = now.AddHours(1),
                ContentExpiresAt = now.AddHours(1),
                FetchedAt = now
            });
            db.XSearchPosts.Add(new XSearchPost
            {
                UserId = userId,
                ArticleId = articleId,
                Query = "家庭菜園",
                QueryHash = "real",
                PostId = realPostId,
                Text = "実投稿の本文",
                CacheExpiresAt = now.AddHours(1),
                ContentExpiresAt = now.AddHours(1),
                FetchedAt = now
            });
            await db.SaveChangesAsync();
        }
        using var factory = CreateFactory(true);
        using var client = await CreateClientAsync(factory, userId);

        foreach (var source in new[] { "web", "x" })
        {
            var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/research/{source}",
                new { query = "家庭菜園", maxResults = 3 });
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
            await ExecuteJobAsync(factory, accepted.GetProperty("jobId").GetGuid());
        }

        var response = await client.GetAsync($"/api/articles/{articleId}/research");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(results.GetProperty("isDummy").GetBoolean());
        Assert.Equal(3, results.GetProperty("webResults").GetArrayLength());
        Assert.Equal(3, results.GetProperty("xPosts").GetArrayLength());
        Assert.Contains("サンプル", results.GetProperty("xPosts")[0].GetProperty("text").GetString());

        using var realFactory = CreateFactory(false);
        using var realClient = await CreateClientAsync(realFactory, userId);
        var realResults = await realClient.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}/research");
        Assert.Single(realResults.GetProperty("webResults").EnumerateArray());
        Assert.Empty(realResults.GetProperty("xPosts").EnumerateArray());
        Assert.Contains("再取得できなかった", realResults.GetProperty("warning").GetString());
        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("実投稿の本文", (await verifyDb.XSearchPosts.SingleAsync(post => post.PostId == realPostId)).Text);
    }

    [Fact]
    public async Task Generation_UsesFetchedSourcesAndRecordsTheActualPromptHash()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        var capture = new CapturingAiClient();
        using var baseFactory = CreateFactory(true);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerationClient>();
            services.AddSingleton<IAiTextGenerationClient>(capture);
        }));
        using var client = await CreateClientAsync(factory, userId);
        var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/generation/outline",
            new { articleId, h2Count = 1, h3Count = 0, searchMode = true });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
        await ExecuteJobAsync(factory, accepted.GetProperty("jobId").GetGuid());
        var request = Assert.Single(capture.Requests);
        Assert.NotEmpty(request.References);
        Assert.Contains(request.References, source => source.Title!.Contains("家庭菜園"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var log = await db.AiGenerationLogs.SingleAsync(log => log.ArticleId == articleId);
        Assert.Equal(PromptHashCalculator.Compute(
            ReferencePromptFormatter.SystemInstruction(request.SystemInstruction, request.References),
            ReferencePromptFormatter.UserPrompt(request.UserPrompt, request.References)), log.PromptHash);
        Assert.Equal(request.PromptChars, log.PromptChars);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generation_PrioritizesManualSourcesOverAutomaticSearchOnEveryRun(bool headingScoped)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        var heading = new ArticleHeading { ArticleId = articleId, Level = 2, Title = "育て方", DisplayOrder = 1 };
        var capture = new CapturingAiClient();
        using var baseFactory = CreateFactory(true);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerationClient>();
            services.AddSingleton<IAiTextGenerationClient>(capture);
        }));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ArticleHeadings.Add(heading);
            await db.SaveChangesAsync();
        }
        using var client = await CreateClientAsync(factory, userId);
        var manual = await client.PostAsJsonAsync($"/api/articles/{articleId}/research/web",
            new { query = "手動で選んだ水やり資料", headingId = headingScoped ? heading.Id : (Guid?)null, maxResults = 2 });
        Assert.Equal(HttpStatusCode.Accepted, manual.StatusCode);
        var manualJob = await manual.Content.ReadFromJsonAsync<JsonElement>();
        await ExecuteJobAsync(factory, manualJob.GetProperty("jobId").GetGuid());

        for (var run = 0; run < 2; run++)
        {
            var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/generation/body",
                new { articleId, scope = "All", useWebSearch = true });
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
            await ExecuteJobAsync(factory, accepted.GetProperty("jobId").GetGuid());
        }

        Assert.Equal(2, capture.Requests.Count);
        Assert.All(capture.Requests, request =>
        {
            Assert.Equal(10, request.References.Count);
            Assert.All(request.References.Take(2), source => Assert.Contains("手動で選んだ水やり資料", source.Title));
            Assert.All(request.References.Skip(2), source => Assert.Contains("家庭菜園", source.Title));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(12)]
    public async Task References_ApplyManualPriorityBeforeLimitsAndExcludeInvalidSources(int manualCount)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        using var factory = CreateFactory(true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        SearchResult Source(string title, bool manual, DateTimeOffset fetchedAt) => new()
        {
            UserId = userId,
            ArticleId = articleId,
            Query = "家庭菜園",
            Title = title,
            Url = $"https://example.test/{title}",
            Snippet = $"{title}の内容",
            IsManual = manual,
            IsDummy = true,
            FetchedAt = fetchedAt,
            CacheExpiresAt = now.AddHours(1),
            ContentExpiresAt = now.AddHours(1)
        };
        db.SearchResults.AddRange(Enumerable.Range(1, manualCount)
            .Select(index => Source($"manual-{index}", true, now.AddMinutes(-120 + index))));
        db.SearchResults.AddRange(Enumerable.Range(1, 105)
            .Select(index => Source($"automatic-{index}", false, now.AddMinutes(-1))));
        if (manualCount > 0)
        {
            var duplicate = Source("duplicate", false, now);
            duplicate.Url = $"https://example.test/manual-{manualCount}";
            db.SearchResults.Add(duplicate);
        }
        var expiredCache = Source("expired-cache", true, now);
        expiredCache.CacheExpiresAt = now.AddSeconds(-1);
        var expiredContent = Source("expired-content", true, now);
        expiredContent.ContentExpiresAt = now.AddSeconds(-1);
        var real = Source("different-mode", true, now);
        real.IsDummy = false;
        var unsafeUrl = Source("unsafe-url", true, now);
        unsafeUrl.Url = "javascript:alert(1)";
        var emptySnippet = Source("empty-snippet", true, now);
        emptySnippet.Snippet = " ";
        var otherHeading = new ArticleHeading { ArticleId = articleId, Level = 2, Title = "別の見出し", DisplayOrder = 1 };
        db.ArticleHeadings.Add(otherHeading);
        var headingOnly = Source("other-heading", true, now);
        headingOnly.HeadingId = otherHeading.Id;
        db.SearchResults.AddRange(expiredCache, expiredContent, real, unsafeUrl, emptySnippet, headingOnly);
        await db.SaveChangesAsync();

        var references = await scope.ServiceProvider.GetRequiredService<IArticleResearchService>()
            .GetReferencesAsync(userId, articleId, null, searchWeb: false);

        Assert.Equal(10, references.Count);
        var expectedManual = Enumerable.Range(1, manualCount).Reverse().Take(10).Select(index => $"manual-{index}");
        Assert.Equal(expectedManual, references.Take(Math.Min(10, manualCount)).Select(source => source.Title));
        Assert.All(references.Skip(manualCount), source => Assert.StartsWith("automatic-", source.Title));
        Assert.Equal(10, references.Select(source => source.Url).Distinct().Count());
    }

    [Fact]
    public async Task ManualSearch_PromotesMatchingCacheWithoutRefetchAndAutomaticSearchPreservesPriority()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        using var factory = CreateFactory(true);
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IArticleResearchService>();
            await service.GetReferencesAsync(userId, articleId, null, searchWeb: true);
            var initial = await service.GetAsync(new ArticleActor(userId, false), articleId);
            Assert.Equal(10, initial!.WebResults.Count);
            Assert.All(initial.WebResults, result => Assert.False(result.IsManual));
        }
        using var client = await CreateClientAsync(factory, userId);
        var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/research/web",
            new { query = "家庭菜園", maxResults = 10 });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();
        await ExecuteJobAsync(factory, jobId);
        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await db.ArticleGenerationJobs.SingleAsync(job => job.Id == jobId);
        using var resultJson = JsonDocument.Parse(job.ResultJson!);
        Assert.True(resultJson.RootElement.GetProperty("cached").GetBoolean());
        var before = await db.SearchResults.AsNoTracking().Where(result => result.ArticleId == articleId).ToListAsync();
        Assert.Equal(10, before.Count);
        Assert.All(before, result => Assert.True(result.IsManual));

        await verifyScope.ServiceProvider.GetRequiredService<IArticleResearchService>()
            .GetReferencesAsync(userId, articleId, null, searchWeb: true);
        var after = await db.SearchResults.AsNoTracking().Where(result => result.ArticleId == articleId).ToListAsync();
        Assert.Equal(before.OrderBy(result => result.Id).Select(result => (result.Id, result.FetchedAt)),
            after.OrderBy(result => result.Id).Select(result => (result.Id, result.FetchedAt)));
        var displayed = await client.GetFromJsonAsync<ArticleResearchResponse>($"/api/articles/{articleId}/research");
        Assert.Equal(10, displayed!.WebResults.Count);
        Assert.All(displayed.WebResults, result => Assert.True(result.IsManual));
    }

    [Fact]
    public async Task Generation_SearchFailurePreservesExistingArticleAndHeadingBody()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        var heading = new ArticleHeading
        {
            ArticleId = articleId,
            Level = 2,
            Title = "育て方",
            Body = "保存済みの本文",
            Status = HeadingStatus.Generated,
            DisplayOrder = 1
        };
        using var factory = CreateFactory(false);
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var article = await db.Articles.SingleAsync(item => item.Id == articleId);
            article.Body = "保存済みの記事全体";
            article.SearchMode = true;
            db.ArticleHeadings.Add(heading);
            await db.SaveChangesAsync();
        }
        using var client = await CreateClientAsync(factory, userId);
        var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/generation/body",
            new { articleId, scope = "All", useWebSearch = true });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
        var exception = await Assert.ThrowsAsync<JobExecutionException>(() =>
            ExecuteJobAsync(factory, accepted.GetProperty("jobId").GetGuid()));
        Assert.Contains("Tavily", exception.UserMessage);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedArticle = await verifyDb.Articles.SingleAsync(item => item.Id == articleId);
        var savedHeading = await verifyDb.ArticleHeadings.SingleAsync(item => item.Id == heading.Id);
        Assert.Equal("保存済みの記事全体", savedArticle.Body);
        Assert.Equal("保存済みの本文", savedHeading.Body);
        Assert.Equal(ArticleStatus.Failed, savedArticle.Status);
        Assert.Equal(HeadingStatus.Failed, savedHeading.Status);
        Assert.False((await verifyDb.AiGenerationLogs.SingleAsync(log => log.ArticleId == articleId)).Succeeded);
    }

    [Fact]
    public async Task SearchJob_DoesNotRunAfterModeChanges()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        using var dummy = CreateFactory(true);
        using var client = await CreateClientAsync(dummy, userId);
        var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/research/web", new { query = "家庭菜園" });
        var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
        using var real = CreateFactory(false);
        var exception = await Assert.ThrowsAsync<JobExecutionException>(() => ExecuteJobAsync(real, accepted.GetProperty("jobId").GetGuid()));
        Assert.Equal(JobErrorCodes.Conflict, exception.ErrorCode);
    }

    [Fact]
    public async Task XSearch_SamePostCanBelongToDifferentArticlesAndRefreshAfterExpiry()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = CreateFactory(true);
        using var client = await CreateClientAsync(factory, userId);
        foreach (var index in Enumerable.Range(0, 2))
        {
            var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
            var queued = await client.PostAsJsonAsync($"/api/articles/{articleId}/research/x", new { query = "同じ検索", maxResults = 1 });
            var accepted = await queued.Content.ReadFromJsonAsync<JsonElement>();
            await ExecuteJobAsync(factory, accepted.GetProperty("jobId").GetGuid());
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var posts = await db.XSearchPosts.Where(post => post.UserId == userId).ToListAsync();
        Assert.Equal(2, posts.Count);
        Assert.Equal(posts[0].PostId, posts[1].PostId);
        posts[0].CacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        posts[0].ContentExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        posts[0].Text = "期限切れ";
        await db.SaveChangesAsync();
        var response = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{posts[0].ArticleId}/research");
        Assert.Empty(response.GetProperty("xPosts").EnumerateArray());
        var next = await client.PostAsJsonAsync($"/api/articles/{posts[0].ArticleId}/research/x", new { query = "同じ検索", maxResults = 1 });
        var nextJob = await next.Content.ReadFromJsonAsync<JsonElement>();
        await ExecuteJobAsync(factory, nextJob.GetProperty("jobId").GetGuid());
        await db.Entry(posts[0]).ReloadAsync();
        Assert.Contains("サンプル", posts[0].Text);
        Assert.Equal(2, await db.XSearchPosts.CountAsync(post => post.UserId == userId));
    }

    [Fact]
    public async Task Research_RejectsOtherOwnersAndInvalidRequests()
    {
        var owner = Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(owner, $"{owner}@example.test");
        await fixture.SeedUserAsync(other, $"{other}@example.test");
        var articleId = await fixture.SeedArticleAsync(owner, "家庭菜園");
        using var factory = CreateFactory(true);
        using var client = await CreateClientAsync(factory, other);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/articles/{articleId}/research")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/articles/{articleId}/research/web",
            new { query = "家庭菜園" })).StatusCode);
        using var ownerClient = await CreateClientAsync(factory, owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync($"/api/articles/{articleId}/research/web",
            new { query = new string('a', 301) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync($"/api/articles/{articleId}/research/x",
            new { query = "家庭菜園", maxResults = 101 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync(
            $"/api/articles/{articleId}/research?headingId={Guid.NewGuid()}")).StatusCode);
    }

    private TestApplicationFactory CreateFactory(bool dummy) => new(fixture.ConnectionString,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["ExternalApis:UseMocks"] = dummy.ToString(),
            ["AiProviders:Gemini:ApiKey"] = "",
            ["SearchProviders:Tavily:ApiKey"] = "",
            ["SearchProviders:X:BearerToken"] = "",
            ["Logging:LogLevel:Default"] = "Warning"
        });

    private static async Task<HttpClient> CreateClientAsync(WebApplicationFactory<Program> factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        var token = await client.GetFromJsonAsync<JsonElement>("/api/security/antiforgery-token");
        client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("requestToken").GetString());
        return client;
    }

    private static async Task ExecuteJobAsync(WebApplicationFactory<Program> factory, Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ArticleGenerationJobs.SingleAsync(job => job.Id == jobId);
        var job = new LeasedJob(row.Id, row.UserId, row.ArticleId, row.HeadingId, row.JobType, row.PayloadJson, 1, row.MaxAttempts);
        var result = await scope.ServiceProvider.GetRequiredService<JobDispatcher>().DispatchAsync(job);
        await scope.ServiceProvider.GetRequiredService<JobLeaseService>().MarkSucceededAsync(job.Id, result.ResultJson);
    }

    private sealed class CapturingAiClient : IAiTextGenerationClient
    {
        public List<AiTextGenerationRequest> Requests { get; } = [];
        public Task<AiTextGenerationResult> GenerateAsync(AiTextGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new DummyTextGenerationClient().GenerateAsync(request, cancellationToken);
        }
    }
}
