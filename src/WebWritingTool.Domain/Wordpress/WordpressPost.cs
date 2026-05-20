using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Wordpress;

public sealed class WordpressPost : ICreatedAtEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid ArticleId { get; set; }

    public Guid WordpressSiteId { get; set; }

    public Guid? JobId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? PostId { get; set; }

    public string? PostUrl { get; set; }

    public int? CategoryId { get; set; }

    public string RequestedStatus { get; set; } = "Draft";

    public WordpressPostStatus Status { get; set; } = WordpressPostStatus.Queued;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
}
