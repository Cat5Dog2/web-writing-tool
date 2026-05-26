using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WebWritingTool.Application.Admin;
using WebWritingTool.Application.Security;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/admin")
            .RequireAuthorization(ApplicationPolicies.RequireAdmin)
            .RequireCsrfToken()
            .WithTags("Admin");

        api.MapGet("/users", GetUsersAsync)
            .WithName("GetAdminUsers")
            .WithSummary("管理者向けユーザー一覧を取得します。");

        api.MapPost("/users", CreateUserAsync)
            .WithName("CreateAdminUser")
            .WithSummary("管理者がユーザーを作成します。");

        api.MapPut("/users/{userId}", UpdateUserAsync)
            .WithName("UpdateAdminUser")
            .WithSummary("管理者がユーザーの表示名と有効状態を更新します。");

        api.MapPut("/users/{userId}/role", UpdateRoleAsync)
            .WithName("UpdateAdminUserRole")
            .WithSummary("管理者がユーザーのロールを変更します。");

        api.MapPut("/users/{userId}/usage-limit", UpdateUsageLimitAsync)
            .WithName("UpdateAdminUserUsageLimit")
            .WithSummary("管理者がユーザー別利用上限を更新します。");

        api.MapDelete("/users/{userId}", DeleteUserAsync)
            .WithName("DeleteAdminUser")
            .WithSummary("管理者がユーザーと関連業務データを物理削除します。");

        api.MapGet("/audit-logs", GetAuditLogsAsync)
            .WithName("GetAdminAuditLogs")
            .WithSummary("ユーザー管理監査ログを取得します。");

        return endpoints;
    }

    private static async Task<IResult> GetUsersAsync(
        [AsParameters] AdminUserListRequest request,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var response = await adminUserService.ListUsersAsync(
            new AdminUserListQuery(
                request.Page ?? 1,
                request.PageSize ?? 10,
                request.Q,
                request.Role,
                request.IsEnabled,
                request.Sort,
                request.Direction),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateAdminUserRequest request,
        ClaimsPrincipal principal,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await adminUserService.CreateUserAsync(
            new CreateAdminUserCommand(
                actor,
                request.Email,
                request.DisplayName,
                request.Password,
                request.Role,
                request.IsEnabled,
                request.MonthlyLimitChars,
                request.RemainingOutlineCount),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Created($"/api/admin/users/{result.Value.Id}", result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateUserAsync(
        string userId,
        [FromBody] UpdateAdminUserRequest request,
        ClaimsPrincipal principal,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await adminUserService.UpdateUserAsync(
            new UpdateAdminUserCommand(actor, userId, request.DisplayName, request.IsEnabled),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateRoleAsync(
        string userId,
        [FromBody] UpdateAdminUserRoleRequest request,
        ClaimsPrincipal principal,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await adminUserService.UpdateRoleAsync(
            new UpdateAdminUserRoleCommand(actor, userId, request.Role),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateUsageLimitAsync(
        string userId,
        [FromBody] UpdateAdminUserUsageLimitRequest request,
        ClaimsPrincipal principal,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await adminUserService.UpdateUsageLimitAsync(
            new UpdateAdminUserUsageLimitCommand(
                actor,
                userId,
                request.MonthlyLimitChars,
                request.RemainingOutlineCount),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> DeleteUserAsync(
        string userId,
        ClaimsPrincipal principal,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await adminUserService.DeleteUserAsync(actor, userId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> GetAuditLogsAsync(
        [AsParameters] AdminAuditLogRequest request,
        IAdminUserService adminUserService,
        CancellationToken cancellationToken)
    {
        var response = await adminUserService.ListAuditLogsAsync(
            new AdminAuditLogQuery(
                request.Page ?? 1,
                request.PageSize ?? 10,
                request.TargetUserId,
                request.Action,
                request.From,
                request.To),
            cancellationToken);

        return Results.Ok(response);
    }

    private static AdminUserActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : new AdminUserActor(userId);
    }

    private static IResult ToProblemResult(
        AdminUserServiceError error,
        IReadOnlyList<AdminUserValidationError> validationErrors)
    {
        return error switch
        {
            AdminUserServiceError.ValidationFailed => Results.ValidationProblem(
                validationErrors
                    .GroupBy(item => item.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Message).ToArray())),
            AdminUserServiceError.NotFound => Results.NotFound(),
            AdminUserServiceError.RunningJobExists => Results.Problem(
                title: "Conflict",
                detail: "Running jobs exist for this user.",
                statusCode: StatusCodes.Status409Conflict),
            AdminUserServiceError.LastAdminUser => Results.Problem(
                title: "Bad Request",
                detail: "The last Admin user cannot be demoted, disabled, or deleted.",
                statusCode: StatusCodes.Status400BadRequest),
            AdminUserServiceError.SelfOperationNotAllowed => Results.Problem(
                title: "Bad Request",
                detail: "Admin users cannot delete their own account from the admin API.",
                statusCode: StatusCodes.Status400BadRequest),
            AdminUserServiceError.RoleNotFound => Results.Problem(
                title: "Bad Request",
                detail: "The requested role is not configured.",
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Admin user operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private sealed class AdminUserListRequest
    {
        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public string? Q { get; init; }

        public string? Role { get; init; }

        public bool? IsEnabled { get; init; }

        public string? Sort { get; init; }

        public string? Direction { get; init; }
    }

    private sealed record CreateAdminUserRequest(
        string Email,
        string? DisplayName,
        string Password,
        string? Role,
        bool IsEnabled,
        int? MonthlyLimitChars,
        int? RemainingOutlineCount);

    private sealed record UpdateAdminUserRequest(string? DisplayName, bool IsEnabled);

    private sealed record UpdateAdminUserRoleRequest(string Role);

    private sealed record UpdateAdminUserUsageLimitRequest(
        int MonthlyLimitChars,
        int RemainingOutlineCount);

    private sealed class AdminAuditLogRequest
    {
        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public string? TargetUserId { get; init; }

        public string? Action { get; init; }

        public DateOnly? From { get; init; }

        public DateOnly? To { get; init; }
    }
}
