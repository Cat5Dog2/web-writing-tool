using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class JobLeaseService(
    ApplicationDbContext dbContext,
    IOptions<BackgroundJobOptions> options,
    JobRetryPolicy retryPolicy,
    ISecretMasker secretMasker)
{
    public async Task<LeasedJob?> TryAcquireAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var queuedStatus = JobStatus.Queued.ToString();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var jobs = await dbContext.ArticleGenerationJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ArticleGenerationJobs"
                WHERE "Status" = {queuedStatus}
                  AND ("NextRunAt" IS NULL OR "NextRunAt" <= {now})
                ORDER BY "Priority" DESC, "QueuedAt" ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);

        var job = jobs.SingleOrDefault();
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Status = JobStatus.Running;
        job.LockedBy = workerId;
        job.LockedAt = now;
        job.StartedAt ??= now;
        job.AttemptCount += 1;
        job.Progress = 0;
        job.NextRunAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToLeasedJob(job);
    }

    public async Task MarkSucceededAsync(
        Guid jobId,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.ArticleGenerationJobs.FirstOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);

        if (job is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Succeeded;
        job.Progress = 100;
        job.ResultJson = resultJson;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.LockedBy = null;
        job.LockedAt = null;
        job.FinishedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<JobFailureDecision?> MarkFailureOrRetryAsync(
        Guid jobId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.ArticleGenerationJobs.FirstOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);

        if (job is null)
        {
            return null;
        }

        var failure = JobFailure.FromException(exception);
        var now = DateTimeOffset.UtcNow;
        var shouldRetry = retryPolicy.CanRetry(failure.ErrorCode, job.AttemptCount, job.MaxAttempts);

        job.ErrorCode = failure.ErrorCode;
        job.ErrorMessage = SanitizeMessage(failure.UserMessage);
        job.LockedBy = null;
        job.LockedAt = null;

        if (shouldRetry)
        {
            job.Status = JobStatus.Queued;
            job.NextRunAt = retryPolicy.CalculateNextRunAt(now, job.AttemptCount, failure.RetryAfter);
            job.FinishedAt = null;
        }
        else
        {
            job.Status = JobStatus.Failed;
            job.FinishedAt = now;
            job.NextRunAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new JobFailureDecision(
            job.Id,
            job.JobType,
            job.AttemptCount,
            job.MaxAttempts,
            failure.ErrorCode,
            job.ErrorMessage,
            shouldRetry,
            job.NextRunAt);
    }

    public async Task<int> RecoverExpiredLocksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresBefore = now.Subtract(options.Value.LockTimeout);
        var expiredJobs = await dbContext.ArticleGenerationJobs
            .Where(job => job.Status == JobStatus.Running
                && job.LockedAt != null
                && job.LockedAt < expiresBefore)
            .ToListAsync(cancellationToken);

        foreach (var job in expiredJobs)
        {
            job.ErrorCode = JobErrorCodes.Timeout;
            job.ErrorMessage = "ジョブのロック期限が切れました。";
            job.LockedBy = null;
            job.LockedAt = null;

            if (retryPolicy.CanRetry(job.ErrorCode, job.AttemptCount, job.MaxAttempts))
            {
                job.Status = JobStatus.Queued;
                job.NextRunAt = retryPolicy.CalculateNextRunAt(now, job.AttemptCount);
                job.FinishedAt = null;
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.NextRunAt = null;
                job.FinishedAt = now;
            }
        }

        if (expiredJobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expiredJobs.Count;
    }

    private static LeasedJob ToLeasedJob(ArticleGenerationJob job)
    {
        return new LeasedJob(
            job.Id,
            job.UserId,
            job.ArticleId,
            job.HeadingId,
            job.JobType,
            job.PayloadJson,
            job.AttemptCount,
            job.MaxAttempts);
    }

    private string SanitizeMessage(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "ジョブ処理に失敗しました。"
            : secretMasker.Mask(message).Trim();

        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }
}

public sealed record JobFailureDecision(
    Guid JobId,
    JobType JobType,
    int AttemptCount,
    int MaxAttempts,
    string ErrorCode,
    string? ErrorMessage,
    bool Retried,
    DateTimeOffset? NextRunAt);

internal sealed record JobFailure(string ErrorCode, string UserMessage, TimeSpan? RetryAfter)
{
    public static JobFailure FromException(Exception exception)
    {
        return exception switch
        {
            JobExecutionException jobException => new JobFailure(
                jobException.ErrorCode,
                jobException.UserMessage,
                jobException.RetryAfter),
            TimeoutException => new JobFailure(
                JobErrorCodes.Timeout,
                "外部サービスまたは内部処理がタイムアウトしました。",
                null),
            OperationCanceledException => new JobFailure(
                JobErrorCodes.Timeout,
                "ジョブ処理がキャンセルされました。",
                null),
            _ => new JobFailure(
                JobErrorCodes.UnknownError,
                "ジョブ処理に失敗しました。",
                null)
        };
    }
}
