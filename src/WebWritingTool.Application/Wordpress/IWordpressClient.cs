namespace WebWritingTool.Application.Wordpress;

public interface IWordpressClient
{
    Task<WordpressConnectionTestResult> TestConnectionAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default);

    Task<WordpressPostResult> CreatePostAsync(
        WordpressPostRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WordpressSiteConnection(
    string BaseUrl,
    string LoginId,
    string ApplicationPassword);

public sealed record WordpressConnectionTestResult(
    bool Success,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record WordpressCategoryDto(
    int Id,
    string Name,
    string Slug);

public sealed record WordpressPostRequest(
    WordpressSiteConnection Connection,
    string Title,
    string HtmlBody,
    int? CategoryId,
    string Status);

public sealed record WordpressPostResult(
    bool Success,
    int? PostId,
    string? PostUrl,
    string? ErrorCode,
    string? ErrorMessage);
