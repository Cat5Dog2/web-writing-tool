namespace WebWritingTool.Application.Notifications;

public interface INotificationSettingService
{
    Task<NotificationSettingResponse> GetAsync(
        NotificationActor actor,
        CancellationToken cancellationToken = default);

    Task<NotificationServiceResult<NotificationSettingResponse>> UpdateAsync(
        UpdateNotificationSettingCommand command,
        CancellationToken cancellationToken = default);
}

public interface INotificationTestService
{
    Task<NotificationServiceResult<NotificationTestResponse>> SendTestAsync(
        SendTestNotificationCommand command,
        CancellationToken cancellationToken = default);
}

public interface INotificationJobService
{
    Task QueueForSucceededJobAsync(
        QueueNotificationForSucceededJobCommand command,
        CancellationToken cancellationToken = default);

    Task QueueForFailedJobAsync(
        QueueNotificationForFailedJobCommand command,
        CancellationToken cancellationToken = default);
}
