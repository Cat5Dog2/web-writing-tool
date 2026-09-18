using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class DummyArticleGenerationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task DummyMode_GeneratesTitlesOutlineAndBodyWithoutApiKeys()
    {
        var userId = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(userId, $"{userId}@example.test");
        var articleId = await fixture.SeedArticleAsync(userId, "家庭菜園");
        using var factory = new TestApplicationFactory(fixture.ConnectionString, configurationOverrides:
            new Dictionary<string, string?>
            {
                ["ExternalApis:UseMocks"] = "true",
                ["AiProviders:Gemini:ApiKey"] = "",
                ["SearchProviders:Tavily:ApiKey"] = "",
                ["SearchProviders:X:BearerToken"] = ""
            });

        using var browser = factory.CreateClient();
        var loginHtml = await browser.GetStringAsync("/login");
        Assert.Contains("ダミーモード", System.Net.WebUtility.HtmlDecode(loginHtml));

        var titles = await ExecuteAsync(factory, userId, articleId, JobType.TitleGeneration,
            new { articleId, candidateCount = 3 });
        using var titleResult = JsonDocument.Parse(titles.ResultJson!);
        Assert.Equal(3, titleResult.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.All(titleResult.RootElement.GetProperty("candidates").EnumerateArray(),
            candidate => Assert.Contains("家庭菜園", candidate.GetProperty("title").GetString()));

        await ExecuteAsync(factory, userId, articleId, JobType.OutlineGeneration,
            new { articleId, h2Count = 2, h3Count = 3 });
        await ExecuteAsync(factory, userId, articleId, JobType.BodyGeneration,
            new { articleId, scope = "All", useWebSearch = true });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var article = await db.Articles.SingleAsync(item => item.Id == articleId);
        var headings = await db.ArticleHeadings.Where(item => item.ArticleId == articleId).ToListAsync();
        Assert.Equal(ArticleStatus.Completed, article.Status);
        Assert.Equal(2, headings.Count(item => item.Level == 2));
        Assert.Equal(3, headings.Count(item => item.Level == 3));
        Assert.All(headings, heading =>
        {
            Assert.Equal(HeadingStatus.Generated, heading.Status);
            Assert.Contains("ダミー", heading.Body);
            Assert.Contains("家庭菜園", heading.Body);
            Assert.Equal(heading.Body!.Length, heading.ActualLength);
        });
        Assert.Contains("## ", article.Body);
        Assert.Contains("ダミー", article.Body);
        var logs = await db.AiGenerationLogs.Where(item => item.ArticleId == articleId).ToListAsync();
        Assert.Equal(7, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.True(log.Succeeded);
            Assert.Equal("Dummy", log.Provider);
        });
    }

    private static async Task<JobExecutionResult> ExecuteAsync(
        TestApplicationFactory factory, string userId, Guid articleId, JobType type, object payload)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var queued = await services.GetRequiredService<IJobCommandService>().EnqueueAsync(
            new EnqueueJobCommand(new JobActor(userId, false), type, articleId, null,
                JsonSerializer.Serialize(payload), int.MaxValue));
        Assert.True(queued.Succeeded, queued.Error.ToString());
        var leases = services.GetRequiredService<JobLeaseService>();
        var job = await leases.TryAcquireAsync("dummy-generation-test");
        Assert.NotNull(job);
        Assert.Equal(queued.Value!.JobId, job.Id);
        var result = await services.GetRequiredService<JobDispatcher>().DispatchAsync(job);
        await leases.MarkSucceededAsync(job.Id, result.ResultJson);
        return result;
    }
}
