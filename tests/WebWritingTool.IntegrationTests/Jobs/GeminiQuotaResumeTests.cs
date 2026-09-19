using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class GeminiQuotaResumeTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task DeferredBodyJob_ResumesMissingHeadingsWithoutRepeatingCompletedGeneration()
    {
        var user = Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(user, $"{user}@example.test");
        var articleId = await fixture.SeedArticleAsync(user, "家庭菜園");
        var client = new DeferSecondBodyClient();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerationClient>();
            services.AddSingleton<IAiTextGenerationClient>(client);
        }));
        Guid jobId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ArticleHeadings.AddRange(
                new ArticleHeading { ArticleId = articleId, Level = 2, Title = "土づくり", DisplayOrder = 0 },
                new ArticleHeading { ArticleId = articleId, Level = 2, Title = "水やり", DisplayOrder = 1 });
            await db.SaveChangesAsync();
            var queued = await scope.ServiceProvider.GetRequiredService<IJobCommandService>().EnqueueAsync(new(
                new JobActor(user, false), JobType.BodyGeneration, articleId, null,
                JsonSerializer.Serialize(new { articleId, scope = "All" }), int.MaxValue));
            Assert.True(queued.Succeeded, queued.Error.ToString());
            jobId = queued.Value!.JobId;
        }

        string firstBody;
        using (var scope = factory.Services.CreateScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
            var leased = await leases.TryAcquireAsync("quota-first");
            Assert.Equal(jobId, leased!.Id);
            var exception = await Assert.ThrowsAsync<ExternalApiDeferredException>(() =>
                scope.ServiceProvider.GetRequiredService<JobDispatcher>().DispatchAsync(leased));
            await leases.MarkFailureOrRetryAsync(jobId, exception);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = await db.ArticleGenerationJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(0, job.AttemptCount);
            Assert.Equal(JobStatus.Queued, job.Status);
            Assert.Equal(50, job.Progress);
            firstBody = (await db.ArticleHeadings.SingleAsync(h => h.ArticleId == articleId && h.DisplayOrder == 0)).Body!;
            Assert.False(string.IsNullOrWhiteSpace(firstBody));
            Assert.Single(await db.AiGenerationLogs.Where(log => log.JobId == jobId).ToListAsync());
            job.NextRunAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
            var leased = await leases.TryAcquireAsync("quota-resumed");
            Assert.Equal(jobId, leased!.Id);
            Assert.True(leased.IsResuming);
            Assert.Equal(1, leased.AttemptCount);
            var result = await scope.ServiceProvider.GetRequiredService<JobDispatcher>().DispatchAsync(leased);
            await leases.MarkSucceededAsync(jobId, result.ResultJson);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var headings = await db.ArticleHeadings.Where(h => h.ArticleId == articleId).OrderBy(h => h.DisplayOrder).ToListAsync();
            Assert.Equal(firstBody, headings[0].Body);
            Assert.All(headings, heading => Assert.Equal(HeadingStatus.Generated, heading.Status));
            Assert.Equal(ArticleStatus.Completed, (await db.Articles.SingleAsync(a => a.Id == articleId)).Status);
            Assert.Equal(2, await db.AiGenerationLogs.CountAsync(log => log.JobId == jobId && log.Succeeded));
            Assert.All(await db.AiGenerationLogs.Where(log => log.JobId == jobId).ToListAsync(), log =>
            {
                Assert.Equal(100, log.InputTokens);
                Assert.Equal(200, log.OutputTokens);
            });
            Assert.Equal(2, await db.UsageLedgers.CountAsync(log => log.JobId == jobId));
            Assert.Equal(3, client.Calls);
        }
    }

    private sealed class DeferSecondBodyClient : IAiTextGenerationClient
    {
        private readonly DummyTextGenerationClient _inner = new();
        public int Calls { get; private set; }

        public async Task<AiTextGenerationResult> GenerateAsync(AiTextGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (++Calls == 2) throw new ExternalApiDeferredException(DateTimeOffset.UtcNow.AddMinutes(1));
            return (await _inner.GenerateAsync(request, cancellationToken)) with { InputTokens = 100, OutputTokens = 200 };
        }
    }
}
