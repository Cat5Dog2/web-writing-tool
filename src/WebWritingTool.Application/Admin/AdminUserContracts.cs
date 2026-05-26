namespace WebWritingTool.Application.Admin;

public sealed record AdminUserActor(string UserId);

public sealed record AdminUserListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Role,
    bool? IsEnabled,
    string? Sort,
    string? Direction);

public sealed record AdminUserListResponse(
    IReadOnlyList<AdminUserResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record AdminUserResponse(
    string Id,
    string? Email,
    string? DisplayName,
    string Role,
    bool IsEnabled,
    int MonthlyLimitChars,
    int RemainingOutlineCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateAdminUserCommand(
    AdminUserActor Actor,
    string Email,
    string? DisplayName,
    string Password,
    string? Role,
    bool IsEnabled,
    int? MonthlyLimitChars,
    int? RemainingOutlineCount);

public sealed record UpdateAdminUserCommand(
    AdminUserActor Actor,
    string UserId,
    string? DisplayName,
    bool IsEnabled);

public sealed record UpdateAdminUserRoleCommand(
    AdminUserActor Actor,
    string UserId,
    string Role);

public sealed record UpdateAdminUserUsageLimitCommand(
    AdminUserActor Actor,
    string UserId,
    int MonthlyLimitChars,
    int RemainingOutlineCount);

public sealed record AdminUserUsageLimitResponse(
    string UserId,
    int MonthlyLimitChars,
    int RemainingOutlineCount,
    DateTimeOffset UpdatedAt);

public sealed record AdminAuditLogQuery(
    int Page,
    int PageSize,
    string? TargetUserId,
    string? Action,
    DateOnly? From,
    DateOnly? To);

public sealed record AdminAuditLogListResponse(
    IReadOnlyList<AdminAuditLogResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record AdminAuditLogResponse(
    Guid Id,
    string Action,
    string? AdminUserId,
    string? TargetUserId,
    string? TargetUserIdSnapshot,
    string? Summary,
    DateTimeOffset CreatedAt);

public enum AdminUserServiceError
{
    None,
    ValidationFailed,
    NotFound,
    IdentityFailure,
    RoleNotFound,
    LastAdminUser,
    SelfOperationNotAllowed,
    RunningJobExists
}

public sealed record AdminUserValidationError(string Field, string Message);

public sealed record AdminUserServiceResult(
    AdminUserServiceError Error,
    IReadOnlyList<AdminUserValidationError> ValidationErrors)
{
    public bool Succeeded => Error == AdminUserServiceError.None;

    public static AdminUserServiceResult Success { get; } = new(AdminUserServiceError.None, []);

    public static AdminUserServiceResult Failure(
        AdminUserServiceError error,
        IReadOnlyList<AdminUserValidationError>? validationErrors = null)
    {
        return new AdminUserServiceResult(error, validationErrors ?? []);
    }
}

public sealed record AdminUserServiceResult<T>(
    T? Value,
    AdminUserServiceError Error,
    IReadOnlyList<AdminUserValidationError> ValidationErrors)
{
    public bool Succeeded => Error == AdminUserServiceError.None;

    public static AdminUserServiceResult<T> Success(T value)
    {
        return new AdminUserServiceResult<T>(value, AdminUserServiceError.None, []);
    }

    public static AdminUserServiceResult<T> Failure(
        AdminUserServiceError error,
        IReadOnlyList<AdminUserValidationError>? validationErrors = null)
    {
        return new AdminUserServiceResult<T>(default, error, validationErrors ?? []);
    }
}
