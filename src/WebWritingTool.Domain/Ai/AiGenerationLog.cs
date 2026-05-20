using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Ai;

public sealed class AiGenerationLog : ICreatedAtEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid? ArticleId { get; set; }

    public Guid? JobId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string? PromptHash { get; set; }

    public int PromptChars { get; set; }

    public int OutputChars { get; set; }

    public int UsageChars { get; set; }

    public int? LatencyMs { get; set; }

    public bool Succeeded { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
