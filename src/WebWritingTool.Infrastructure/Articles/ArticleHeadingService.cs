using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

public sealed class ArticleHeadingService(ApplicationDbContext dbContext) : IArticleHeadingService
{
    private const int DisplayOrderStep = 10;

    public async Task<ArticleServiceResult<ArticleHeadingListResponse>> GetHeadingsAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return ArticleServiceResult<ArticleHeadingListResponse>.Failure(ArticleServiceError.NotFound);
        }

        var headings = await dbContext.ArticleHeadings
            .AsNoTracking()
            .Where(heading => heading.ArticleId == articleId)
            .OrderBy(heading => heading.DisplayOrder)
            .ToListAsync(cancellationToken);

        return ArticleServiceResult<ArticleHeadingListResponse>.Success(
            new ArticleHeadingListResponse(headings.Select(ToResponse).ToArray()));
    }

    public async Task<ArticleServiceResult<ArticleHeadingResponse>> CreateHeadingAsync(
        CreateArticleHeadingCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(ArticleServiceError.NotFound);
        }

        var headings = await LoadHeadingsAsync(command.ArticleId, cancellationToken);
        var errors = ValidateCreate(command, headings);
        if (errors.Count > 0)
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                errors);
        }

        var heading = new ArticleHeading
        {
            ArticleId = article.Id,
            ParentId = command.Level == 3 ? command.ParentId : null,
            Level = command.Level,
            Title = command.Title.Trim(),
            TargetLength = command.TargetLength,
            UseWebSearch = command.UseWebSearch,
            Status = HeadingStatus.Pending
        };

        var orderedHeadings = headings.OrderBy(item => item.DisplayOrder).ToList();
        var insertIndex = GetInsertIndex(command, orderedHeadings);
        orderedHeadings.Insert(insertIndex, heading);
        NormalizeDisplayOrders(orderedHeadings);

        dbContext.ArticleHeadings.Add(heading);
        UpdateArticleBody(article, orderedHeadings);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult<ArticleHeadingResponse>.Success(ToResponse(heading));
    }

    public async Task<ArticleServiceResult<ArticleHeadingResponse>> UpdateHeadingAsync(
        UpdateArticleHeadingCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(ArticleServiceError.NotFound);
        }

        var heading = await dbContext.ArticleHeadings
            .FirstOrDefaultAsync(
                item => item.ArticleId == command.ArticleId && item.Id == command.HeadingId,
                cancellationToken);

        if (heading is null)
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(ArticleServiceError.NotFound);
        }

        var errors = ValidateUpdate(command);
        if (errors.Count > 0)
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                errors);
        }

        if (!string.IsNullOrWhiteSpace(command.RowVersion))
        {
            if (!TryDecodeRowVersion(command.RowVersion, out var rowVersion))
            {
                return ArticleServiceResult<ArticleHeadingResponse>.Failure(
                    ArticleServiceError.ValidationFailed,
                    [new ArticleValidationError(nameof(command.RowVersion), "RowVersionが不正です。")]);
            }

            dbContext.Entry(heading).Property(item => item.RowVersion).OriginalValue = rowVersion;
        }

        heading.Title = command.Title.Trim();
        heading.Body = NormalizeOptionalText(command.Body);
        heading.ActualLength = heading.Body?.Length;
        heading.TargetLength = command.TargetLength;
        heading.UseWebSearch = command.UseWebSearch;
        heading.Status = string.IsNullOrWhiteSpace(heading.Body)
            ? HeadingStatus.Pending
            : HeadingStatus.Generated;

        var headings = await LoadHeadingsAsync(command.ArticleId, cancellationToken);
        UpdateArticleBody(article, headings);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArticleServiceResult<ArticleHeadingResponse>.Failure(ArticleServiceError.ConcurrencyConflict);
        }

        return ArticleServiceResult<ArticleHeadingResponse>.Success(ToResponse(heading));
    }

    public async Task<ArticleServiceResult> DeleteHeadingAsync(
        ArticleActor actor,
        Guid articleId,
        Guid headingId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        }

        var headings = await LoadHeadingsAsync(articleId, cancellationToken);
        var heading = headings.FirstOrDefault(item => item.Id == headingId);
        if (heading is null)
        {
            return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        }

        var deleteTargets = heading.Level == 2
            ? headings.Where(item => item.Id == heading.Id || item.ParentId == heading.Id).ToList()
            : [heading];

        if (HasBlockingStatus(deleteTargets)
            || await HasBlockingJobAsync(articleId, deleteTargets.Select(item => item.Id).ToArray(), cancellationToken))
        {
            return ArticleServiceResult.Failure(ArticleServiceError.ConflictGeneratingHeading);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var target in deleteTargets)
        {
            target.DeletedAt = now;
        }

        var remaining = headings
            .Except(deleteTargets)
            .OrderBy(item => item.DisplayOrder)
            .ToList();
        NormalizeDisplayOrders(remaining);
        UpdateArticleBody(article, remaining);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult.Success;
    }

    public async Task<ArticleServiceResult> UpdateHeadingOrderAsync(
        UpdateArticleHeadingOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        }

        var headings = await LoadHeadingsAsync(command.ArticleId, cancellationToken);
        var errors = ValidateOrder(command.Items, headings);
        if (errors.Count > 0)
        {
            return ArticleServiceResult.Failure(ArticleServiceError.ValidationFailed, errors);
        }

        if (HasBlockingStatus(headings)
            || await HasBlockingJobAsync(command.ArticleId, headings.Select(item => item.Id).ToArray(), cancellationToken))
        {
            return ArticleServiceResult.Failure(ArticleServiceError.ConflictGeneratingHeading);
        }

        var headingById = headings.ToDictionary(heading => heading.Id);
        var orderedItems = command.Items.OrderBy(item => item.DisplayOrder).ToArray();
        for (var index = 0; index < orderedItems.Length; index++)
        {
            var orderItem = orderedItems[index];
            var heading = headingById[orderItem.HeadingId];
            heading.ParentId = heading.Level == 3 ? orderItem.ParentId : null;
            heading.DisplayOrder = (index + 1) * DisplayOrderStep;
        }

        UpdateArticleBody(article, headings.OrderBy(item => item.DisplayOrder).ToList());
        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult.Success;
    }

    private async Task<List<ArticleHeading>> LoadHeadingsAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ArticleHeadings
            .Where(heading => heading.ArticleId == articleId)
            .OrderBy(heading => heading.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> HasBlockingJobAsync(
        Guid articleId,
        IReadOnlyCollection<Guid> headingIds,
        CancellationToken cancellationToken)
    {
        return await dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.ArticleId == articleId
                && (job.Status == JobStatus.Queued || job.Status == JobStatus.Running)
                && (job.HeadingId == null || headingIds.Contains(job.HeadingId.Value))
                && (job.JobType == JobType.OutlineGeneration
                    || job.JobType == JobType.BodyGeneration
                    || job.JobType == JobType.Rewrite),
            cancellationToken);
    }

    private static List<ArticleValidationError> ValidateCreate(
        CreateArticleHeadingCommand command,
        IReadOnlyList<ArticleHeading> headings)
    {
        var errors = new List<ArticleValidationError>();
        ValidateTitle(command.Title, errors);
        ValidateTargetLength(command.TargetLength, errors);

        if (command.Level is not 2 and not 3)
        {
            errors.Add(new ArticleValidationError(nameof(command.Level), "見出しレベルは2または3を指定してください。"));
            return errors;
        }

        if (command.Level == 2 && command.ParentId.HasValue)
        {
            errors.Add(new ArticleValidationError(nameof(command.ParentId), "H2に親見出しは指定できません。"));
        }

        if (command.Level == 3)
        {
            var parent = headings.FirstOrDefault(item => item.Id == command.ParentId);
            if (parent is null || parent.Level != 2)
            {
                errors.Add(new ArticleValidationError(nameof(command.ParentId), "H3の親H2を指定してください。"));
            }
        }

        if (command.InsertAfterHeadingId.HasValue)
        {
            var insertAfter = headings.FirstOrDefault(item => item.Id == command.InsertAfterHeadingId.Value);
            if (insertAfter is null)
            {
                errors.Add(new ArticleValidationError(nameof(command.InsertAfterHeadingId), "挿入位置の見出しが見つかりません。"));
            }
            else if (command.Level == 3
                && insertAfter.Level == 2
                && insertAfter.Id != command.ParentId)
            {
                errors.Add(new ArticleValidationError(nameof(command.InsertAfterHeadingId), "H3は親H2配下へ挿入してください。"));
            }
            else if (command.Level == 3
                && insertAfter.Level == 3
                && insertAfter.ParentId != command.ParentId)
            {
                errors.Add(new ArticleValidationError(nameof(command.InsertAfterHeadingId), "H3は同じ親H2配下へ挿入してください。"));
            }
        }

        return errors;
    }

    private static List<ArticleValidationError> ValidateUpdate(UpdateArticleHeadingCommand command)
    {
        var errors = new List<ArticleValidationError>();
        ValidateTitle(command.Title, errors);
        ValidateTargetLength(command.TargetLength, errors);
        return errors;
    }

    private static List<ArticleValidationError> ValidateOrder(
        IReadOnlyList<ArticleHeadingOrderItem> items,
        IReadOnlyList<ArticleHeading> headings)
    {
        var errors = new List<ArticleValidationError>();
        var activeIds = headings.Select(heading => heading.Id).ToHashSet();
        var requestIds = items.Select(item => item.HeadingId).ToArray();

        if (requestIds.Length != activeIds.Count || requestIds.Distinct().Count() != requestIds.Length)
        {
            errors.Add(new ArticleValidationError(nameof(items), "すべての見出しを重複なく指定してください。"));
            return errors;
        }

        if (requestIds.Any(id => !activeIds.Contains(id)))
        {
            errors.Add(new ArticleValidationError(nameof(items), "記事に存在しない見出しが含まれています。"));
            return errors;
        }

        var headingById = headings.ToDictionary(heading => heading.Id);
        var orderById = items.ToDictionary(item => item.HeadingId);
        var h2Ids = headings.Where(heading => heading.Level == 2).Select(heading => heading.Id).ToHashSet();

        foreach (var item in items)
        {
            var heading = headingById[item.HeadingId];
            if (heading.Level == 2 && item.ParentId.HasValue)
            {
                errors.Add(new ArticleValidationError(nameof(item.ParentId), "H2に親見出しは指定できません。"));
            }
            else if (heading.Level == 3)
            {
                if (!item.ParentId.HasValue || !h2Ids.Contains(item.ParentId.Value))
                {
                    errors.Add(new ArticleValidationError(nameof(item.ParentId), "H3の親H2を指定してください。"));
                }
                else if (orderById[item.ParentId.Value].DisplayOrder >= item.DisplayOrder)
                {
                    errors.Add(new ArticleValidationError(nameof(item.DisplayOrder), "H3は親H2より後に配置してください。"));
                }
            }
        }

        return errors;
    }

    private static void ValidateTitle(string? title, ICollection<ArticleValidationError> errors)
    {
        var length = title?.Trim().Length ?? 0;
        if (length is < 1 or > 250)
        {
            errors.Add(new ArticleValidationError(nameof(title), "見出しタイトルは1から250文字で入力してください。"));
        }
    }

    private static void ValidateTargetLength(int? targetLength, ICollection<ArticleValidationError> errors)
    {
        if (targetLength < 0)
        {
            errors.Add(new ArticleValidationError(nameof(targetLength), "目安文字数は0以上を指定してください。"));
        }
    }

    private static int GetInsertIndex(
        CreateArticleHeadingCommand command,
        IReadOnlyList<ArticleHeading> headings)
    {
        if (command.InsertAfterHeadingId.HasValue)
        {
            var insertAfter = headings.First(item => item.Id == command.InsertAfterHeadingId.Value);
            var index = IndexOfHeading(headings, insertAfter.Id);
            if (command.Level == 2 && insertAfter.Level == 2)
            {
                var lastChildIndex = headings
                    .Select((heading, headingIndex) => new { heading, headingIndex })
                    .Where(item => item.heading.ParentId == insertAfter.Id)
                    .Select(item => item.headingIndex)
                    .DefaultIfEmpty(index)
                    .Max();
                return lastChildIndex + 1;
            }

            return index + 1;
        }

        if (command.Level == 3)
        {
            var parent = headings.First(item => item.Id == command.ParentId);
            var parentIndex = IndexOfHeading(headings, parent.Id);
            var lastChildIndex = headings
                .Select((heading, headingIndex) => new { heading, headingIndex })
                .Where(item => item.heading.ParentId == parent.Id)
                .Select(item => item.headingIndex)
                .DefaultIfEmpty(parentIndex)
                .Max();
            return lastChildIndex + 1;
        }

        return headings.Count;
    }

    private static int IndexOfHeading(IReadOnlyList<ArticleHeading> headings, Guid headingId)
    {
        for (var index = 0; index < headings.Count; index++)
        {
            if (headings[index].Id == headingId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void NormalizeDisplayOrders(IReadOnlyList<ArticleHeading> headings)
    {
        for (var index = 0; index < headings.Count; index++)
        {
            headings[index].DisplayOrder = (index + 1) * DisplayOrderStep;
        }
    }

    private static void UpdateArticleBody(Article article, IReadOnlyList<ArticleHeading> headings)
    {
        article.Body = BuildArticleBody(headings);
        article.HtmlBody = null;
        article.InvalidateHumanReview();
    }

    private static string BuildArticleBody(IReadOnlyList<ArticleHeading> headings)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            headings
                .OrderBy(heading => heading.DisplayOrder)
                .Select(heading =>
                {
                    var prefix = heading.Level == 3 ? "###" : "##";
                    var body = string.IsNullOrWhiteSpace(heading.Body)
                        ? string.Empty
                        : Environment.NewLine + Environment.NewLine + heading.Body.Trim();
                    return $"{prefix} {heading.Title.Trim()}{body}";
                }));
    }

    private static bool HasBlockingStatus(IEnumerable<ArticleHeading> headings)
    {
        return headings.Any(heading => heading.Status is HeadingStatus.Queued or HeadingStatus.Generating);
    }

    private static bool CanAccess(ArticleActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private static ArticleHeadingResponse ToResponse(ArticleHeading heading)
    {
        return new ArticleHeadingResponse(
            heading.Id,
            heading.ParentId,
            heading.Level,
            heading.Title,
            heading.Body,
            heading.DisplayOrder,
            heading.TargetLength,
            heading.ActualLength,
            heading.Status.ToString(),
            heading.UseWebSearch,
            Convert.ToBase64String(heading.RowVersion));
    }
}
