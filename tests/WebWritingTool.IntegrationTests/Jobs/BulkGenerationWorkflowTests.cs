using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Search;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public sealed class BulkGenerationWorkflowTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Overview_KeepsOlderStoppedRun_AndFiltersHistoryByOwner()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IArticleCommandService>();
        var service = scope.ServiceProvider.GetRequiredService<IBulkGenerationService>();
        var actor = new ArticleActor(userId, false);
        var first = await command.BulkCreateAsync(new(userId, ["古い登録"], 1, 0, true, "Ai", "Ai",
            "gemini-3.8-flash", false, null, false, null, null, new("FullArticle", false, false)));
        var oldArticle = Assert.Single(first.Value!.Jobs).ArticleId;
        Assert.True((await service.StopAsync(actor, oldArticle)).Succeeded);
        var second = await command.BulkCreateAsync(new(userId, ["新しい登録"], 1, 0, true, "Ai", "Ai",
            "gemini-3.8-flash", false, null, false, null, null, new("OutlineOnly", false, false)));
        using var client = await fixture.CreateAuthenticatedClientAsync(userId);
        Assert.Equal(2, (await service.GetBatchesAsync(actor)).Count);
        var batches = await client.GetFromJsonAsync<BulkGenerationBatch[]>("/api/bulk-generations/batches");
        Assert.Equal(new[] { second.Value!.BatchId, first.Value.BatchId }, batches!.Select(b => (Guid?)b.BatchId));
        var overview = await client.GetFromJsonAsync<BulkGenerationProgress[]>($"/api/bulk-generations/overview?batchId={second.Value.BatchId}");
        Assert.Equal(2, overview!.Length);
        Assert.Equal("Canceled", overview.Single(i => i.ArticleId == oldArticle).Status);
        Assert.Single(await service.GetProgressAsync(actor, batchId: second.Value.BatchId));
        Assert.Empty(await service.GetBatchesAsync(new("other-owner", false)));
        Assert.Empty(await service.GetOverviewAsync(new("other-owner", false), first.Value.BatchId));
        Assert.True((await command.DeleteAsync(actor, oldArticle)).Succeeded);
        Assert.Single(await service.GetOverviewAsync(actor, second.Value.BatchId));
        Assert.Single(await service.GetBatchesAsync(actor));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task FullArticle_CompletesWithoutOpeningDetail_AndUsesOnlySelectedSources(
        bool guest, bool web, bool x)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?>
            {
                ["ExternalApis:UseMocks"] = guest ? "false" : "true",
                ["AiProviders:Gemini:ApiKey"] = "",
                ["SearchProviders:Tavily:ApiKey"] = "",
                ["SearchProviders:X:BearerToken"] = ""
            });
        if (guest)
        {
            using var guestScope = factory.Services.CreateScope();
            var guestDb = guestScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            guestDb.UserClaims.Add(new IdentityUserClaim<string>
            {
                UserId = userId,
                ClaimType = GuestIdentity.ClaimType,
                ClaimValue = GuestIdentity.ClaimValue
            });
            await guestDb.SaveChangesAsync();
        }

        using var client = await fixture.CreateAuthenticatedClientAsync(userId);
        using var response = await client.PostAsJsonAsync("/api/articles/bulk", new
        {
            lines = new[] { "家庭菜園", "整理整頓|指定したタイトル" },
            h2Count = 1,
            h3Count = 1,
            titleMethod = "Ai",
            outlineMethod = "Search",
            generationModel = "gemini-3.8-flash",
            searchMode = true,
            isDomesticOnly = true,
            generation = new
            {
                scope = "FullArticle",
                useWebSearch = web,
                useXSearch = x,
                webResultCount = 3,
                xResultCount = 3,
                xSearchDays = 7
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<BulkCreateArticlesResponse>();
        Assert.NotNull(accepted);
        await DrainAsync(factory.Services, userId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var articles = await db.Articles.Where(a => a.UserId == userId).ToListAsync();
        Assert.Equal(2, articles.Count);
        Assert.Equal("指定したタイトル", articles.Single(a => a.Keyword == "整理整頓").Title);
        foreach (var article in articles)
        {
            Assert.Equal(ArticleStatus.Completed, article.Status);
            Assert.False(string.IsNullOrWhiteSpace(article.Title));
            Assert.False(string.IsNullOrWhiteSpace(article.MetaDescription));
            Assert.Contains("## ", article.Body);
            Assert.Contains("<h2", article.HtmlBody);
            var headings = await db.ArticleHeadings.Where(h => h.ArticleId == article.Id).ToListAsync();
            Assert.Equal(2, headings.Count);
            Assert.All(headings, h => Assert.Equal(HeadingStatus.Generated, h.Status));
            Assert.Equal(web, await db.SearchResults.AnyAsync(r => r.ArticleId == article.Id));
            Assert.Equal(x, await db.XSearchPosts.AnyAsync(r => r.ArticleId == article.Id));
            Assert.False(await db.SearchResults.AnyAsync(r => r.ArticleId == article.Id && r.HeadingId != null));
            Assert.All(await db.AiGenerationLogs.Where(l => l.ArticleId == article.Id).ToListAsync(),
                log => Assert.Equal("Dummy", log.Provider));
        }
    }

    [Theory]
    [InlineData("Unknown", 10, 10, 30)]
    [InlineData("FullArticle", 0, 10, 30)]
    [InlineData("FullArticle", 21, 10, 30)]
    [InlineData("FullArticle", 10, 101, 30)]
    [InlineData("FullArticle", 10, 10, 0)]
    public async Task InvalidGenerationOptions_DoNotCreateArticles(string scope, int web, int x, int days)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var client = await fixture.CreateAuthenticatedClientAsync(userId);
        using var response = await client.PostAsJsonAsync("/api/articles/bulk", new
        {
            lines = new[] { "家庭菜園" },
            h2Count = 1,
            h3Count = 0,
            titleMethod = "Ai",
            outlineMethod = "Ai",
            generationModel = "gemini-3.8-flash",
            generation = new
            {
                scope,
                useWebSearch = true,
                useXSearch = true,
                webResultCount = web,
                xResultCount = x,
                xSearchDays = days
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var dbScope = fixture.Factory.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Articles.AnyAsync(a => a.UserId == userId));
    }

    [Fact]
    public async Task FailedBody_ResumesOnlyMissingHeadings_AndRejectsOtherUsers()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var client = new FailSecondBodyClient();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerationClient>();
            services.AddSingleton<IAiTextGenerationClient>(client);
        }));
        Guid articleId;
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IArticleCommandService>().BulkCreateAsync(
                new BulkCreateArticlesCommand(userId, ["家庭菜園|指定タイトル"], 1, 1, true, "Ai", "Ai",
                    "gemini-3.8-flash", false, null, false, null, null, new("FullArticle", false, false)));
            articleId = Assert.Single(result.Value!.Jobs).ArticleId;
        }
        await DrainAsync(factory.Services, userId, failImmediately: true);
        string firstBody;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.ArticleGenerationRuns.SingleAsync(r => r.ArticleId == articleId);
            Assert.Equal(JobStatus.Failed, run.Status);
            var headings = await db.ArticleHeadings.Where(h => h.ArticleId == articleId).OrderBy(h => h.DisplayOrder).ToListAsync();
            firstBody = headings[0].Body!;
            Assert.Equal(HeadingStatus.Generated, headings[0].Status);
            Assert.Equal(HeadingStatus.Failed, headings[1].Status);
            var service = scope.ServiceProvider.GetRequiredService<IBulkGenerationService>();
            Assert.Empty(await service.GetProgressAsync(new ArticleActor("someone-else", false), articleId: articleId));
            Assert.Equal(ArticleServiceError.NotFound, (await service.RetryAsync(new("someone-else", false), articleId)).Error);
            Assert.True((await service.RetryAsync(new(userId, false), articleId)).Succeeded);
            Assert.False((await service.RetryAsync(new(userId, false), articleId)).Succeeded);
        }
        await DrainAsync(factory.Services, userId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(ArticleStatus.Completed, (await db.Articles.SingleAsync(a => a.Id == articleId)).Status);
            Assert.Equal(firstBody, await db.ArticleHeadings.Where(h => h.ArticleId == articleId).OrderBy(h => h.DisplayOrder).Select(h => h.Body).FirstAsync());
            Assert.Equal(3, client.BodyCalls);
            Assert.Equal(1, await db.ArticleGenerationJobs.CountAsync(j => j.ArticleId == articleId && j.JobType == JobType.BodyGeneration));
        }
    }

    [Fact]
    public async Task OutlineOnly_StopAndResume_PreservesScope_AndBlocksEditsWhileActive()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?> { ["ExternalApis:UseMocks"] = "true" });
        Guid articleId;
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var result = await services.GetRequiredService<IArticleCommandService>().BulkCreateAsync(
                new BulkCreateArticlesCommand(userId, ["家庭菜園"], 1, 0, true, "Ai", "Search",
                    "gemini-3.8-flash", true, null, false, null, null, new("OutlineOnly", false, false)));
            articleId = Assert.Single(result.Value!.Jobs).ArticleId;
            var heading = await services.GetRequiredService<IArticleHeadingService>().CreateHeadingAsync(
                new(new(userId, false), articleId, null, 2, "手動見出し", null, 100, false));
            Assert.Equal(ArticleServiceError.ConflictRunningJob, heading.Error);
            var job = await services.GetRequiredService<IJobCommandService>().EnqueueAsync(
                new(new(userId, false), JobType.OutlineGeneration, articleId, null, "{}", 0));
            Assert.Equal(JobServiceError.RunningJobExists, job.Error);
            var service = services.GetRequiredService<IBulkGenerationService>();
            Assert.True((await service.StopAsync(new(userId, false), articleId)).Succeeded);
            Assert.Equal("Canceled", Assert.Single(await service.GetProgressAsync(new(userId, false), articleId: articleId)).Status);
            Assert.True((await service.RetryAsync(new(userId, false), articleId)).Succeeded);
        }
        await DrainAsync(factory.Services, userId);
        using var checkScope = factory.Services.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(ArticleStatus.OutlineReady, (await db.Articles.SingleAsync(a => a.Id == articleId)).Status);
        Assert.False(await db.ArticleGenerationJobs.AnyAsync(j => j.ArticleId == articleId && j.JobType == JobType.BodyGeneration));
        Assert.False(await db.SearchResults.AnyAsync(r => r.ArticleId == articleId));
    }

    [Fact]
    public async Task RunningStage_StopSurvivesWorkerScope_ThenResumesNextStageOnce()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?> { ["ExternalApis:UseMocks"] = "true" });
        using var workerScope = factory.Services.CreateScope();
        var services = workerScope.ServiceProvider;
        var result = await services.GetRequiredService<IArticleCommandService>().BulkCreateAsync(
            new BulkCreateArticlesCommand(userId, ["家庭菜園"], 1, 0, true, "Ai", "Ai",
                "gemini-3.8-flash", false, null, false, null, null, new("FullArticle", false, false)));
        var articleId = Assert.Single(result.Value!.Jobs).ArticleId;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var job = await db.ArticleGenerationJobs.SingleAsync(j => j.ArticleId == articleId);
        job.Status = JobStatus.Running;
        job.AttemptCount = 1;
        await db.SaveChangesAsync();
        using (var stopScope = factory.Services.CreateScope())
            Assert.True((await stopScope.ServiceProvider.GetRequiredService<IBulkGenerationService>()
                .StopAsync(new(userId, false), articleId)).Succeeded);
        var generated = await services.GetRequiredService<JobDispatcher>().DispatchAsync(new LeasedJob(
            job.Id, userId, articleId, null, job.JobType, job.PayloadJson, 1, job.MaxAttempts));
        await services.GetRequiredService<JobLeaseService>().MarkSucceededAsync(job.Id, generated.ResultJson);
        db.ChangeTracker.Clear();
        Assert.Equal(JobStatus.Canceled, (await db.ArticleGenerationRuns.SingleAsync(r => r.ArticleId == articleId)).Status);
        Assert.Equal(1, await db.ArticleGenerationJobs.CountAsync(j => j.ArticleId == articleId));
        using (var resumeScope = factory.Services.CreateScope())
            Assert.True((await resumeScope.ServiceProvider.GetRequiredService<IBulkGenerationService>()
                .RetryAsync(new(userId, false), articleId)).Succeeded);
        await DrainAsync(factory.Services, userId);
        db.ChangeTracker.Clear();
        Assert.Equal(ArticleStatus.Completed, (await db.Articles.SingleAsync(a => a.Id == articleId)).Status);
        Assert.Equal(3, await db.ArticleGenerationJobs.CountAsync(j => j.ArticleId == articleId));
    }

    [Fact]
    public async Task DisabledSources_ExcludePreviouslySavedReferences()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?> { ["ExternalApis:UseMocks"] = "true" });
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IArticleCommandService>().BulkCreateAsync(
            new BulkCreateArticlesCommand(userId, ["家庭菜園"], 1, 0, true, "Ai", "Ai",
                "gemini-3.8-flash", false, null, false, null, null, new("FullArticle", true, true)));
        var articleId = Assert.Single(result.Value!.Jobs).ArticleId;
        await DrainAsync(factory.Services, userId);
        var research = scope.ServiceProvider.GetRequiredService<IArticleResearchService>();
        Assert.Empty(await research.GetSelectedReferencesAsync(userId, articleId, new(false, false)));
        var web = await research.GetSelectedReferencesAsync(userId, articleId, new(true, false));
        var x = await research.GetSelectedReferencesAsync(userId, articleId, new(false, true));
        Assert.NotEmpty(web);
        Assert.NotEmpty(x);
        Assert.All(web, source => Assert.StartsWith("web-", source.SourceId));
        Assert.All(x, source => Assert.StartsWith("x-", source.SourceId));
    }

    [Fact]
    public async Task SearchFailure_StopsOnlyThatArticle_AndZeroResultsWarnAndContinue()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerationClient>();
            services.AddSingleton<IAiTextGenerationClient, DummyTextGenerationClient>();
            services.RemoveAll<IWebSearchClient>();
            services.AddSingleton<IWebSearchClient, ControlledSearchClient>();
        }));
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IArticleCommandService>().BulkCreateAsync(
                new BulkCreateArticlesCommand(userId, ["検索障害|タイトル1", "資料なし|タイトル2"], 1, 0, true, "Ai", "Ai",
                    "gemini-3.8-flash", true, null, false, null, null, new("FullArticle", true, false)));
            Assert.Equal(2, result.Value!.CreatedArticleCount);
        }
        await DrainAsync(factory.Services, userId, failImmediately: true);
        using var checkScope = factory.Services.CreateScope();
        var progress = await checkScope.ServiceProvider.GetRequiredService<IBulkGenerationService>().GetProgressAsync(new(userId, false));
        var failed = Assert.Single(progress, p => p.Keyword == "検索障害");
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("WebSearch", failed.Stage);
        var empty = Assert.Single(progress, p => p.Keyword == "資料なし");
        Assert.Equal("Succeeded", empty.Status);
        Assert.Contains("0件", empty.Warning);
        var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.ArticleGenerationJobs.AnyAsync(j => j.ArticleId == failed.ArticleId && j.JobType == JobType.BodyGeneration));
        Assert.Equal(ArticleStatus.Failed, (await db.Articles.SingleAsync(a => a.Id == failed.ArticleId)).Status);
    }

    private sealed class ControlledSearchClient : IWebSearchClient
    {
        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Query == "検索障害")
                throw new ExternalIntegrationException(ExternalIntegrationErrorCodes.Timeout, "テストで制御した検索のタイムアウト");
            return Task.FromResult<IReadOnlyList<WebSearchResult>>([]);
        }
    }

    private sealed class FailSecondBodyClient : IAiTextGenerationClient
    {
        private readonly DummyTextGenerationClient inner = new();
        public int BodyCalls { get; private set; }
        public Task<AiTextGenerationResult> GenerateAsync(AiTextGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Operation == AiOperations.BodyGeneration && ++BodyCalls == 2)
                throw new ExternalIntegrationException(ExternalIntegrationErrorCodes.Timeout, "テストで制御した本文生成のタイムアウト");
            return inner.GenerateAsync(request, cancellationToken);
        }
    }

    private static async Task DrainAsync(IServiceProvider provider, string userId, bool failImmediately = false)
    {
        for (var i = 0; i < 20; i++)
        {
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<ApplicationDbContext>();
            var job = await db.ArticleGenerationJobs.Where(j => j.UserId == userId && j.Status == JobStatus.Queued)
                .OrderBy(j => j.QueuedAt).FirstOrDefaultAsync();
            if (job is null) return;
            job.Status = JobStatus.Running;
            job.AttemptCount++;
            if (failImmediately) job.MaxAttempts = 1;
            await db.SaveChangesAsync();
            var leases = services.GetRequiredService<JobLeaseService>();
            JobExecutionResult result;
            try
            {
                result = await services.GetRequiredService<JobDispatcher>().DispatchAsync(new LeasedJob(
                    job.Id, job.UserId, job.ArticleId, job.HeadingId, job.JobType, job.PayloadJson, job.AttemptCount, job.MaxAttempts));
            }
            catch (JobExecutionException exception) when (failImmediately)
            {
                await leases.MarkFailureOrRetryAsync(job.Id, exception);
                continue;
            }
            await leases.MarkSucceededAsync(job.Id, result.ResultJson);
            await leases.MarkSucceededAsync(job.Id, result.ResultJson);
        }
        Assert.Fail("一括生成が20段階以内に終了しませんでした。");
    }
}
