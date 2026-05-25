using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Wordpress;

public sealed class WordpressPostService(
    ApplicationDbContext dbContext,
    IContentRenderingService contentRenderingService,
    JobRetryPolicy retryPolicy)
    : IWordpressPostCommandService, IWordpressPostQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WordpressPostPreviewResponse?> GetPreviewAsync(
        WordpressActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return null;
        }

        var sites = await dbContext.WordpressSites
            .AsNoTracking()
            .Where(site => site.UserId == article.UserId)
            .OrderBy(site => site.SiteName)
            .Select(site => ToSiteResponse(site))
            .ToListAsync(cancellationToken);

        return new WordpressPostPreviewResponse(
            article.Id,
            article.Title ?? article.Keyword,
            article.HtmlBody ?? string.Empty,
            article.HumanReviewRequired,
            article.HumanReviewedAt,
            sites);
    }

    public async Task<WordpressServiceResult<JobAcceptedResponse>> CreatePostJobAsync(
        CreateWordpressPostJobCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(WordpressServiceError.NotFound);
        }

        var site = await dbContext.WordpressSites
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == command.WordpressSiteId && item.UserId == article.UserId,
                cancellationToken);

        if (site is null)
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(WordpressServiceError.NotFound);
        }

        var validationErrors = ValidatePostInput(command.Title, command.HtmlBody);
        if (validationErrors.Count > 0)
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(
                WordpressServiceError.ValidationFailed,
                validationErrors);
        }

        if (!article.Status.CanPostToWordpress())
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(WordpressServiceError.NotPostable);
        }

        var requestedStatus = NormalizeStatus(command.Status);
        if (IsPublishBlocked(article, requestedStatus))
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(WordpressServiceError.HumanReviewRequired);
        }

        if (await HasActiveWordpressPostJobAsync(article.Id, cancellationToken))
        {
            return WordpressServiceResult<JobAcceptedResponse>.Failure(WordpressServiceError.Conflict);
        }

        var sanitizedHtml = contentRenderingService.SanitizeHtml(command.HtmlBody);
        var categoryId = command.CategoryId ?? site.DefaultCategoryId;
        var job = await EnqueuePostJobAsync(
            article,
            site.Id,
            command.Title.Trim(),
            sanitizedHtml,
            categoryId,
            requestedStatus,
            WordpressPostSources.Manual,
            cancellationToken);

        return WordpressServiceResult<JobAcceptedResponse>.Success(ToAcceptedResponse(job));
    }

    public async Task QueueAutoPostIfReadyAsync(
        string userId,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == articleId && item.UserId == userId, cancellationToken);

        if (article is null
            || !article.AutoPostToWordpress
            || article.AutoPostQueuedAt.HasValue
            || !article.Status.CanPostToWordpress()
            || !article.AutoPostWordpressSiteId.HasValue
            || string.IsNullOrWhiteSpace(article.HtmlBody))
        {
            return;
        }

        var site = await dbContext.WordpressSites
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == article.AutoPostWordpressSiteId.Value && item.UserId == userId,
                cancellationToken);

        if (site is null
            || await HasActiveWordpressPostJobAsync(article.Id, cancellationToken)
            || await HasSucceededPostAsync(article.Id, cancellationToken))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(article.Title) ? article.Keyword : article.Title;
        var categoryId = article.AutoPostWordpressCategoryId ?? site.DefaultCategoryId;
        await EnqueuePostJobAsync(
            article,
            site.Id,
            title,
            contentRenderingService.SanitizeHtml(article.HtmlBody),
            categoryId,
            WordpressPostStatuses.Draft,
            WordpressPostSources.BulkAutoPost,
            cancellationToken);

        article.AutoPostQueuedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WordpressPostHistoryResponse?> GetHistoryAsync(
        WordpressActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return null;
        }

        var items = await dbContext.WordpressPosts
            .AsNoTracking()
            .Where(post => post.ArticleId == articleId)
            .Join(
                dbContext.WordpressSites.IgnoreQueryFilters().AsNoTracking(),
                post => post.WordpressSiteId,
                site => site.Id,
                (post, site) => new WordpressPostHistoryItemResponse(
                    post.Id,
                    post.WordpressSiteId,
                    site.SiteName,
                    post.PostId,
                    post.PostUrl,
                    post.CategoryId,
                    post.RequestedStatus,
                    post.Status.ToString(),
                    post.ErrorMessage,
                    post.CreatedAt,
                    post.PostedAt))
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return new WordpressPostHistoryResponse(items);
    }

    private async Task<ArticleGenerationJob> EnqueuePostJobAsync(
        Article article,
        Guid wordpressSiteId,
        string title,
        string htmlBody,
        int? categoryId,
        string requestedStatus,
        string source,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var payload = new WordpressPostPayload(
            article.Id,
            wordpressSiteId,
            title.Trim(),
            htmlBody,
            categoryId,
            requestedStatus,
            source);

        var job = new ArticleGenerationJob
        {
            UserId = article.UserId,
            ArticleId = article.Id,
            JobType = JobType.WordpressPost,
            Status = JobStatus.Queued,
            Priority = 0,
            Progress = 0,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            AttemptCount = 0,
            MaxAttempts = retryPolicy.GetMaxAttempts(JobType.WordpressPost),
            QueuedAt = now
        };

        dbContext.ArticleGenerationJobs.Add(job);
        dbContext.WordpressPosts.Add(new WordpressPost
        {
            UserId = article.UserId,
            ArticleId = article.Id,
            WordpressSiteId = wordpressSiteId,
            JobId = job.Id,
            Title = title.Trim(),
            CategoryId = categoryId,
            RequestedStatus = requestedStatus,
            Status = WordpressPostStatus.Queued,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    private async Task<bool> HasActiveWordpressPostJobAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.ArticleId == articleId
                && job.JobType == JobType.WordpressPost
                && (job.Status == JobStatus.Queued || job.Status == JobStatus.Running),
            cancellationToken);
    }

    private async Task<bool> HasSucceededPostAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        return await dbContext.WordpressPosts.AnyAsync(
            post => post.ArticleId == articleId && post.Status == WordpressPostStatus.Succeeded,
            cancellationToken);
    }

    private static List<WordpressValidationError> ValidatePostInput(
        string title,
        string htmlBody)
    {
        var errors = new List<WordpressValidationError>();
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 250)
        {
            errors.Add(new WordpressValidationError(nameof(title), "タイトルは1から250文字で入力してください。"));
        }

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            errors.Add(new WordpressValidationError(nameof(htmlBody), "投稿HTMLを入力してください。"));
        }

        return errors;
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

    private static JobAcceptedResponse ToAcceptedResponse(ArticleGenerationJob job)
    {
        return new JobAcceptedResponse(
            job.Id,
            job.ArticleId,
            job.HeadingId,
            job.JobType.ToString(),
            job.Status.ToString(),
            $"/api/jobs/{job.Id}");
    }

    private static WordpressSiteResponse ToSiteResponse(WordpressSite site)
    {
        return new WordpressSiteResponse(
            site.Id,
            site.SiteName,
            site.BaseUrl,
            site.LoginId,
            site.DefaultCategoryId,
            site.DefaultCategoryName,
            site.SiteAdminProfile,
            site.WritingCharacter,
            site.ReaderPersona,
            site.LastConnectedAt,
            site.CreatedAt,
            site.UpdatedAt,
            Convert.ToBase64String(site.RowVersion));
    }

    private static bool CanAccess(WordpressActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }
}
