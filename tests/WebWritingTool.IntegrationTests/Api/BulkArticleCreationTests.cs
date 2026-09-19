using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public sealed class BulkArticleCreationTests(IntegrationTestFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(20, 60)]
    [InlineData(null, null)]
    public async Task BulkCreate_WithMixedLines_PersistsOutlineJobsWithRequestedSettings(int? h2Count, int? h3Count)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var client = await fixture.CreateAuthenticatedClientAsync(userId);
        using var response = await client.PostAsJsonAsync("/api/articles/bulk", new
        {
            lines = new[] { "家庭菜園", "不正|タイトル|余分", "整理整頓|整理の基本" },
            h2Count,
            h3Count,
            titleMethod = "Ai",
            outlineMethod = "Keyword",
            generationModel = "gemini-3.8-flash",
            searchMode = false,
            isDomesticOnly = true
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BulkCreateArticlesResponse>();
        Assert.NotNull(result);
        Assert.Equal(2, result.CreatedArticleCount);
        Assert.Equal(2, result.Jobs.Count);
        Assert.Equal(2, Assert.Single(result.RejectedLines).LineNumber);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var articles = await db.Articles.Where(article => article.UserId == userId).ToListAsync();
        Assert.Equal(2, articles.Count);
        Assert.DoesNotContain(articles, article => article.Keyword == "不正");
        Assert.Equal("整理の基本", articles.Single(article => article.Keyword == "整理整頓").Title);
        foreach (var accepted in result.Jobs)
        {
            var article = Assert.Single(articles, item => item.Id == accepted.ArticleId);
            Assert.Equal(ArticleStatus.OutlineQueued, article.Status);
            var job = await db.ArticleGenerationJobs.SingleAsync(item => item.Id == accepted.JobId);
            Assert.Equal(userId, job.UserId);
            Assert.Equal(article.Id, job.ArticleId);
            Assert.Equal(JobType.OutlineGeneration, job.JobType);
            Assert.Equal(JobStatus.Queued, job.Status);
            Assert.Equal($"/api/jobs/{job.Id}", accepted.StatusUrl);
            Assert.Equal("OutlineGeneration", accepted.JobType);
            Assert.Equal("Queued", accepted.Status);
            var payload = JsonSerializer.Deserialize<OutlineGenerationPayload>(job.PayloadJson, JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(article.Id, payload.ArticleId);
            Assert.Equal(article.Keyword, payload.Keyword);
            Assert.Equal(article.Title, payload.Title);
            Assert.Equal(h2Count, payload.H2Count);
            Assert.Equal(h3Count, payload.H3Count);
            Assert.Equal("Keyword", payload.OutlineMethod);
            Assert.Equal("gemini-3.8-flash", payload.GenerationModel);
            Assert.False(payload.SearchMode);
            Assert.True(payload.IsDomesticOnly);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(21, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 61)]
    public async Task BulkCreate_WithInvalidCounts_CreatesNeitherArticlesNorJobs(int h2Count, int h3Count)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IArticleCommandService>()
            .BulkCreateAsync(CreateCommand(userId, h2Count, h3Count));

        Assert.False(result.Succeeded);
        Assert.Equal(ArticleServiceError.ValidationFailed, result.Error);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Articles.AnyAsync(article => article.UserId == userId));
        Assert.False(await db.ArticleGenerationJobs.AnyAsync(job => job.UserId == userId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkCreate_QueuedJobs_GenerateRequestedHeadingsWithoutExternalApis(bool guestMode)
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?>
            {
                ["ExternalApis:UseMocks"] = guestMode ? "false" : "true",
                ["AiProviders:Gemini:ApiKey"] = "",
                ["SearchProviders:Tavily:ApiKey"] = ""
            });
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (guestMode)
        {
            db.UserClaims.Add(new IdentityUserClaim<string>
            {
                UserId = userId,
                ClaimType = GuestIdentity.ClaimType,
                ClaimValue = GuestIdentity.ClaimValue
            });
            await db.SaveChangesAsync();
        }

        var result = await services.GetRequiredService<IArticleCommandService>()
            .BulkCreateAsync(CreateCommand(userId, 1, 0));
        Assert.True(result.Succeeded);
        var accepted = Assert.Single(result.Value!.Jobs);
        var job = await db.ArticleGenerationJobs.SingleAsync(item => item.Id == accepted.JobId);
        await services.GetRequiredService<JobDispatcher>().DispatchAsync(new LeasedJob(
            job.Id, job.UserId, job.ArticleId, job.HeadingId, job.JobType, job.PayloadJson, 1, job.MaxAttempts));

        db.ChangeTracker.Clear();
        var article = await db.Articles.SingleAsync(item => item.Id == accepted.ArticleId);
        Assert.Equal(ArticleStatus.OutlineReady, article.Status);
        var headings = await db.ArticleHeadings.Where(item => item.ArticleId == article.Id).ToListAsync();
        Assert.Equal(2, Assert.Single(headings).Level);
        Assert.Contains("家庭菜園", headings[0].Title);
        var log = await db.AiGenerationLogs.SingleAsync(item => item.ArticleId == article.Id);
        Assert.True(log.Succeeded);
        Assert.Equal("Dummy", log.Provider);
    }

    private static BulkCreateArticlesCommand CreateCommand(string userId, int? h2Count, int? h3Count) =>
        new(userId, ["家庭菜園|家庭菜園の基本"], h2Count, h3Count, true,
            "Ai", "Keyword", "gemini-3.8-flash", false, null, false, null, null);
}
