using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class XFullArchiveSearchJobHandler(
    ApplicationDbContext dbContext,
    IXFullArchiveSearchClient xClient,
    SearchCachePolicyResolver cachePolicyResolver,
    ITopicRiskClassifier topicRiskClassifier)
    : SearchJobHandlerBase(dbContext, cachePolicyResolver, topicRiskClassifier), IJobHandler
{
    public JobType JobType => JobType.XFullArchiveSearch;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<XFullArchiveSearchJobPayload>(job);
        var articleId = payload.ArticleId == Guid.Empty ? job.ArticleId ?? Guid.Empty : payload.ArticleId;
        var headingId = payload.HeadingId ?? job.HeadingId;
        var (article, heading) = await GetTargetsAsync(articleId, headingId, job.UserId, cancellationToken);
        var query = string.IsNullOrWhiteSpace(payload.Query)
            ? article.Keyword
            : payload.Query.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "X検索クエリが空です。");
        }

        var request = new XFullArchiveSearchRequest(
            query,
            string.IsNullOrWhiteSpace(payload.Language) ? "ja" : payload.Language,
            payload.StartTime,
            payload.EndTime,
            payload.MaxResults.GetValueOrDefault(100),
            payload.LargeResearchMode,
            payload.ExcludeRetweets ?? true,
            payload.ExcludeReplies ?? true,
            RawCacheTtl: null,
            RehydrateBeforeDisplay: false);
        var normalized = SearchQueryNormalizer.NormalizeX(request);
        var now = DateTimeOffset.UtcNow;
        var cachedCount = await DbContext.XSearchPosts.CountAsync(
            post => post.UserId == job.UserId
                && post.ArticleId == article.Id
                && post.HeadingId == headingId
                && post.QueryHash == normalized.QueryHash
                && post.CacheExpiresAt != null
                && post.CacheExpiresAt > now,
            cancellationToken);

        if (cachedCount > 0)
        {
            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingId,
                queryHash = normalized.QueryHash,
                cached = true,
                postCount = cachedCount
            }));
        }

        var topicRisk = ClassifyAndApplyTopicRisk(article, query, heading?.Title);
        var ttl = CachePolicyResolver.ResolveX(now, topicRisk);

        try
        {
            var results = await xClient.SearchAsync(request, cancellationToken);
            var distinctResults = results
                .Where(result => !string.IsNullOrWhiteSpace(result.PostId))
                .GroupBy(result => result.PostId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var postIds = distinctResults.Select(result => result.PostId).ToArray();
            var existingPostIds = await DbContext.XSearchPosts
                .Where(post => postIds.Contains(post.PostId))
                .Select(post => post.PostId)
                .ToListAsync(cancellationToken);
            var existingPostIdSet = existingPostIds.ToHashSet(StringComparer.Ordinal);
            var addedCount = 0;

            foreach (var result in distinctResults)
            {
                if (existingPostIdSet.Contains(result.PostId))
                {
                    continue;
                }

                DbContext.XSearchPosts.Add(new XSearchPost
                {
                    UserId = job.UserId,
                    ArticleId = article.Id,
                    HeadingId = headingId,
                    Query = query,
                    QueryHash = normalized.QueryHash,
                    PostId = result.PostId,
                    AuthorId = result.AuthorId,
                    Text = result.Text,
                    Url = result.Url,
                    Language = result.Language,
                    PostedAt = result.PostedAt,
                    FetchedAt = now,
                    CacheExpiresAt = ttl.CacheExpiresAt,
                    ContentExpiresAt = ttl.ContentExpiresAt,
                    MetadataExpiresAt = ttl.MetadataExpiresAt
                });
                addedCount++;
            }

            await DbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingId,
                queryHash = normalized.QueryHash,
                cached = false,
                postCount = distinctResults.Length,
                addedCount
            }));
        }
        catch (Exception ex)
        {
            throw ToJobExecutionException(ex);
        }
    }
}
