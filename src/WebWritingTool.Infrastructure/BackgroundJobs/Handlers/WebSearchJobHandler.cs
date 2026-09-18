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
    ITopicRiskClassifier topicRiskClassifier,
    SearchDataMode dataMode)
    : SearchJobHandlerBase(dbContext, cachePolicyResolver, topicRiskClassifier), IJobHandler
{
    public JobType JobType => JobType.WebSearch;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<WebSearchJobPayload>(job);
        if (payload.IsDummy.HasValue && payload.IsDummy != dataMode.IsDummy)
        {
            throw new JobExecutionException(JobErrorCodes.Conflict, "検索モードが変更されました。検索を再登録してください。");
        }
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
        if (query.Length > 300 || request.MaxResults is < 1 or > 20)
        {
            throw new JobExecutionException(JobErrorCodes.ValidationError, "検索条件が上限を超えています。");
        }
        var normalized = SearchQueryNormalizer.NormalizeWeb(request);
        var now = DateTimeOffset.UtcNow;
        var cachedResults = DbContext.SearchResults.Where(
            result => result.UserId == job.UserId
                && result.ArticleId == article.Id
                && result.HeadingId == headingId
                && result.IsDummy == dataMode.IsDummy
                && result.QueryHash == normalized.QueryHash
                && result.CacheExpiresAt != null
                && result.ContentExpiresAt > now
                && result.CacheExpiresAt > now);
        var cachedCount = await cachedResults.CountAsync(cancellationToken);

        if (cachedCount > 0)
        {
            if (payload.IsManual)
            {
                await cachedResults.Where(result => !result.IsManual)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(result => result.IsManual, true), cancellationToken);
            }

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
                    IsDummy = dataMode.IsDummy,
                    IsManual = payload.IsManual,
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
