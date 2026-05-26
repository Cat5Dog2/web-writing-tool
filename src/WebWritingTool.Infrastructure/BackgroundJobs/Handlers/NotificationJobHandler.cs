using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Notifications;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class NotificationJobHandler(
    ApplicationDbContext dbContext,
    ISecretProtector secretProtector,
    IDiscordNotificationClient discordNotificationClient)
    : IJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public JobType JobType => JobType.Notification;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload(job);
        var setting = await dbContext.NotificationSettings.FirstOrDefaultAsync(
            item => item.UserId == job.UserId
                && item.Provider == NotificationProviders.Discord
                && item.Enabled,
            cancellationToken);

        if (setting is null)
        {
            return new JobExecutionResult(SerializeResult(new
            {
                skipped = true,
                reason = "Notification setting is disabled."
            }));
        }

        var article = payload.ArticleId.HasValue
            ? await dbContext.Articles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == payload.ArticleId.Value && item.UserId == job.UserId,
                    cancellationToken)
            : null;

        var notification = BuildNotification(payload, article);
        var sentAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await discordNotificationClient.SendAsync(
                new DiscordNotificationRequest(
                    secretProtector.Unprotect(setting.EncryptedWebhookUrl),
                    notification.Content,
                    [notification.Embed]),
                cancellationToken);

            sentAt = result.SentAt;
            var log = AddNotificationLog(
                job.UserId,
                payload,
                setting,
                result.Success,
                notification.MessageSummary,
                result.ErrorCode,
                result.ErrorMessage,
                sentAt);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (!result.Success)
            {
                throw new JobExecutionException(
                    ToJobErrorCode(result.ErrorCode),
                    result.ErrorMessage ?? "Discord通知に失敗しました。",
                    retryAfter: result.RetryAfter);
            }

            return new JobExecutionResult(SerializeResult(new
            {
                notificationLogId = log.Id,
                payload.EventType,
                payload.SourceJobId
            }));
        }
        catch (ExternalIntegrationException ex)
        {
            AddNotificationLog(
                job.UserId,
                payload,
                setting,
                success: false,
                notification.MessageSummary,
                ex.ErrorCode,
                ex.UserMessage,
                sentAt);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            throw new JobExecutionException(
                ToJobErrorCode(ex.ErrorCode),
                ex.UserMessage,
                ex,
                ex.RetryAfter);
        }
    }

    private static NotificationJobPayload ReadPayload(LeasedJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<NotificationJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "通知ジョブPayloadが不正です。",
                ex);
        }
    }

    private NotificationLog AddNotificationLog(
        string userId,
        NotificationJobPayload payload,
        NotificationSetting setting,
        bool success,
        string message,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset sentAt)
    {
        var log = new NotificationLog
        {
            UserId = userId,
            ArticleId = payload.ArticleId,
            JobId = payload.SourceJobId,
            NotificationSettingId = setting.Id,
            Provider = NotificationProviders.Discord,
            DestinationMasked = setting.DestinationMasked,
            EventType = NormalizeEventType(payload.EventType),
            Status = success ? NotificationStatuses.Succeeded : NotificationStatuses.Failed,
            Message = Truncate(message, 1000),
            ErrorCode = errorCode,
            ErrorMessage = Truncate(errorMessage, 1000),
            CreatedAt = sentAt
        };

        dbContext.NotificationLogs.Add(log);
        return log;
    }

    private static NotificationContent BuildNotification(
        NotificationJobPayload payload,
        Article? article)
    {
        var title = Truncate(article?.Title ?? article?.Keyword ?? "記事", 180) ?? "記事";
        var sourceJobId = payload.SourceJobId?.ToString("D") ?? "-";
        var articleId = payload.ArticleId?.ToString("D") ?? "-";

        return NormalizeEventType(payload.EventType) switch
        {
            NotificationEventTypes.WordpressPosted => new NotificationContent(
                $"WordPress投稿が完了しました: {title}",
                "WordPress投稿完了",
                "WordPress投稿が完了しました。",
                payload.WordpressPostUrl,
                [
                    new DiscordNotificationEmbedField("記事ID", articleId, true),
                    new DiscordNotificationEmbedField("ジョブID", sourceJobId, true),
                    new DiscordNotificationEmbedField("ステータス", "Succeeded", true)
                ]),
            NotificationEventTypes.JobFailed => new NotificationContent(
                $"ジョブが失敗しました: {title}",
                "ジョブ失敗",
                CreateFailureDescription(payload),
                null,
                [
                    new DiscordNotificationEmbedField("記事ID", articleId, true),
                    new DiscordNotificationEmbedField("ジョブID", sourceJobId, true),
                    new DiscordNotificationEmbedField("エラー", payload.ErrorCode ?? "UnknownError", true)
                ]),
            _ => new NotificationContent(
                $"記事作成が完了しました: {title}",
                "記事作成完了",
                "本文生成が完了しました。",
                null,
                [
                    new DiscordNotificationEmbedField("記事ID", articleId, true),
                    new DiscordNotificationEmbedField("ジョブID", sourceJobId, true),
                    new DiscordNotificationEmbedField("ステータス", "Succeeded", true)
                ])
        };
    }

    private static string CreateFailureDescription(NotificationJobPayload payload)
    {
        var summary = string.IsNullOrWhiteSpace(payload.ErrorMessage)
            ? "ジョブ処理に失敗しました。"
            : payload.ErrorMessage.Trim();

        return Truncate(summary, 500) ?? "ジョブ処理に失敗しました。";
    }

    private static string NormalizeEventType(string? eventType)
    {
        return eventType switch
        {
            NotificationEventTypes.ArticleCompleted => NotificationEventTypes.ArticleCompleted,
            NotificationEventTypes.WordpressPosted => NotificationEventTypes.WordpressPosted,
            NotificationEventTypes.JobFailed => NotificationEventTypes.JobFailed,
            _ => NotificationEventTypes.ArticleCompleted
        };
    }

    private static string ToJobErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            ExternalIntegrationErrorCodes.ValidationError => JobErrorCodes.ValidationError,
            ExternalIntegrationErrorCodes.UnauthorizedExternalApi => JobErrorCodes.UnauthorizedExternalApi,
            ExternalIntegrationErrorCodes.ForbiddenExternalApi => JobErrorCodes.ForbiddenExternalApi,
            ExternalIntegrationErrorCodes.RateLimited => JobErrorCodes.RateLimited,
            ExternalIntegrationErrorCodes.Timeout => JobErrorCodes.Timeout,
            ExternalIntegrationErrorCodes.ExternalServerError => JobErrorCodes.ExternalServerError,
            ExternalIntegrationErrorCodes.ExternalBadResponse => JobErrorCodes.ExternalBadResponse,
            ExternalIntegrationErrorCodes.NetworkError => JobErrorCodes.NetworkError,
            _ => JobErrorCodes.UnknownError
        };
    }

    private static string SerializeResult(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
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

    private sealed record NotificationContent(
        string Content,
        string EmbedTitle,
        string Description,
        string? Url,
        IReadOnlyList<DiscordNotificationEmbedField> Fields)
    {
        public string MessageSummary => Description;

        public DiscordNotificationEmbed Embed => new(
            EmbedTitle,
            Description,
            IsAbsoluteHttpsUrl(Url) ? Url : null,
            EmbedTitle == "ジョブ失敗" ? 15548997 : 5763719,
            Fields);
    }

    private static bool IsAbsoluteHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
