using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;

namespace WebWritingTool.Application.Search;

public sealed record SearchDataMode(bool IsDummy);

public sealed record ArticleResearchRequest(string? Query, Guid? HeadingId = null, int? MaxResults = null);

public sealed record ResearchWebResult(
    string Title, string Url, string? Snippet, DateTimeOffset FetchedAt, bool IsManual = false);

public sealed record ResearchXPost(
    string PostId, string? AuthorId, string Text, string? Url,
    DateTimeOffset? PostedAt, DateTimeOffset FetchedAt);

public sealed record ArticleResearchResponse(
    bool IsDummy, IReadOnlyList<ResearchWebResult> WebResults,
    IReadOnlyList<ResearchXPost> XPosts, string? Warning);

public interface IArticleResearchService
{
    Task<JobServiceResult<JobAcceptedResponse>> EnqueueAsync(
        ArticleActor actor, Guid articleId, string source, ArticleResearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ArticleResearchResponse?> GetAsync(
        ArticleActor actor, Guid articleId, Guid? headingId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiReferenceSource>> GetReferencesAsync(
        string userId, Guid articleId, Guid? headingId, bool searchWeb,
        string? query = null, bool? domesticOnly = null, CancellationToken cancellationToken = default);
}
