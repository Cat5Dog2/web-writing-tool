namespace WebWritingTool.Application.Notifications;

using WebWritingTool.Domain.Jobs;

public sealed record NotificationActor(string UserId, bool IsAdmin);

public static class NotificationProviders
{
    public const string Discord = "Discord";
}

public static class NotificationEventTypes
{
    public const string ArticleCompleted = "ArticleCompleted";
    public const string WordpressPosted = "WordpressPosted";
    public const string JobFailed = "JobFailed";
    public const string Test = "Test";
}

public static class NotificationStatuses
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed record NotificationSettingResponse(
    string Provider,
    string? DestinationMasked,
    bool Enabled,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateNotificationSettingCommand(
    NotificationActor Actor,
    string Provider,
    string? Destination,
    bool Enabled);

public sealed record SendTestNotificationCommand(
    NotificationActor Actor,
    string Provider,
    string? Destination);

public sealed record NotificationTestResponse(
    bool Success,
    string Message,
    DateTimeOffset SentAt);

public sealed record QueueNotificationForSucceededJobCommand(
    string UserId,
    Guid SourceJobId,
    Guid? ArticleId,
    JobType JobType,
    string? ResultJson);

public sealed record QueueNotificationForFailedJobCommand(
    string UserId,
    Guid SourceJobId,
    Guid? ArticleId,
    JobType JobType,
    string ErrorCode,
    string? ErrorMessage);

public sealed record NotificationJobPayload(
    Guid? ArticleId,
    Guid? SourceJobId,
    string EventType,
    string? WordpressPostUrl,
    string? ErrorCode,
    string? ErrorMessage);

public enum NotificationServiceError
{
    None,
    ValidationFailed,
    NotFound,
    ConcurrencyConflict,
    ExternalFailure,
    Disabled,
    RateLimited
}

public sealed record NotificationValidationError(string Field, string Message);

public sealed record NotificationServiceResult(
    NotificationServiceError Error,
    IReadOnlyList<NotificationValidationError> ValidationErrors)
{
    public bool Succeeded => Error == NotificationServiceError.None;

    public static NotificationServiceResult Success { get; } = new(NotificationServiceError.None, []);

    public static NotificationServiceResult Failure(
        NotificationServiceError error,
        IReadOnlyList<NotificationValidationError>? validationErrors = null)
    {
        return new NotificationServiceResult(error, validationErrors ?? []);
    }
}

public sealed record NotificationServiceResult<T>(
    T? Value,
    NotificationServiceError Error,
    IReadOnlyList<NotificationValidationError> ValidationErrors)
{
    public bool Succeeded => Error == NotificationServiceError.None;

    public static NotificationServiceResult<T> Success(T value)
    {
        return new NotificationServiceResult<T>(value, NotificationServiceError.None, []);
    }

    public static NotificationServiceResult<T> Failure(
        NotificationServiceError error,
        IReadOnlyList<NotificationValidationError>? validationErrors = null)
    {
        return new NotificationServiceResult<T>(default, error, validationErrors ?? []);
    }
}

public static class DiscordWebhookUrl
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "discordapp.com",
        "canary.discord.com",
        "ptb.discord.com"
    };

    public static bool TryNormalize(string? value, out string normalized, out string? errorMessage)
    {
        normalized = string.Empty;
        errorMessage = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Discord Webhook URLはHTTPS URLで指定してください。";
            return false;
        }

        if (!uri.IsDefaultPort || !AllowedHosts.Contains(uri.Host))
        {
            errorMessage = "Discord Webhook URLのホストが不正です。";
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length != 4
            || !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "webhooks", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[2])
            || string.IsNullOrWhiteSpace(segments[3]))
        {
            errorMessage = "Discord Webhook URLの形式が不正です。";
            return false;
        }

        normalized = new UriBuilder(Uri.UriSchemeHttps, uri.Host.ToLowerInvariant())
        {
            Path = $"api/webhooks/{segments[2]}/{segments[3]}",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.ToString().TrimEnd('/');

        return true;
    }

    public static string Mask(string normalizedUrl)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return "https://discord.com/api/webhooks/.../...";
        }

        return $"https://{uri.Host.ToLowerInvariant()}/api/webhooks/.../...";
    }
}
