namespace WebWritingTool.Domain.Jobs;

public sealed class ArticleGenerationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid? ArticleId { get; set; }

    public Guid? HeadingId { get; set; }

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Queued;

    public int Priority { get; set; }

    public int Progress { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public string? ResultJson { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public DateTimeOffset? NextRunAt { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
