using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.BackgroundJobs.Handlers;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Search;

public sealed class ArticleResearchService(
    ApplicationDbContext dbContext,
    IJobCommandService jobService,
    WebSearchJobHandler webSearchHandler,
    IXPostRehydrationService rehydrationService,
    SearchDataMode dataMode) : IArticleResearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int ResultLimit = 20;

    public async Task<JobServiceResult<JobAcceptedResponse>> EnqueueAsync(
        ArticleActor actor, Guid articleId, string source, ArticleResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(actor, articleId, request.HeadingId, cancellationToken);
        if (article is null)
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.NotFound);
        }

        var query = string.IsNullOrWhiteSpace(request.Query) ? article.Keyword : request.Query.Trim();
        var maxResults = request.MaxResults ?? 10;
        if (source is not ("web" or "x") || query.Length is 0 or > 300
            || maxResults < 1 || maxResults > (source == "web" ? 20 : 100))
        {
            return JobServiceResult<JobAcceptedResponse>.Failure(JobServiceError.ValidationFailed,
                [new JobValidationError("query", "検索語は300文字以内、取得件数はWeb検索1〜20件、X検索1〜100件で指定してください。")]);
        }

        object payload = source == "web"
            ? new WebSearchJobPayload(articleId, request.HeadingId, query, "Japan", "ja", maxResults,
                article.IsDomesticOnly, null, "basic", null, null, dataMode.IsDummy, IsManual: true)
            : new XFullArchiveSearchJobPayload(articleId, request.HeadingId, query, "ja", null, null,
                maxResults, false, true, true, dataMode.IsDummy);
        return await jobService.EnqueueAsync(new EnqueueJobCommand(
            new JobActor(actor.UserId, actor.IsAdmin), source == "web" ? JobType.WebSearch : JobType.XFullArchiveSearch,
            articleId, request.HeadingId, JsonSerializer.Serialize(payload, JsonOptions), 0), cancellationToken);
    }

    public async Task<ArticleResearchResponse?> GetAsync(
        ArticleActor actor, Guid articleId, Guid? headingId = null, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(actor, articleId, headingId, cancellationToken);
        return article is null ? null : await ReadResultsAsync(article, headingId, cancellationToken);
    }

    public async Task<IReadOnlyList<AiReferenceSource>> GetReferencesAsync(
        string userId, Guid articleId, Guid? headingId, bool searchWeb,
        string? query = null, bool? domesticOnly = null, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(new ArticleActor(userId, false), articleId, headingId, cancellationToken)
            ?? throw new JobExecutionException(JobErrorCodes.NotFound, "記事または見出しが見つかりません。");
        if (searchWeb)
        {
            var payload = new WebSearchJobPayload(articleId, headingId, query ?? article.Keyword,
                "Japan", "ja", 10, domesticOnly ?? article.IsDomesticOnly, null, "basic", null, null, dataMode.IsDummy,
                IsManual: false);
            // 生成ジョブの中で検索を完了させてからAIへ渡す。UIスレッドでは検索しない。
            try
            {
                await webSearchHandler.HandleAsync(new LeasedJob(Guid.Empty, userId, articleId, headingId,
                    JobType.WebSearch, JsonSerializer.Serialize(payload, JsonOptions), 1, 1), cancellationToken);
            }
            catch (JobExecutionException exception)
            {
                throw new ExternalIntegrationException(exception.ErrorCode, exception.UserMessage, exception, exception.RetryAfter);
            }
        }

        var results = await ReadResultsAsync(article, headingId, cancellationToken);
        return ToReferences(results);
    }

    public async Task<IReadOnlyList<AiReferenceSource>> GetSelectedReferencesAsync(
        string userId, Guid articleId, ResearchSourceSelection sources, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(new ArticleActor(userId, false), articleId, null, cancellationToken)
            ?? throw new JobExecutionException(JobErrorCodes.NotFound, "記事が見つかりません。");
        return ToReferences(await ReadResultsAsync(article, null, cancellationToken, sources));
    }

    private IReadOnlyList<AiReferenceSource> ToReferences(ArticleResearchResponse results)
    {
        return results.WebResults.Where(result => !string.IsNullOrWhiteSpace(result.Snippet))
            .Take(10).Select((result, index) => new AiReferenceSource($"web-{index + 1}",
                result.Title, result.Url, $"取得日時: {result.FetchedAt:O}\n{Excerpt(result.Snippet!)}"))
            .Concat(results.XPosts.Take(10).Select((post, index) => new AiReferenceSource($"x-{index + 1}",
                dataMode.IsDummy ? "サンプル投稿（実在しません）" : "X投稿（個人の発言・未検証）",
                post.Url, $"投稿日時: {post.PostedAt:O}\n取得日時: {post.FetchedAt:O}\n{Excerpt(post.Text)}")))
            .ToArray();
    }

    private async Task<Article?> GetArticleAsync(
        ArticleActor actor, Guid articleId, Guid? headingId, CancellationToken cancellationToken)
    {
        var article = await dbContext.Articles.FirstOrDefaultAsync(
            article => article.Id == articleId && (article.UserId == actor.UserId || actor.IsAdmin), cancellationToken);
        if (article is null || headingId.HasValue && !await dbContext.ArticleHeadings.AnyAsync(
                heading => heading.Id == headingId && heading.ArticleId == articleId, cancellationToken))
        {
            return null;
        }
        return article;
    }

    private async Task<ArticleResearchResponse> ReadResultsAsync(
        Article article, Guid? headingId, CancellationToken cancellationToken, ResearchSourceSelection? sources = null)
    {
        var now = DateTimeOffset.UtcNow;
        var web = await dbContext.SearchResults.AsNoTracking()
            .Where(result => (sources == null || sources.UseWeb) && result.UserId == article.UserId && result.ArticleId == article.Id
                && (result.HeadingId == null || result.HeadingId == headingId) && result.IsDummy == dataMode.IsDummy
                && result.CacheExpiresAt > now && result.ContentExpiresAt > now)
            .OrderByDescending(result => result.IsManual)
            .ThenByDescending(result => result.FetchedAt).ThenBy(result => result.Rank).ThenBy(result => result.Id)
            .Take(100).ToListAsync(cancellationToken);
        var xQuery = dbContext.XSearchPosts.AsNoTracking()
            .Where(post => (sources == null || sources.UseX) && post.UserId == article.UserId && post.ArticleId == article.Id
                && (post.HeadingId == null || post.HeadingId == headingId) && post.IsDummy == dataMode.IsDummy
                && post.CacheExpiresAt > now && post.ContentExpiresAt > now && post.Text != null);
        var posts = await xQuery.OrderByDescending(post => post.FetchedAt).Take(100).ToListAsync(cancellationToken);
        string? warning = null;
        if (!dataMode.IsDummy && posts.Count > 0)
        {
            try
            {
                var mode = article.TopicRisk == "compliance_strict" ? TopicRiskMode.ComplianceStrict
                    : article.StrictMode || article.TopicRisk == "strict" ? TopicRiskMode.Strict : TopicRiskMode.Normal;
                await rehydrationService.RehydrateCachedPostsAsync(article.UserId,
                    posts.Select(post => post.PostId).Distinct().ToArray(), mode, cancellationToken);
                posts = await xQuery.OrderByDescending(post => post.FetchedAt).Take(100).ToListAsync(cancellationToken);
            }
            catch (ExternalIntegrationException)
            {
                if (sources?.UseX == true) throw;
                posts.Clear();
                warning = "X投稿を再取得できなかったため、表示・生成への利用を保留しました。時間をおいて再度取得してください。";
            }
        }

        return new ArticleResearchResponse(dataMode.IsDummy,
            web.Where(result => SafeUrl(result.Url) is not null).DistinctBy(result => result.Url)
                .Take(ResultLimit).Select(result => new ResearchWebResult(result.Title ?? "参考情報", result.Url,
                    result.Snippet, result.FetchedAt, result.IsManual)).ToArray(),
            posts.DistinctBy(post => post.PostId).Take(ResultLimit).Select(post => new ResearchXPost(
                post.PostId, post.AuthorId, post.Text!, SafeUrl(post.Url), post.PostedAt, post.FetchedAt)).ToArray(), warning);
    }

    private static string Excerpt(string text) => text.Length <= 500 ? text : text[..500] + "…";

    private static string? SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
