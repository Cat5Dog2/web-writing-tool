using WebWritingTool.Domain.Jobs;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed record LeasedJob(
    Guid Id,
    string UserId,
    Guid? ArticleId,
    Guid? HeadingId,
    JobType JobType,
    string PayloadJson,
    int AttemptCount,
    int MaxAttempts,
    Guid? GenerationRunId = null)
{
    public bool IsResuming { get; init; }
}

public sealed record JobExecutionResult(string? ResultJson);
