using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Notifications;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Notifications;

public sealed class NotificationSettingService(
    ApplicationDbContext dbContext,
    ISecretProtector secretProtector,
    ISecretMasker secretMasker,
    ISecurityRateLimiter securityRateLimiter,
    IDiscordNotificationClient discordNotificationClient)
    : INotificationSettingService, INotificationTestService
{
    public async Task<NotificationSettingResponse> GetAsync(
        NotificationActor actor,
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == actor.UserId
                    && item.Provider == NotificationProviders.Discord,
                cancellationToken);

        return setting is null
            ? new NotificationSettingResponse(NotificationProviders.Discord, null, false, null)
            : ToResponse(setting);
    }

    public async Task<NotificationServiceResult<NotificationSettingResponse>> UpdateAsync(
        UpdateNotificationSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.NotificationSettings
            .FirstOrDefaultAsync(
                item => item.UserId == command.Actor.UserId
                    && item.Provider == NotificationProviders.Discord,
                cancellationToken);

        var validationErrors = Validate(command, destinationRequired: setting is null);
        if (validationErrors.Count > 0)
        {
            return NotificationServiceResult<NotificationSettingResponse>.Failure(
                NotificationServiceError.ValidationFailed,
                validationErrors);
        }

        var hasNewDestination = !string.IsNullOrWhiteSpace(command.Destination);
        var normalizedUrl = string.Empty;
        if (hasNewDestination)
        {
            DiscordWebhookUrl.TryNormalize(command.Destination, out normalizedUrl, out _);
        }

        var now = DateTimeOffset.UtcNow;
        if (setting is null)
        {
            setting = new NotificationSetting
            {
                UserId = command.Actor.UserId,
                Provider = NotificationProviders.Discord,
                CreatedAt = now
            };
            dbContext.NotificationSettings.Add(setting);
        }

        if (hasNewDestination)
        {
            setting.DestinationMasked = DiscordWebhookUrl.Mask(normalizedUrl);
            setting.EncryptedWebhookUrl = secretProtector.Protect(normalizedUrl);
        }

        setting.Enabled = command.Enabled;
        setting.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NotificationServiceResult<NotificationSettingResponse>.Success(ToResponse(setting));
    }

    public async Task<NotificationServiceResult<NotificationTestResponse>> SendTestAsync(
        SendTestNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateTest(command);
        if (validationErrors.Count > 0)
        {
            return NotificationServiceResult<NotificationTestResponse>.Failure(
                NotificationServiceError.ValidationFailed,
                validationErrors);
        }

        if (!await securityRateLimiter.IsAllowedAsync(
                SecurityRateLimitPolicyNames.NotificationTest,
                command.Actor.UserId,
                cancellationToken))
        {
            return NotificationServiceResult<NotificationTestResponse>.Failure(NotificationServiceError.RateLimited);
        }

        var destination = await ResolveDestinationAsync(command, cancellationToken);
        if (destination is null)
        {
            return NotificationServiceResult<NotificationTestResponse>.Failure(
                NotificationServiceError.Disabled,
                [new NotificationValidationError(nameof(command.Destination), "有効なDiscord通知設定がありません。")]);
        }

        var message = "Discord通知の送信テストです。";
        var sentAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await discordNotificationClient.SendAsync(
                new DiscordNotificationRequest(
                    destination.WebhookUrl,
                    "通知テスト: Web Writing Tool",
                    [
                        new DiscordNotificationEmbed(
                            "通知テスト",
                            message,
                            null,
                            5763719,
                            [])
                    ]),
                cancellationToken);

            sentAt = result.SentAt;
            AddNotificationLog(
                command.Actor.UserId,
                destination,
                NotificationEventTypes.Test,
                result.Success,
                message,
                result.ErrorCode,
                result.ErrorMessage,
                sentAt);
            await dbContext.SaveChangesAsync(cancellationToken);

            return NotificationServiceResult<NotificationTestResponse>.Success(
                new NotificationTestResponse(
                    result.Success,
                    result.Success ? "通知を送信しました。" : result.ErrorMessage ?? "通知送信に失敗しました。",
                    sentAt));
        }
        catch (ExternalIntegrationException ex)
        {
            AddNotificationLog(
                command.Actor.UserId,
                destination,
                NotificationEventTypes.Test,
                success: false,
                message,
                ex.ErrorCode,
                ex.UserMessage,
                sentAt);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return NotificationServiceResult<NotificationTestResponse>.Success(
                new NotificationTestResponse(false, ex.UserMessage, sentAt));
        }
    }

    private static List<NotificationValidationError> Validate(
        UpdateNotificationSettingCommand command,
        bool destinationRequired)
    {
        var errors = new List<NotificationValidationError>();

        if (string.IsNullOrWhiteSpace(command.Actor.UserId))
        {
            errors.Add(new NotificationValidationError(nameof(command.Actor.UserId), "ユーザーIDが不正です。"));
        }

        if (!string.Equals(command.Provider, NotificationProviders.Discord, StringComparison.Ordinal))
        {
            errors.Add(new NotificationValidationError(nameof(command.Provider), "通知ProviderはDiscordを指定してください。"));
        }

        if (destinationRequired && string.IsNullOrWhiteSpace(command.Destination))
        {
            errors.Add(new NotificationValidationError(nameof(command.Destination), "Discord Webhook URLを入力してください。"));
        }
        else if (!string.IsNullOrWhiteSpace(command.Destination)
            && !DiscordWebhookUrl.TryNormalize(command.Destination, out _, out var errorMessage))
        {
            errors.Add(new NotificationValidationError(nameof(command.Destination), errorMessage ?? "Discord Webhook URLが不正です。"));
        }

        return errors;
    }

    private static List<NotificationValidationError> ValidateTest(SendTestNotificationCommand command)
    {
        var errors = new List<NotificationValidationError>();

        if (string.IsNullOrWhiteSpace(command.Actor.UserId))
        {
            errors.Add(new NotificationValidationError(nameof(command.Actor.UserId), "ユーザーIDが不正です。"));
        }

        if (!string.Equals(command.Provider, NotificationProviders.Discord, StringComparison.Ordinal))
        {
            errors.Add(new NotificationValidationError(nameof(command.Provider), "通知ProviderはDiscordを指定してください。"));
        }

        if (!string.IsNullOrWhiteSpace(command.Destination)
            && !DiscordWebhookUrl.TryNormalize(command.Destination, out _, out var errorMessage))
        {
            errors.Add(new NotificationValidationError(nameof(command.Destination), errorMessage ?? "Discord Webhook URLが不正です。"));
        }

        return errors;
    }

    private async Task<NotificationDestination?> ResolveDestinationAsync(
        SendTestNotificationCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.Destination))
        {
            DiscordWebhookUrl.TryNormalize(command.Destination, out var normalizedUrl, out _);
            return new NotificationDestination(
                null,
                normalizedUrl,
                DiscordWebhookUrl.Mask(normalizedUrl));
        }

        var setting = await dbContext.NotificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == command.Actor.UserId
                    && item.Provider == NotificationProviders.Discord
                    && item.Enabled,
                cancellationToken);

        return setting is null
            ? null
            : new NotificationDestination(
                setting.Id,
                secretProtector.Unprotect(setting.EncryptedWebhookUrl),
                setting.DestinationMasked);
    }

    private void AddNotificationLog(
        string userId,
        NotificationDestination destination,
        string eventType,
        bool success,
        string message,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset sentAt)
    {
        dbContext.NotificationLogs.Add(new NotificationLog
        {
            UserId = userId,
            NotificationSettingId = destination.SettingId,
            Provider = NotificationProviders.Discord,
            DestinationMasked = destination.DestinationMasked,
            EventType = eventType,
            Status = success ? NotificationStatuses.Succeeded : NotificationStatuses.Failed,
            Message = Truncate(message, 1000),
            ErrorCode = errorCode,
            ErrorMessage = Truncate(secretMasker.Mask(errorMessage), 1000),
            CreatedAt = sentAt
        });
    }

    private static NotificationSettingResponse ToResponse(NotificationSetting setting)
    {
        return new NotificationSettingResponse(
            setting.Provider,
            setting.DestinationMasked,
            setting.Enabled,
            setting.UpdatedAt);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record NotificationDestination(
        Guid? SettingId,
        string WebhookUrl,
        string DestinationMasked);
}
