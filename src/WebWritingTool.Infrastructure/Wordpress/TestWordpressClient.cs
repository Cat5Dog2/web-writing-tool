using WebWritingTool.Application.Wordpress;

namespace WebWritingTool.Infrastructure.Wordpress;

public sealed class TestWordpressClient : IWordpressClient
{
    public Task<WordpressConnectionTestResult> TestConnectionAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WordpressConnectionTestResult(
            true,
            "WordPress接続テストに成功しました。",
            DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WordpressCategoryDto> categories =
        [
            new WordpressCategoryDto(7, "E2E", "e2e")
        ];

        return Task.FromResult(categories);
    }

    public Task<WordpressPostResult> CreatePostAsync(
        WordpressPostRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WordpressPostResult(
            true,
            PostId: 1001,
            PostUrl: $"{request.Connection.BaseUrl.TrimEnd('/')}/?p=1001",
            ErrorCode: null,
            ErrorMessage: null));
    }
}
