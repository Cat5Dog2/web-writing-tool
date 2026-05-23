using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class WebSearchJobHandler(
    ApplicationDbContext dbContext,
    IWebSearchClient webSearchClient,
    SearchCachePolicyResolver cachePolicyResolver,
    ITopicRiskClassifier topicRiskClassifier)
    : SearchJobHandlerBase(dbContext, cachePolicyResolver, topicRiskClassifier), IJobHandler
{
    public JobType JobType => JobType.WebSearch;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<WebSearchJobPayload>(job);
        var articleId = payload.ArticleId == Guid.Empty ? job.ArticleId ?? Guid.Empty : payload.ArticleId;
        var headingId = payload.HeadingId ?? job.HeadingId;
        var (article, heading) = await GetTargetsAsync(articleId, headingId, job.UserId, cancellationToken);
        var query = ResolveQuery(payload.Query, article.Keyword, heading?.SearchQuery, heading?.Title);
        var request = new WebSearchRequest(
            query,
            payload.Region,
            string.IsNullOrWhiteSpace(payload.Language) ? "ja" : payload.Language,
            payload.MaxResults.GetValueOrDefault(10),
            payload.DomesticOnly ?? article.IsDomesticOnly,
            payload.Topic,
            string.IsNullOrWhiteSpace(payload.SearchDepth) ? "basic" : payload.SearchDepth,
            payload.StartDate,
            payload.EndDate,
            SearchCacheTtl: null,
            ContentCacheTtl: null);
        var normalized = SearchQueryNormalizer.NormalizeWeb(request);
        var now = DateTimeOffset.UtcNow;
        var cachedCount = await DbContext.SearchResults.CountAsync(
            result => result.UserId == job.UserId
                && result.ArticleId == article.Id
                && result.HeadingId == headingId
                && result.QueryHash == normalized.QueryHash
                && result.CacheExpiresAt != null
                && result.CacheExpiresAt > now,
            cancellationToken);

        if (cachedCount > 0)
        {
            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingId,
                queryHash = normalized.QueryHash,
                cached = true,
                resultCount = cachedCount
            }));
        }

        var topicRisk = ClassifyAndApplyTopicRisk(article, query, heading?.Title);
        var ttl = CachePolicyResolver.ResolveTavily(now, topicRisk);

        try
        {
            var results = await webSearchClient.SearchAsync(request, cancellationToken);
            var distinctResults = results
                .Where(result => !string.IsNullOrWhiteSpace(result.Url))
                .GroupBy(result => NormalizeUrl(result.Url), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(Math.Max(1, request.MaxResults))
                .ToArray();

            var rank = 1;
            foreach (var result in distinctResults)
            {
                DbContext.SearchResults.Add(new SearchResult
                {
                    UserId = job.UserId,
                    ArticleId = article.Id,
                    HeadingId = headingId,
                    Query = query,
                    Title = result.Title,
                    Url = result.Url,
                    Snippet = result.Snippet,
                    Rank = rank++,
                    Provider = result.Provider,
                    QueryHash = normalized.QueryHash,
                    CacheExpiresAt = ttl.CacheExpiresAt,
                    RawJsonExpiresAt = ttl.RawJsonExpiresAt,
                    ContentExpiresAt = ttl.ContentExpiresAt,
                    MetadataExpiresAt = ttl.MetadataExpiresAt,
                    FetchedAt = now
                });
            }

            await DbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingId,
                queryHash = normalized.QueryHash,
                cached = false,
                resultCount = distinctResults.Length
            }));
        }
        catch (Exception ex)
        {
            throw ToJobExecutionException(ex);
        }
    }

    private static string ResolveQuery(
        string payloadQuery,
        string articleKeyword,
        string? headingSearchQuery,
        string? headingTitle)
    {
        var query = string.IsNullOrWhiteSpace(payloadQuery) ? headingSearchQuery : payloadQuery;
        query = string.IsNullOrWhiteSpace(query) ? headingTitle : query;
        query = string.IsNullOrWhiteSpace(query) ? articleKeyword : query;

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "検索クエリが空です。");
        }

        return query.Trim();
    }

    private static string NormalizeUrl(string url)
    {
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : url.Trim();
    }
}
