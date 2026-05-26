using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Web.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications")
            .RequireAuthorization()
            .WithTags("Notifications");

        notifications.MapGet("/settings", GetSettingAsync)
            .WithName("GetNotificationSetting")
            .WithSummary("通知設定を取得します。");

        notifications.MapPut("/settings", UpdateSettingAsync)
            .WithName("UpdateNotificationSetting")
            .WithSummary("通知設定を保存します。");

        notifications.MapPost("/test", SendTestAsync)
            .WithName("SendTestNotification")
            .WithSummary("通知送信テストを実行します。");

        return endpoints;
    }

    private static async Task<IResult> GetSettingAsync(
        ClaimsPrincipal principal,
        INotificationSettingService notificationSettingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await notificationSettingService.GetAsync(actor, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateSettingAsync(
        [FromBody] UpdateNotificationSettingRequest request,
        ClaimsPrincipal principal,
        INotificationSettingService notificationSettingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await notificationSettingService.UpdateAsync(
            new UpdateNotificationSettingCommand(
                actor,
                request.Provider,
                request.Destination,
                request.Enabled),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> SendTestAsync(
        [FromBody] SendTestNotificationRequest request,
        ClaimsPrincipal principal,
        INotificationTestService notificationTestService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await notificationTestService.SendTestAsync(
            new SendTestNotificationCommand(
                actor,
                request.Provider,
                request.Destination),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static NotificationActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new NotificationActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private static IResult ToProblemResult(
        NotificationServiceError error,
        IReadOnlyList<NotificationValidationError> validationErrors)
    {
        return error switch
        {
            NotificationServiceError.ValidationFailed => Results.ValidationProblem(
                validationErrors
                    .GroupBy(item => item.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Message).ToArray())),
            NotificationServiceError.Disabled => Results.Problem(
                title: "NotificationDisabled",
                detail: validationErrors.FirstOrDefault()?.Message ?? "Discord notification is not enabled.",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Notification operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private sealed record UpdateNotificationSettingRequest(
        string Provider,
        string? Destination,
        bool Enabled);

    private sealed record SendTestNotificationRequest(
        string Provider,
        string? Destination);
}
