namespace WebWritingTool.Domain.Jobs;

public sealed class ArticleGenerationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Guid ArticleId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public JobType Stage { get; set; }
    public bool StopRequested { get; set; }
    public string? Warning { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}
