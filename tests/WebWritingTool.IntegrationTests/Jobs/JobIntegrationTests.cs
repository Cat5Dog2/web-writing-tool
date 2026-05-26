using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class JobIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task TryAcquireAsync_LocksQueuedJobAndMovesItToRunning()
    {
        var context = await CreateJobContextAsync(priority: 1000);

        using var scope = fixture.Factory.Services.CreateScope();
        var leaseService = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
        var leasedJob = await leaseService.TryAcquireAsync("worker-a");

        Assert.NotNull(leasedJob);
        Assert.Equal(context.JobId, leasedJob.Id);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await dbContext.ArticleGenerationJobs.SingleAsync(item => item.Id == context.JobId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal("worker-a", job.LockedBy);
        Assert.NotNull(job.LockedAt);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public async Task MarkSucceededAsync_CompletesRunningJob()
    {
        var context = await CreateJobContextAsync(priority: 2000);

        using var scope = fixture.Factory.Services.CreateScope();
        var leaseService = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
        var leasedJob = await leaseService.TryAcquireAsync("worker-success");
        Assert.NotNull(leasedJob);

        await leaseService.MarkSucceededAsync(context.JobId, """{"ok":true}""");

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await dbContext.ArticleGenerationJobs.SingleAsync(item => item.Id == context.JobId);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(100, job.Progress);
        Assert.Equal("""{"ok":true}""", job.ResultJson);
        Assert.Null(job.LockedBy);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task MarkFailureOrRetryAsync_WithRetryableError_RequeuesJob()
    {
        var context = await CreateJobContextAsync(JobStatus.Running, attemptCount: 1);

        using var scope = fixture.Factory.Services.CreateScope();
        var leaseService = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
        var decision = await leaseService.MarkFailureOrRetryAsync(
            context.JobId,
            new JobExecutionException(
                JobErrorCodes.RateLimited,
                "rate limited",
                retryAfter: TimeSpan.FromSeconds(30)));

        Assert.NotNull(decision);
        Assert.True(decision.Retried);
        Assert.Equal(JobErrorCodes.RateLimited, decision.ErrorCode);
        Assert.NotNull(decision.NextRunAt);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await dbContext.ArticleGenerationJobs.SingleAsync(item => item.Id == context.JobId);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Null(job.LockedBy);
        Assert.NotNull(job.NextRunAt);
        Assert.Null(job.FinishedAt);
    }

    [Fact]
    public async Task CancelAndRetryAsync_TransitionsJobsThroughCommandService()
    {
        var queuedContext = await CreateJobContextAsync(JobStatus.Queued);
        var failedContext = await CreateJobContextAsync(
            JobStatus.Failed,
            attemptCount: 3,
            errorCode: JobErrorCodes.Timeout);

        using var scope = fixture.Factory.Services.CreateScope();
        var jobCommandService = scope.ServiceProvider.GetRequiredService<IJobCommandService>();

        var cancelResult = await jobCommandService.CancelAsync(
            new JobActor(queuedContext.UserId, IsAdmin: false),
            queuedContext.JobId);
        Assert.True(cancelResult.Succeeded, cancelResult.Error.ToString());
        Assert.Equal(JobStatus.Canceled.ToString(), cancelResult.Value!.Status);

        var retryResult = await jobCommandService.RetryAsync(
            new JobActor(failedContext.UserId, IsAdmin: false),
            failedContext.JobId);
        Assert.True(retryResult.Succeeded, retryResult.Error.ToString());
        Assert.Equal(JobStatus.Queued.ToString(), retryResult.Value!.Status);
        Assert.NotEqual(failedContext.JobId, retryResult.Value.JobId);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            JobStatus.Canceled,
            await dbContext.ArticleGenerationJobs
                .Where(job => job.Id == queuedContext.JobId)
                .Select(job => job.Status)
                .SingleAsync());
        Assert.True(await dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.Id == retryResult.Value.JobId && job.Status == JobStatus.Queued));
    }

    private async Task<JobContext> CreateJobContextAsync(
        JobStatus status = JobStatus.Queued,
        int attemptCount = 0,
        int priority = 0,
        string? errorCode = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"job-user-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"job-keyword-{suffix}");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = new ArticleGenerationJob
        {
            UserId = userId,
            ArticleId = articleId,
            JobType = JobType.OutlineGeneration,
            Status = status,
            Priority = priority,
            Progress = status == JobStatus.Failed ? 50 : 0,
            PayloadJson = "{}",
            AttemptCount = attemptCount,
            MaxAttempts = 3,
            QueuedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            StartedAt = status is JobStatus.Running or JobStatus.Failed ? DateTimeOffset.UtcNow.AddMinutes(-1) : null,
            FinishedAt = status == JobStatus.Failed ? DateTimeOffset.UtcNow : null,
            LockedBy = status == JobStatus.Running ? "worker-existing" : null,
            LockedAt = status == JobStatus.Running ? DateTimeOffset.UtcNow.AddSeconds(-10) : null,
            ErrorCode = errorCode,
            ErrorMessage = errorCode is null ? null : "failed"
        };

        dbContext.ArticleGenerationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        return new JobContext(userId, articleId, job.Id);
    }

    private sealed record JobContext(string UserId, Guid ArticleId, Guid JobId);
}
