using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Wordpress;

namespace WebWritingTool.Infrastructure.Generation;

// ダミー記事の作成に伴う検索・自動投稿・通知から実サービスへ接続しない。
public sealed class DummyExternalApiClient : IWordpressClient, IDiscordNotificationClient
{
    public Task<WordpressConnectionTestResult> TestConnectionAsync(
        WordpressSiteConnection connection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WordpressConnectionTestResult(
            false, "ダミーモードではWordPressへ接続しません。", DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
        WordpressSiteConnection connection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<WordpressCategoryDto>>([]);
    }

    public Task<WordpressPostResult> CreatePostAsync(
        WordpressPostRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WordpressPostResult(false, null, null,
            ExternalIntegrationErrorCodes.ValidationError, "ダミーモードではWordPressへ投稿しません。"));
    }

    public Task<DiscordNotificationResult> SendAsync(
        DiscordNotificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiscordNotificationResult(false,
            ExternalIntegrationErrorCodes.ValidationError, "ダミーモードではDiscordへ通知しません。",
            null, DateTimeOffset.UtcNow));
    }
}
