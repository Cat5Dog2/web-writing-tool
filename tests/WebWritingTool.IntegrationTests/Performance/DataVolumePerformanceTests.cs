using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;
using Xunit.Abstractions;

namespace WebWritingTool.IntegrationTests.Performance;

// docs/test-design.md の NFT-PERF-001 から NFT-PERF-004。データ量増加ケースも兼ねる。
// Category=Performance は scripts/test.ps1 から除外し、夜間CIの performance ジョブだけで動かす。
// 実行時間が長く共有DBへ大量データを入れるため、PRとmainのCIでは動かさない。
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Performance")]
public class DataVolumePerformanceTests(IntegrationTestFixture fixture, ITestOutputHelper output)
{
    private const int ArticleCount = 1_000;
    private const int HeadingCount = 100;
    private const int QueuedJobCount = 10_000;

    // 最初の1回はEFモデル構築、接続確立、JITを含むため計測から外す。
    // 計測は3回行い中央値で判定する。1回だけだとCI runnerの瞬間的な負荷で結果がぶれる。
    private const int WarmupIterations = 1;
    private const int MeasuredIterations = 3;

    [Fact]
    public async Task NftPerf001_ArticleList_FirstPageOfTen_CompletesWithinOneSecond()
    {
        var userId = await SeedArticlesAsync();
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        var elapsed = await MeasureMedianAsync(async () =>
        {
            var list = await client.GetFromJsonAsync<ArticleListResponse>("/api/articles?page=1&pageSize=10");
            Assert.NotNull(list);
            Assert.Equal(10, list.Items.Count);
            Assert.Equal(ArticleCount, list.TotalCount);
        });

        output.WriteLine($"NFT-PERF-001 median={elapsed.TotalMilliseconds:F0}ms articles={ArticleCount}");
        Assert.True(
            elapsed < TimeSpan.FromSeconds(1),
            $"記事一覧10件表示が1秒を超えた。median={elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task NftPerf002_ArticleSearch_OverOneThousandArticles_CompletesWithinTwoSeconds()
    {
        var userId = await SeedArticlesAsync();
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        var elapsed = await MeasureMedianAsync(async () =>
        {
            var list = await client.GetFromJsonAsync<ArticleListResponse>(
                $"/api/articles?page=1&pageSize=20&q=perf-{userId}");
            Assert.NotNull(list);
            Assert.Equal(ArticleCount, list.TotalCount);
        });

        output.WriteLine($"NFT-PERF-002 median={elapsed.TotalMilliseconds:F0}ms articles={ArticleCount}");
        Assert.True(
            elapsed < TimeSpan.FromSeconds(2),
            $"記事検索が2秒を超えた。median={elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task NftPerf003_HeadingList_WithOneHundredHeadings_CompletesWithinTwoSeconds()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"perf-heading-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"perf-heading-keyword-{suffix}");
        await SeedHeadingsAsync(articleId);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        var elapsed = await MeasureMedianAsync(async () =>
        {
            var headings = await client.GetFromJsonAsync<ArticleHeadingListResponse>(
                $"/api/articles/{articleId}/headings");
            Assert.NotNull(headings);
            Assert.Equal(HeadingCount, headings.Items.Count);
        });

        output.WriteLine($"NFT-PERF-003 median={elapsed.TotalMilliseconds:F0}ms headings={HeadingCount}");
        Assert.True(
            elapsed < TimeSpan.FromSeconds(2),
            $"見出し取得が2秒を超えた。median={elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task NftPerf004_JobLease_WithTenThousandQueuedJobs_StaysStable()
    {
        const int acquisitions = 20;
        const int perAcquisitionBudgetMilliseconds = 1_000;

        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"perf-job-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"perf-job-keyword-{suffix}");
        await SeedQueuedJobsAsync(userId, articleId);

        var durations = new List<TimeSpan>(acquisitions);
        for (var index = 0; index < acquisitions + WarmupIterations; index++)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var leaseService = scope.ServiceProvider.GetRequiredService<JobLeaseService>();

            var start = Stopwatch.GetTimestamp();
            var leased = await leaseService.TryAcquireAsync($"perf-worker-{suffix}");
            var duration = Stopwatch.GetElapsedTime(start);

            Assert.NotNull(leased);
            if (index >= WarmupIterations)
            {
                durations.Add(duration);
            }
        }

        var max = durations.Max();
        var median = Median(durations);
        output.WriteLine(
            $"NFT-PERF-004 median={median.TotalMilliseconds:F0}ms max={max.TotalMilliseconds:F0}ms "
            + $"queued={QueuedJobCount} acquisitions={acquisitions}");

        // 「安定」の判定は、キューが1万件でも1件あたりの取得時間が予算内に収まり続けること。
        Assert.True(
            max < TimeSpan.FromMilliseconds(perAcquisitionBudgetMilliseconds),
            $"ジョブ取得が{perAcquisitionBudgetMilliseconds}msを超えた。max={max.TotalMilliseconds:F0}ms");
    }

    private async Task<string> SeedArticlesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"perf-article-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < ArticleCount; index++)
        {
            dbContext.Articles.Add(new Article
            {
                UserId = userId,
                Keyword = $"perf-{userId}-{index:D4}",
                Title = $"perf article {index:D4}",
                Status = ArticleStatus.Draft,
                Tags = ["perf"],
                GenerationModel = "gemini-3.7-flash",
                OutlineMethod = "Keyword",
                NotificationMode = "None",
                IsDomesticOnly = true
            });
        }

        await dbContext.SaveChangesAsync();
        return userId;
    }

    private async Task SeedHeadingsAsync(Guid articleId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < HeadingCount; index++)
        {
            dbContext.ArticleHeadings.Add(new ArticleHeading
            {
                ArticleId = articleId,
                Level = 2,
                Title = $"perf heading {index:D3}",
                Body = new string('x', 200),
                DisplayOrder = index,
                Status = HeadingStatus.Pending
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedQueuedJobsAsync(string userId, Guid articleId)
    {
        const int batchSize = 1_000;
        var queuedAt = DateTimeOffset.UtcNow;

        for (var offset = 0; offset < QueuedJobCount; offset += batchSize)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            for (var index = 0; index < batchSize; index++)
            {
                dbContext.ArticleGenerationJobs.Add(new ArticleGenerationJob
                {
                    UserId = userId,
                    ArticleId = articleId,
                    JobType = JobType.BodyGeneration,
                    Status = JobStatus.Queued,
                    Priority = 0,
                    PayloadJson = "{}",
                    QueuedAt = queuedAt.AddSeconds(offset + index)
                });
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task<TimeSpan> MeasureMedianAsync(Func<Task> action)
    {
        for (var index = 0; index < WarmupIterations; index++)
        {
            await action();
        }

        var durations = new List<TimeSpan>(MeasuredIterations);
        for (var index = 0; index < MeasuredIterations; index++)
        {
            var start = Stopwatch.GetTimestamp();
            await action();
            durations.Add(Stopwatch.GetElapsedTime(start));
        }

        return Median(durations);
    }

    private static TimeSpan Median(List<TimeSpan> durations)
    {
        var ordered = durations.Order().ToList();
        return ordered[ordered.Count / 2];
    }
}
