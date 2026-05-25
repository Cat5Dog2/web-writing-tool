namespace WebWritingTool.Application.Wordpress;

public sealed record WordpressActor(string UserId, bool IsAdmin);

public static class WordpressPostStatuses
{
    public const string Draft = "Draft";
    public const string Publish = "Publish";
}

public static class WordpressPostSources
{
    public const string Manual = "Manual";
    public const string BulkAutoPost = "BulkAutoPost";
}

public sealed record WordpressSiteListResponse(IReadOnlyList<WordpressSiteResponse> Items);

public sealed record WordpressSiteResponse(
    Guid Id,
    string SiteName,
    string BaseUrl,
    string LoginId,
    int? DefaultCategoryId,
    string? DefaultCategoryName,
    string? SiteAdminProfile,
    string? WritingCharacter,
    string? ReaderPersona,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion);

public sealed record CreateWordpressSiteCommand(
    string UserId,
    string SiteName,
    string BaseUrl,
    string LoginId,
    string ApplicationPassword,
    int? DefaultCategoryId,
    string? DefaultCategoryName,
    string? SiteAdminProfile,
    string? WritingCharacter,
    string? ReaderPersona);

public sealed record UpdateWordpressSiteCommand(
    WordpressActor Actor,
    Guid WordpressSiteId,
    string SiteName,
    string BaseUrl,
    string LoginId,
    string? ApplicationPassword,
    int? DefaultCategoryId,
    string? DefaultCategoryName,
    string? SiteAdminProfile,
    string? WritingCharacter,
    string? ReaderPersona,
    string? RowVersion);

public sealed record WordpressCategoryListResponse(IReadOnlyList<WordpressCategoryDto> Items);

public sealed record WordpressConnectionTestResponse(
    bool Success,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record WordpressPostPreviewResponse(
    Guid ArticleId,
    string Title,
    string HtmlBody,
    bool HumanReviewRequired,
    DateTimeOffset? HumanReviewedAt,
    IReadOnlyList<WordpressSiteResponse> AvailableSites);

public sealed record CreateWordpressPostJobCommand(
    WordpressActor Actor,
    Guid ArticleId,
    Guid WordpressSiteId,
    string Title,
    string HtmlBody,
    int? CategoryId,
    string? Status);

public sealed record WordpressPostPayload(
    Guid ArticleId,
    Guid WordpressSiteId,
    string Title,
    string HtmlBody,
    int? CategoryId,
    string Status,
    string Source);

public sealed record WordpressPostHistoryResponse(IReadOnlyList<WordpressPostHistoryItemResponse> Items);

public sealed record WordpressPostHistoryItemResponse(
    Guid Id,
    Guid WordpressSiteId,
    string SiteName,
    int? PostId,
    string? PostUrl,
    int? CategoryId,
    string RequestedStatus,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt);

public enum WordpressServiceError
{
    None,
    ValidationFailed,
    NotFound,
    Conflict,
    ConcurrencyConflict,
    ExternalFailure,
    HumanReviewRequired,
    NotPostable
}

public sealed record WordpressValidationError(string Field, string Message);

public sealed record WordpressServiceResult(
    WordpressServiceError Error,
    IReadOnlyList<WordpressValidationError> ValidationErrors)
{
    public bool Succeeded => Error == WordpressServiceError.None;

    public static WordpressServiceResult Success { get; } = new(WordpressServiceError.None, []);

    public static WordpressServiceResult Failure(
        WordpressServiceError error,
        IReadOnlyList<WordpressValidationError>? validationErrors = null)
    {
        return new WordpressServiceResult(error, validationErrors ?? []);
    }
}

public sealed record WordpressServiceResult<T>(
    T? Value,
    WordpressServiceError Error,
    IReadOnlyList<WordpressValidationError> ValidationErrors)
{
    public bool Succeeded => Error == WordpressServiceError.None;

    public static WordpressServiceResult<T> Success(T value)
    {
        return new WordpressServiceResult<T>(value, WordpressServiceError.None, []);
    }

    public static WordpressServiceResult<T> Failure(
        WordpressServiceError error,
        IReadOnlyList<WordpressValidationError>? validationErrors = null)
    {
        return new WordpressServiceResult<T>(default, error, validationErrors ?? []);
    }
}
