using WebWritingTool.Domain.Articles;

namespace WebWritingTool.Application.Articles;

public sealed record ArticleActor(string UserId, bool IsAdmin);

public sealed record ArticleListQuery(
    int Page,
    int PageSize,
    string? Search,
    IReadOnlyList<string> Tags,
    string? Status,
    DateOnly? CreatedFrom,
    DateOnly? CreatedTo,
    string? Sort,
    string? Direction);

public sealed record ArticleListResponse(
    IReadOnlyList<ArticleListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record ArticleListItemResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    string HeadlineSource,
    string Status,
    string StatusLabel,
    string? Title,
    string Keyword,
    IReadOnlyList<string> Tags,
    string? Memo,
    string? GenerationModel,
    bool CanPostToWordpress,
    bool HasRunningJob,
    bool HasQueuedJob);

public sealed record ArticleDetailResponse(
    Guid Id,
    string Keyword,
    string? Title,
    string Status,
    string? Tone,
    IReadOnlyList<string> Tags,
    string? Memo,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? LearningType,
    string? LearningText,
    string? AdditionalPrompt,
    string? Body,
    string? HtmlBody,
    string? MetaDescription,
    string? GenerationModel,
    string OutlineMethod,
    bool SearchMode,
    bool IsDomesticOnly,
    string NotificationMode,
    string? TopicRisk,
    bool HumanReviewRequired,
    DateTimeOffset? HumanReviewedAt,
    string? HumanReviewedByUserId,
    Guid? WritingProfileWordpressSiteId,
    string? WritingProfileSiteName,
    bool AutoPostToWordpress,
    Guid? AutoPostWordpressSiteId,
    int? AutoPostWordpressCategoryId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion,
    IReadOnlyList<ArticleHeadingResponse> Headings);

public sealed record ArticleHeadingResponse(
    Guid Id,
    Guid? ParentId,
    int Level,
    string Title,
    string? Body,
    int DisplayOrder,
    int? TargetLength,
    int? ActualLength,
    string Status,
    bool UseWebSearch,
    string RowVersion);

public sealed record ArticleHeadingListResponse(IReadOnlyList<ArticleHeadingResponse> Items);

public sealed record CreateArticleHeadingCommand(
    ArticleActor Actor,
    Guid ArticleId,
    Guid? ParentId,
    int Level,
    string Title,
    Guid? InsertAfterHeadingId,
    int? TargetLength,
    bool UseWebSearch);

public sealed record UpdateArticleHeadingCommand(
    ArticleActor Actor,
    Guid ArticleId,
    Guid HeadingId,
    string Title,
    string? Body,
    int? TargetLength,
    bool UseWebSearch,
    string? RowVersion);

public sealed record UpdateArticleHeadingOrderCommand(
    ArticleActor Actor,
    Guid ArticleId,
    IReadOnlyList<ArticleHeadingOrderItem> Items);

public sealed record ArticleHeadingOrderItem(Guid HeadingId, Guid? ParentId, int DisplayOrder);

public sealed record ConvertArticleHtmlCommand(
    ArticleActor Actor,
    Guid ArticleId,
    bool InsertLineBreakAfterPeriod);

public sealed record ConvertArticleHtmlResponse(
    Guid ArticleId,
    string HtmlBody,
    DateTimeOffset ConvertedAt);

public sealed record CreateArticleCommand(
    string UserId,
    string Keyword,
    string? Title,
    bool GenerateImage,
    int? H2Count,
    int? H3Count,
    string? Tone,
    IReadOnlyList<string> Tags,
    string? Memo,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? LearningType,
    string? LearningText,
    string? AdditionalPrompt,
    Guid? WritingProfileWordpressSiteId,
    string OutlineMethod,
    string GenerationModel,
    bool SearchMode,
    bool IsDomesticOnly,
    string? NotificationMode);

