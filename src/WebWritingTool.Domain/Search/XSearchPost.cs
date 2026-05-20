namespace WebWritingTool.Domain.Search;

public sealed class XSearchPost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid ArticleId { get; set; }

    public Guid? HeadingId { get; set; }

    public string Query { get; set; } = string.Empty;

    public string QueryHash { get; set; } = string.Empty;

    public string PostId { get; set; } = string.Empty;

    public string? AuthorId { get; set; }

    public string? Text { get; set; }

    public string? Url { get; set; }

    public string? Language { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public DateTimeOffset? CacheExpiresAt { get; set; }

    public DateTimeOffset? ContentExpiresAt { get; set; }

    public DateTimeOffset? MetadataExpiresAt { get; set; }
}
