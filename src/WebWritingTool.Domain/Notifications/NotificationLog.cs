using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Notifications;

public sealed class NotificationLog : ICreatedAtEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid? ArticleId { get; set; }

    public Guid? JobId { get; set; }

    public Guid? NotificationSettingId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string DestinationMasked { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
