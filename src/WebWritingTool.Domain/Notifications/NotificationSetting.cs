using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Notifications;

public sealed class NotificationSetting : IAuditableEntity, ISoftDeletableEntity, IRowVersionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string DestinationMasked { get; set; } = string.Empty;

    public string EncryptedWebhookUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