public sealed record CreateArticleResponse(Guid Id, string Status, string DetailUrl);

public sealed record BulkCreateArticlesCommand(
    string UserId,
    IReadOnlyList<string> Lines,
    int? H2Count,
    int? H3Count,
    bool IsDomesticOnly,
    string TitleMethod,
    string OutlineMethod,
    string GenerationModel,
    bool SearchMode,
    Guid? WritingProfileWordpressSiteId,
    bool AutoPostToWordpress,
    Guid? AutoPostWordpressSiteId,
    int? AutoPostWordpressCategoryId);

public sealed record BulkCreateArticlesResponse(
    int CreatedArticleCount,
    bool AutoPostToWordpress,
    IReadOnlyList<BulkArticleJobResponse> Jobs,
    IReadOnlyList<BulkArticleRejectedLine> RejectedLines);

public sealed record BulkArticleJobResponse(Guid JobId, Guid ArticleId, string JobType, string Status, string StatusUrl);

public sealed record BulkArticleRejectedLine(int LineNumber, string Line, string Reason);

public sealed record BulkArticleLine(int LineNumber, string Keyword, string? Title);

public sealed record BulkArticleLineParseResult(BulkArticleLine? ArticleLine, BulkArticleRejectedLine? RejectedLine);

public sealed record UpdateArticleCommand(
    ArticleActor Actor,
    Guid ArticleId,
    string Keyword,
    string Title,
    IReadOnlyList<string> Tags,
    string? Memo,
    string? Tone,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? LearningType,
    string? LearningText,
    string? AdditionalPrompt,
    string? MetaDescription,
    string? GenerationModel,
    string? OutlineMethod,
    bool SearchMode,
    bool IsDomesticOnly,
    string? NotificationMode,
    Guid? WritingProfileWordpressSiteId,
    string? Body,
    string? HtmlBody,
    string? RowVersion);

public sealed record ArticleFormOptionsResponse(
    IReadOnlyList<ArticleGenerationModelOption> GenerationModels,
    IReadOnlyList<WritingProfileOption> WritingProfiles);

public sealed record ArticleGenerationModelOption(string Model, string DisplayName, string Provider);

public sealed record WritingProfileOption(
    Guid Id,
    string SiteName,
    int? DefaultCategoryId,
    string? DefaultCategoryName);

public enum ArticleServiceError
{
    None,
    ValidationFailed,
    NotFound,
    ConflictRunningJob,
    ConcurrencyConflict,
    ConflictGeneratingHeading
}

public sealed record ArticleValidationError(string Field, string Message);

public sealed record ArticleServiceResult(ArticleServiceError Error, IReadOnlyList<ArticleValidationError> ValidationErrors)
{
    public bool Succeeded => Error == ArticleServiceError.None;

    public static ArticleServiceResult Success { get; } = new(ArticleServiceError.None, []);

    public static ArticleServiceResult Failure(
        ArticleServiceError error,
        IReadOnlyList<ArticleValidationError>? validationErrors = null)
    {
        return new ArticleServiceResult(error, validationErrors ?? []);
    }
}

public sealed record ArticleServiceResult<T>(
    T? Value,
    ArticleServiceError Error,
    IReadOnlyList<ArticleValidationError> ValidationErrors)
{
    public bool Succeeded => Error == ArticleServiceError.None;

    public static ArticleServiceResult<T> Success(T value)
    {
        return new ArticleServiceResult<T>(value, ArticleServiceError.None, []);
    }

    public static ArticleServiceResult<T> Failure(
        ArticleServiceError error,
        IReadOnlyList<ArticleValidationError>? validationErrors = null)
    {
        return new ArticleServiceResult<T>(default, error, validationErrors ?? []);
    }
}

public static class ArticleStatusExtensions
{
    public static bool CanPostToWordpress(this ArticleStatus status)
    {
        return status is ArticleStatus.Completed or ArticleStatus.Posted;
    }
}
