using WebWritingTool.Application.Notifications;

namespace WebWritingTool.Infrastructure.Notifications;

public sealed class TestDiscordNotificationClient : IDiscordNotificationClient
{
    public Task<DiscordNotificationResult> SendAsync(
        DiscordNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DiscordNotificationResult(
            true,
            ErrorCode: null,
            ErrorMessage: null,
            RetryAfter: null,
            DateTimeOffset.UtcNow));
    }
}
