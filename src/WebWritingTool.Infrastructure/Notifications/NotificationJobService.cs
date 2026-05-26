using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Notifications;

public sealed class NotificationJobService(
    ApplicationDbContext dbContext,
    JobRetryPolicy retryPolicy)
    : INotificationJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task QueueForSucceededJobAsync(
        QueueNotificationForSucceededJobCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.JobType == JobType.Notification || command.ArticleId is null)
        {
            return;
        }

        var eventType = command.JobType switch
        {
            JobType.BodyGeneration => NotificationEventTypes.ArticleCompleted,
            JobType.WordpressPost => NotificationEventTypes.WordpressPosted,
            _ => null
        };

        if (eventType is null)
        {
            return;
        }

        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == command.ArticleId.Value && item.UserId == command.UserId,
                cancellationToken);

        if (!ShouldNotify(article, eventType))
        {
            return;
        }

        var payload = new NotificationJobPayload(
            article!.Id,
            command.SourceJobId,
            eventType,
            eventType == NotificationEventTypes.WordpressPosted
                ? ExtractString(command.ResultJson, "postUrl")
                : null,
            null,
            null);

        await EnqueueAsync(command.UserId, article.Id, command.SourceJobId, eventType, payload, cancellationToken);
    }

    public async Task QueueForFailedJobAsync(
        QueueNotificationForFailedJobCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.JobType == JobType.Notification || command.ArticleId is null)
        {
            return;
        }

        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == command.ArticleId.Value && item.UserId == command.UserId,
                cancellationToken);

        if (!ShouldNotify(article, NotificationEventTypes.JobFailed))
        {
            return;
        }

        var payload = new NotificationJobPayload(
            article!.Id,
            command.SourceJobId,
            NotificationEventTypes.JobFailed,
            null,
            command.ErrorCode,
            Truncate(command.ErrorMessage, 500));

        await EnqueueAsync(
            command.UserId,
            article.Id,
            command.SourceJobId,
            NotificationEventTypes.JobFailed,
            payload,
            cancellationToken);
    }

    private async Task EnqueueAsync(
        string userId,
        Guid articleId,
        Guid sourceJobId,
        string eventType,
        NotificationJobPayload payload,
        CancellationToken cancellationToken)
    {
        var hasEnabledSetting = await dbContext.NotificationSettings
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId
                    && item.Provider == NotificationProviders.Discord
                    && item.Enabled,
                cancellationToken);

        if (!hasEnabledSetting
            || await HasAlreadyLoggedAsync(userId, sourceJobId, eventType, cancellationToken)
            || await HasQueuedNotificationAsync(userId, sourceJobId, eventType, cancellationToken))
        {
            return;
        }

        dbContext.ArticleGenerationJobs.Add(new ArticleGenerationJob
        {
            UserId = userId,
            ArticleId = articleId,
            JobType = JobType.Notification,
            Status = JobStatus.Queued,
            Priority = -10,
            Progress = 0,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            AttemptCount = 0,
            MaxAttempts = retryPolicy.GetMaxAttempts(JobType.Notification),
            QueuedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasAlreadyLoggedAsync(
        string userId,
        Guid sourceJobId,
        string eventType,
        CancellationToken cancellationToken)
    {
        return await dbContext.NotificationLogs
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == userId
                    && item.JobId == sourceJobId
                    && item.EventType == eventType,
                cancellationToken);
    }

    private async Task<bool> HasQueuedNotificationAsync(
        string userId,
        Guid sourceJobId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var payloads = await dbContext.ArticleGenerationJobs
            .AsNoTracking()
            .Where(job => job.UserId == userId
                && job.JobType == JobType.Notification
                && (job.Status == JobStatus.Queued || job.Status == JobStatus.Running))
            .Select(job => job.PayloadJson)
            .ToListAsync(cancellationToken);

        return payloads.Any(payloadJson =>
            TryReadPayload(payloadJson, out var payload)
            && payload.SourceJobId == sourceJobId
            && string.Equals(payload.EventType, eventType, StringComparison.Ordinal));
    }

    private static bool ShouldNotify(Article? article, string eventType)
    {
        if (article is null
            || !string.Equals(article.NotificationMode, NotificationProviders.Discord, StringComparison.Ordinal))
        {
            return false;
        }

        return eventType != NotificationEventTypes.ArticleCompleted
            || article.Status == ArticleStatus.Completed;
    }

    private static bool TryReadPayload(string payloadJson, out NotificationJobPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<NotificationJobPayload>(payloadJson, JsonOptions)
                ?? new NotificationJobPayload(null, null, string.Empty, null, null, null);
            return true;
        }
        catch (JsonException)
        {
            payload = new NotificationJobPayload(null, null, string.Empty, null, null, null);
            return false;
        }
    }

    private static string? ExtractString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
}
