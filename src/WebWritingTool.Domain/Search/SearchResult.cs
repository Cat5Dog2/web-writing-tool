namespace WebWritingTool.Domain.Search;

public sealed class SearchResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid ArticleId { get; set; }

    public Guid? HeadingId { get; set; }

    public string Query { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? Snippet { get; set; }

    public int Rank { get; set; }

    public string? Provider { get; set; }

    public string? QueryHash { get; set; }

    public DateTimeOffset? CacheExpiresAt { get; set; }

    public DateTimeOffset? RawJsonExpiresAt { get; set; }

    public DateTimeOffset? ContentExpiresAt { get; set; }

    public DateTimeOffset? MetadataExpiresAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
