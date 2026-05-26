namespace WebWritingTool.Application.Admin;

public interface IAdminUserService
{
    Task<AdminUserListResponse> ListUsersAsync(
        AdminUserListQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminUserServiceResult<AdminUserResponse>> CreateUserAsync(
        CreateAdminUserCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminUserServiceResult<AdminUserResponse>> UpdateUserAsync(
        UpdateAdminUserCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminUserServiceResult<AdminUserResponse>> UpdateRoleAsync(
        UpdateAdminUserRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminUserServiceResult<AdminUserUsageLimitResponse>> UpdateUsageLimitAsync(
        UpdateAdminUserUsageLimitCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminUserServiceResult> DeleteUserAsync(
        AdminUserActor actor,
        string userId,
        CancellationToken cancellationToken = default);

    Task<AdminAuditLogListResponse> ListAuditLogsAsync(
        AdminAuditLogQuery query,
        CancellationToken cancellationToken = default);
}
