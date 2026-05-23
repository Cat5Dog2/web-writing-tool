using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Jobs;

public sealed class JobService(
    ApplicationDbContext dbContext,
    JobRetryPolicy retryPolicy)
    : IJobCommandService, IJobQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobServiceResult<JobAcceptedResponse>> EnqueueAsync(
        EnqueueJobCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateEnqueue(command);
        if (validationErrors.Count > 0)
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(
                JobServiceError.ValidationFailed,
                validationErrors);
        }

        var target = await ResolveTargetAsync(command, cancellationToken);
        if (!target.Found || !CanAccess(command.Actor, target.OwnerUserId))
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.NotFound);
        }

        var hasDuplicate = await HasDuplicateJobAsync(command, cancellationToken);
        if (hasDuplicate)
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.RunningJobExists);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        ApplyQueuedState(command.JobType, target.Article, target.Heading);

        var job = new ArticleGenerationJob
        {
            UserId = target.OwnerUserId,
            ArticleId = target.Article?.Id ?? command.ArticleId,
            HeadingId = target.Heading?.Id ?? command.HeadingId,
            JobType = command.JobType,
            Status = JobStatus.Queued,
            Priority = command.Priority,
            Progress = 0,
            PayloadJson = NormalizePayloadJson(command.PayloadJson),
            AttemptCount = 0,
            MaxAttempts = retryPolicy.GetMaxAttempts(command.JobType),
            QueuedAt = now
        };

        dbContext.ArticleGenerationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return JobServiceResult<JobAcceptedResponse>.Success(ToAcceptedResponse(job));
    }

    public Task<JobServiceResult<JobCancelResponse>> CancelAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return CancelCoreAsync(actor, jobId, cancellationToken);
    }

    public Task<JobServiceResult<JobAcceptedResponse>> RetryAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return RetryCoreAsync(actor, jobId, cancellationToken);
    }

    public async Task<JobStatusResponse?> GetAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.ArticleGenerationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);

        if (job is null || !CanAccess(actor, job.UserId))
        {
            return null;
        }

        return ToStatusResponse(job);
    }

    private async Task<JobServiceResult<JobCancelResponse>> CancelCoreAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.ArticleGenerationJobs.FirstOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);

        if (job is null || !CanAccess(actor, job.UserId))
        {
            return JobServiceResult<JobCancelResponse>.Failure(JobServiceError.NotFound);
        }

        if (job.Status != JobStatus.Queued)
        {
            return JobServiceResult<JobCancelResponse>.Failure(JobServiceError.JobNotCancelable);
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Canceled;
        job.CanceledAt = now;
        job.FinishedAt = now;
        job.NextRunAt = null;
        job.LockedBy = null;
        job.LockedAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return JobServiceResult<JobCancelResponse>.Success(
            new JobCancelResponse(job.Id, job.Status.ToString()));
    }

    private async Task<JobServiceResult<JobAcceptedResponse>> RetryCoreAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var original = await dbContext.ArticleGenerationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);

        if (original is null || !CanAccess(actor, original.UserId))
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.NotFound);
        }

        if (original.Status != JobStatus.Failed
            || !retryPolicy.IsRetryableError(original.ErrorCode))
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.JobNotRetryable);
        }

        return await EnqueueAsync(
            new EnqueueJobCommand(
                actor,
                original.JobType,
                original.ArticleId,
                original.HeadingId,
                original.PayloadJson,
                original.Priority),
            cancellationToken);
    }

    private static List<JobValidationError> ValidateEnqueue(EnqueueJobCommand command)
    {
        var errors = new List<JobValidationError>();

        if (string.IsNullOrWhiteSpace(command.Actor.UserId))
        {
            errors.Add(new JobValidationError(nameof(command.Actor.UserId), "ユーザーIDが不正です。"));
        }

        if (command.ArticleId is null && command.HeadingId is not null)
        {
            errors.Add(new JobValidationError(nameof(command.ArticleId), "見出しジョブでは記事IDを指定してください。"));
        }

        if ((command.JobType is JobType.OutlineGeneration
                or JobType.WebSearch
                or JobType.XFullArchiveSearch
                or JobType.WordpressPost)
            && command.ArticleId is null)
        {
            errors.Add(new JobValidationError(nameof(command.ArticleId), "記事IDを指定してください。"));
        }

        if ((command.JobType is JobType.BodyGeneration or JobType.Rewrite)
            && command.ArticleId is null)
        {
            errors.Add(new JobValidationError(nameof(command.ArticleId), "記事IDを指定してください。"));
        }

        if (command.JobType == JobType.Rewrite && command.HeadingId is null)
        {
            errors.Add(new JobValidationError(nameof(command.HeadingId), "リライト対象の見出しIDを指定してください。"));
        }

        if (!IsValidPayloadJson(command.PayloadJson))
        {
            errors.Add(new JobValidationError(nameof(command.PayloadJson), "PayloadJsonは有効なJSONで指定してください。"));
        }

        return errors;
    }

    private async Task<JobTarget> ResolveTargetAsync(
        EnqueueJobCommand command,
        CancellationToken cancellationToken)
    {
        Article? article = null;
        ArticleHeading? heading = null;

        if (command.ArticleId.HasValue)
        {
            article = await dbContext.Articles.FirstOrDefaultAsync(
                item => item.Id == command.ArticleId.Value,
                cancellationToken);
        }

        if (command.HeadingId.HasValue)
        {
            heading = await dbContext.ArticleHeadings.FirstOrDefaultAsync(
                item => item.Id == command.HeadingId.Value,
                cancellationToken);

            if (heading is not null
                && (!command.ArticleId.HasValue || heading.ArticleId != command.ArticleId.Value))
            {
                return JobTarget.NotFound;
            }
        }

        if (article is null && heading is not null)
        {
            article = await dbContext.Articles.FirstOrDefaultAsync(
                item => item.Id == heading.ArticleId,
                cancellationToken);
        }

        if (command.ArticleId.HasValue && article is null)
        {
            return JobTarget.NotFound;
        }

        if (command.HeadingId.HasValue && heading is null)
        {
            return JobTarget.NotFound;
        }

        var ownerUserId = article?.UserId ?? command.Actor.UserId;
        return new JobTarget(true, ownerUserId, article, heading);
    }

    private async Task<bool> HasDuplicateJobAsync(
        EnqueueJobCommand command,
        CancellationToken cancellationToken)
    {
        var activeJobs = dbContext.ArticleGenerationJobs
            .Where(job => job.Status == JobStatus.Queued || job.Status == JobStatus.Running);

        return command.JobType switch
        {
            JobType.OutlineGeneration when command.ArticleId.HasValue => await activeJobs.AnyAsync(
                job => job.ArticleId == command.ArticleId.Value
                    && (job.JobType == JobType.OutlineGeneration
                        || (job.JobType == JobType.BodyGeneration && job.HeadingId == null)),
                cancellationToken),

            JobType.BodyGeneration when command.HeadingId.HasValue => await activeJobs.AnyAsync(
                job => job.HeadingId == command.HeadingId.Value
                    && (job.JobType == JobType.BodyGeneration || job.JobType == JobType.Rewrite),
                cancellationToken),

            JobType.BodyGeneration when command.ArticleId.HasValue => await activeJobs.AnyAsync(
                job => job.ArticleId == command.ArticleId.Value
                    && job.HeadingId == null
                    && (job.JobType == JobType.OutlineGeneration || job.JobType == JobType.BodyGeneration),
                cancellationToken),

            JobType.Rewrite when command.HeadingId.HasValue => await activeJobs.AnyAsync(
                job => job.HeadingId == command.HeadingId.Value
                    && (job.JobType == JobType.BodyGeneration || job.JobType == JobType.Rewrite),
                cancellationToken),

            JobType.WordpressPost when command.ArticleId.HasValue => await activeJobs.AnyAsync(
                job => job.ArticleId == command.ArticleId.Value && job.JobType == JobType.WordpressPost,
                cancellationToken),

            _ => await activeJobs.AnyAsync(
                job => job.JobType == command.JobType
                    && job.ArticleId == command.ArticleId
                    && job.HeadingId == command.HeadingId,
                cancellationToken)
        };
    }

    private static void ApplyQueuedState(
        JobType jobType,
        Article? article,
        ArticleHeading? heading)
    {
        switch (jobType)
        {
            case JobType.OutlineGeneration when article is not null:
                article.Status = ArticleStatus.OutlineQueued;
                break;
            case JobType.BodyGeneration when heading is not null:
                heading.Status = HeadingStatus.Queued;
                break;
            case JobType.BodyGeneration when article is not null:
                article.Status = ArticleStatus.BodyQueued;
                break;
            case JobType.Rewrite when heading is not null:
                heading.Status = HeadingStatus.Queued;
                break;
        }
    }

    private static JobAcceptedResponse ToAcceptedResponse(ArticleGenerationJob job)
    {
        return new JobAcceptedResponse(
            job.Id,
            job.ArticleId,
            job.HeadingId,
            job.JobType.ToString(),
            job.Status.ToString(),
            $"/api/jobs/{job.Id}");
    }

    private static JobStatusResponse ToStatusResponse(ArticleGenerationJob job)
    {
        return new JobStatusResponse(
            job.Id,
            job.ArticleId,
            job.HeadingId,
            job.JobType.ToString(),
            job.Status.ToString(),
            job.Progress,
            job.AttemptCount,
            job.MaxAttempts,
            job.ErrorCode,
            job.ErrorMessage,
            job.QueuedAt,
            job.StartedAt,
            job.FinishedAt,
            DeserializeResult(job.ResultJson));
    }

    private static object? DeserializeResult(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(resultJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidPayloadJson(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind is JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizePayloadJson(string? payloadJson)
    {
        return string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
    }

    private static bool CanAccess(JobActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }

    private sealed record JobTarget(
        bool Found,
        string OwnerUserId,
        Article? Article,
        ArticleHeading? Heading)
    {
        public static JobTarget NotFound { get; } = new(false, string.Empty, null, null);
    }
}
