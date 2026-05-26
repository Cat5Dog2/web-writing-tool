namespace WebWritingTool.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public string Provider { get; set; } = "Discord";

    public int TimeoutSeconds { get; set; } = 30;
}
