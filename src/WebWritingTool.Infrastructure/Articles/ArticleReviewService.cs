using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Domain.Audit;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

public sealed class ArticleReviewService(ApplicationDbContext dbContext) : IArticleReviewService
{
    private const int ReviewCommentMaxLength = 1000;

    public async Task<ArticleServiceResult<HumanReviewResponse>> CompleteHumanReviewAsync(
        CompleteHumanReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult<HumanReviewResponse>.Failure(ArticleServiceError.NotFound);
        }

        if (!string.IsNullOrWhiteSpace(command.ReviewComment)
            && command.ReviewComment.Trim().Length > ReviewCommentMaxLength)
        {
            return ArticleServiceResult<HumanReviewResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                [new ArticleValidationError(nameof(command.ReviewComment), "確認メモは1000文字以内で入力してください。")]);
        }

        if (!string.IsNullOrWhiteSpace(command.RowVersion))
        {
            if (!TryDecodeRowVersion(command.RowVersion, out var rowVersion))
            {
                return ArticleServiceResult<HumanReviewResponse>.Failure(
                    ArticleServiceError.ValidationFailed,
                    [new ArticleValidationError(nameof(command.RowVersion), "RowVersionが不正です。")]);
            }

            dbContext.Entry(article).Property(item => item.RowVersion).OriginalValue = rowVersion;
        }

        article.HumanReviewedAt = DateTimeOffset.UtcNow;
        article.HumanReviewedByUserId = command.Actor.UserId;

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = command.Actor.UserId,
            Action = "HumanReviewCompleted",
            EntityType = "Article",
            EntityId = article.Id.ToString(),
            Summary = BuildSummary(article.TopicRisk, command.ReviewComment)
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArticleServiceResult<HumanReviewResponse>.Failure(ArticleServiceError.ConcurrencyConflict);
        }

        return ArticleServiceResult<HumanReviewResponse>.Success(
            new HumanReviewResponse(
                article.Id,
                article.HumanReviewRequired,
                article.HumanReviewedAt,
                article.HumanReviewedByUserId));
    }

    private static string BuildSummary(string? topicRisk, string? reviewComment)
    {
        var summary = $"Human review completed. TopicRisk={topicRisk ?? "normal"}.";
        var comment = reviewComment?.Trim();
        if (string.IsNullOrEmpty(comment))
        {
            return summary;
        }

        var truncated = comment.Length > ReviewCommentMaxLength ? comment[..ReviewCommentMaxLength] : comment;
        return $"{summary} {truncated}";
    }

    private static bool CanAccess(ArticleActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }

    private static bool TryDecodeRowVersion(string rowVersion, out byte[] value)
    {
        try
        {
            value = Convert.FromBase64String(rowVersion);
            return value.Length > 0;
        }
        catch (FormatException)
        {
            value = [];
            return false;
        }
    }
}
