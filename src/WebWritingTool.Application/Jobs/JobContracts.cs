using WebWritingTool.Domain.Jobs;

namespace WebWritingTool.Application.Jobs;

public sealed record JobActor(string UserId, bool IsAdmin);

public sealed record EnqueueJobCommand(
    JobActor Actor,
    JobType JobType,
    Guid? ArticleId,
    Guid? HeadingId,
    string? PayloadJson,
    int Priority);

public sealed record JobAcceptedResponse(
    Guid JobId,
    Guid? ArticleId,
    Guid? HeadingId,
    string JobType,
    string Status,
    string StatusUrl);

public sealed record JobStatusResponse(
    Guid Id,
    Guid? ArticleId,
    Guid? HeadingId,
    string JobType,
    string Status,
    int Progress,
    int AttemptCount,
    int MaxAttempts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    object? Result);

public sealed record JobCancelResponse(Guid Id, string Status);

public enum JobServiceError
{
    None,
    ValidationFailed,
    NotFound,
    RunningJobExists,
    JobNotCancelable,
    JobNotRetryable
}

public sealed record JobValidationError(string Field, string Message);

public sealed record JobServiceResult(JobServiceError Error, IReadOnlyList<JobValidationError> ValidationErrors)
{
    public bool Succeeded => Error == JobServiceError.None;

    public static JobServiceResult Success { get; } = new(JobServiceError.None, []);

    public static JobServiceResult Failure(
        JobServiceError error,
        IReadOnlyList<JobValidationError>? validationErrors = null)
    {
        return new JobServiceResult(error, validationErrors ?? []);
    }
}

public sealed record JobServiceResult<T>(
    T? Value,
    JobServiceError Error,
    IReadOnlyList<JobValidationError> ValidationErrors)
{
    public bool Succeeded => Error == JobServiceError.None;

    public static JobServiceResult<T> Success(T value)
    {
        return new JobServiceResult<T>(value, JobServiceError.None, []);
    }

    public static JobServiceResult<T> Failure(
        JobServiceError error,
        IReadOnlyList<JobValidationError>? validationErrors = null)
    {
        return new JobServiceResult<T>(default, error, validationErrors ?? []);
    }
}
