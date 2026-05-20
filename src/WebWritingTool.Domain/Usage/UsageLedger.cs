namespace WebWritingTool.Domain.Usage;

public sealed class UsageLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid? ArticleId { get; set; }

    public Guid? JobId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public int PromptChars { get; set; }

    public int OutputChars { get; set; }

    public int UsageChars { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
