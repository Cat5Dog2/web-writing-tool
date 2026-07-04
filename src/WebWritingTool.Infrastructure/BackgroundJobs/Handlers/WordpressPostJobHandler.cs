using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class WordpressPostJobHandler(
    ApplicationDbContext dbContext,
    IWordpressClient wordpressClient,
    ISecretProtector secretProtector,
    IContentRenderingService contentRenderingService,
    IXPostRehydrationService xPostRehydrationService)
    : IJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public JobType JobType => JobType.WordpressPost;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload(job);
        var articleId = payload.ArticleId == Guid.Empty
            ? job.ArticleId ?? Guid.Empty
            : payload.ArticleId;

        var article = await dbContext.Articles.FirstOrDefaultAsync(
            item => item.Id == articleId && item.UserId == job.UserId,
            cancellationToken);

        if (article is null)
        {
            throw new JobExecutionException(JobErrorCodes.NotFound, "記事が見つかりません。");
        }

        var site = await dbContext.WordpressSites.FirstOrDefaultAsync(
            item => item.Id == payload.WordpressSiteId && item.UserId == job.UserId,
            cancellationToken);

        if (site is null)
        {
            throw new JobExecutionException(JobErrorCodes.NotFound, "WordPressサイトが見つかりません。");
        }

        var requestedStatus = NormalizeStatus(payload.Status);
        var history = await dbContext.WordpressPosts.FirstOrDefaultAsync(
            item => item.JobId == job.Id,
            cancellationToken);

        if (history is null)
        {
            history = new WordpressPost
            {
                UserId = article.UserId,
                ArticleId = article.Id,
                WordpressSiteId = site.Id,
                JobId = job.Id,
                Title = payload.Title,
                CategoryId = payload.CategoryId ?? site.DefaultCategoryId,
                RequestedStatus = requestedStatus,
                Status = WordpressPostStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WordpressPosts.Add(history);
        }

        history.Status = WordpressPostStatus.Queued;
        history.ErrorCode = null;
        history.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (IsPublishBlocked(article, requestedStatus))
        {
            await MarkHistoryFailedAsync(
                history,
                JobErrorCodes.ValidationError,
                "人間確認前の記事はWordPressへ公開投稿できません。");
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "人間確認前の記事はWordPressへ公開投稿できません。");
        }

        if (!article.Status.CanPostToWordpress())
        {
            await MarkHistoryFailedAsync(
                history,
                JobErrorCodes.ValidationError,
                "投稿可能な記事状態ではありません。");
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "投稿可能な記事状態ではありません。");
        }

        var htmlBody = contentRenderingService.SanitizeHtml(
            string.IsNullOrWhiteSpace(payload.HtmlBody)
                ? article.HtmlBody ?? string.Empty
                : payload.HtmlBody);

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            await MarkHistoryFailedAsync(
                history,
                JobErrorCodes.ValidationError,
                "投稿HTMLがありません。");
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "投稿HTMLがありません。");
        }

        if (string.Equals(requestedStatus, WordpressPostStatuses.Publish, StringComparison.Ordinal))
        {
            await EnsureXRehydrationForPublishAsync(article, history, cancellationToken);
        }

        try
        {
            var result = await wordpressClient.CreatePostAsync(
                new WordpressPostRequest(
                    new WordpressSiteConnection(
                        site.BaseUrl,
                        site.LoginId,
                        secretProtector.Unprotect(site.EncryptedApplicationPassword)),
                    payload.Title,
                    htmlBody,
                    payload.CategoryId ?? site.DefaultCategoryId,
                    requestedStatus),
                cancellationToken);

            if (!result.Success)
            {
                history.Status = WordpressPostStatus.Failed;
                history.ErrorCode = ToJobErrorCode(result.ErrorCode);
                history.ErrorMessage = TruncateErrorMessage(result.ErrorMessage);
                await dbContext.SaveChangesAsync(CancellationToken.None);

                throw new JobExecutionException(
                    history.ErrorCode,
                    history.ErrorMessage ?? "WordPress投稿に失敗しました。");
            }

            var postedAt = DateTimeOffset.UtcNow;
            history.Status = WordpressPostStatus.Succeeded;
            history.PostId = result.PostId;
            history.PostUrl = result.PostUrl;
            history.PostedAt = postedAt;
            history.ErrorCode = null;
            history.ErrorMessage = null;
            article.Status = ArticleStatus.Posted;
            article.PostedAt = postedAt;

            await dbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(JsonSerializer.Serialize(new
            {
                articleId = article.Id,
                wordpressSiteId = site.Id,
                postId = result.PostId,
                postUrl = result.PostUrl
            }, JsonOptions));
        }
        catch (ExternalIntegrationException ex)
        {
            history.Status = WordpressPostStatus.Failed;
            history.ErrorCode = ToJobErrorCode(ex.ErrorCode);
            history.ErrorMessage = TruncateErrorMessage(ex.UserMessage);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            throw new JobExecutionException(
                history.ErrorCode,
                history.ErrorMessage ?? "WordPress投稿に失敗しました。",
                ex,
                ex.RetryAfter);
        }
    }

    private async Task EnsureXRehydrationForPublishAsync(
        Article article,
        WordpressPost history,
        CancellationToken cancellationToken)
    {
        var postIds = await dbContext.XSearchPosts
            .Where(post => post.ArticleId == article.Id && post.UserId == article.UserId)
            .Select(post => post.PostId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (postIds.Count == 0)
        {
            return;
        }

        var topicRiskMode = TopicRiskModeExtensions.ToTopicRiskMode(article.TopicRisk);
        var result = await xPostRehydrationService.RehydrateCachedPostsAsync(
            article.UserId,
            postIds,
            topicRiskMode,
            cancellationToken);

        if (result.RehydrationRequired && result.MissingCount > 0)
        {
            const string message = "X投稿の再取得に失敗しました。引用内容を確認してください。";
            await MarkHistoryFailedAsync(history, JobErrorCodes.XRehydrationFailed, message);
            throw new JobExecutionException(JobErrorCodes.XRehydrationFailed, message);
        }
    }

    private static WordpressPostPayload ReadPayload(LeasedJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<WordpressPostPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "ジョブPayloadが不正です。",
                ex);
        }
    }

    private static bool IsPublishBlocked(Article article, string requestedStatus)
    {
        return string.Equals(requestedStatus, WordpressPostStatuses.Publish, StringComparison.Ordinal)
            && article.HumanReviewRequired
            && article.HumanReviewedAt is null;
    }

    private static string NormalizeStatus(string? status)
    {
        return string.Equals(status, WordpressPostStatuses.Publish, StringComparison.OrdinalIgnoreCase)
            ? WordpressPostStatuses.Publish
            : WordpressPostStatuses.Draft;
    }

    private static string ToJobErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            ExternalIntegrationErrorCodes.ValidationError => JobErrorCodes.ValidationError,
            ExternalIntegrationErrorCodes.UnauthorizedExternalApi => JobErrorCodes.UnauthorizedExternalApi,
            ExternalIntegrationErrorCodes.ForbiddenExternalApi => JobErrorCodes.ForbiddenExternalApi,
            ExternalIntegrationErrorCodes.RateLimited => JobErrorCodes.RateLimited,
            ExternalIntegrationErrorCodes.Timeout => JobErrorCodes.Timeout,
            ExternalIntegrationErrorCodes.ExternalServerError => JobErrorCodes.ExternalServerError,
            ExternalIntegrationErrorCodes.ExternalBadResponse => JobErrorCodes.ExternalBadResponse,
            ExternalIntegrationErrorCodes.NetworkError => JobErrorCodes.NetworkError,
            _ => JobErrorCodes.UnknownError
        };
    }

    private static string? TruncateErrorMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
    }

    private async Task MarkHistoryFailedAsync(
        WordpressPost history,
        string errorCode,
        string message)
    {
        history.Status = WordpressPostStatus.Failed;
        history.ErrorCode = errorCode;
        history.ErrorMessage = TruncateErrorMessage(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
