namespace WebWritingTool.Application.Notifications;

public interface IDiscordNotificationClient
{
    Task<DiscordNotificationResult> SendAsync(
        DiscordNotificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DiscordNotificationRequest(
    string WebhookUrl,
    string Content,
    IReadOnlyList<DiscordNotificationEmbed> Embeds);

public sealed record DiscordNotificationEmbed(
    string Title,
    string? Description,
    string? Url,
    int? Color,
    IReadOnlyList<DiscordNotificationEmbedField> Fields);

public sealed record DiscordNotificationEmbedField(
    string Name,
    string Value,
    bool Inline);

public sealed record DiscordNotificationResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    TimeSpan? RetryAfter,
    DateTimeOffset SentAt);
