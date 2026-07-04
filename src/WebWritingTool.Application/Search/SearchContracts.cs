using WebWritingTool.Application.Generation;

namespace WebWritingTool.Application.Search;

public static class SearchProviders
{
    public const string Tavily = "Tavily";
    public const string X = "X";
}

public sealed record WebSearchRequest(
    string Query,
    string? Region,
    string? Language,
    int MaxResults,
    bool DomesticOnly,
    string? Topic,
    string SearchDepth,
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeSpan? SearchCacheTtl,
    TimeSpan? ContentCacheTtl);

public sealed record WebSearchResult(
    string Title,
    string Url,
    string? Snippet,
    int Rank,
    string Provider);

public interface IWebSearchClient
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record XFullArchiveSearchRequest(
    string Query,
    string? Language,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int MaxResults,
    bool LargeResearchMode,
    bool ExcludeRetweets,
    bool ExcludeReplies,
    TimeSpan? RawCacheTtl,
    bool RehydrateBeforeDisplay);

public sealed record XPostRehydrationRequest(IReadOnlyList<string> PostIds);

public sealed record XSearchPostResult(
    string PostId,
    string? AuthorId,
    string Text,
    string? Url,
    string? Language,
    DateTimeOffset? PostedAt);

public interface IXFullArchiveSearchClient
{
    Task<IReadOnlyList<XSearchPostResult>> SearchAsync(
        XFullArchiveSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<XSearchPostResult>> RehydrateAsync(
        XPostRehydrationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record XPostRehydrationServiceResult(
    bool RehydrationRequired,
    int RequestedCount,
    int RefreshedCount,
    int MissingCount,
    int ChangedCount);

public interface IXPostRehydrationService
{
    Task<XPostRehydrationServiceResult> RehydrateCachedPostsAsync(
        string userId,
        IReadOnlyList<string> postIds,
        TopicRiskMode topicRiskMode,
        CancellationToken cancellationToken = default);
}

public sealed record WebSearchJobPayload(
    Guid ArticleId,
    Guid? HeadingId,
    string Query,
    string? Region,
    string? Language,
    int? MaxResults,
    bool? DomesticOnly,
    string? Topic,
    string? SearchDepth,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record XFullArchiveSearchJobPayload(
    Guid ArticleId,
    Guid? HeadingId,
    string Query,
    string? Language,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int? MaxResults,
    bool LargeResearchMode,
    bool? ExcludeRetweets,
    bool? ExcludeReplies);

public static class SearchExternalException
{
    public static ExternalIntegrationException MissingCredential(string provider)
    {
        return new ExternalIntegrationException(
            ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
            $"{provider} APIキーが設定されていません。");
    }
}
