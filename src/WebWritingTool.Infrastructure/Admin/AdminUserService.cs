using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Admin;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Audit;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Usage;
using WebWritingTool.Infrastructure.Accounts;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Infrastructure.Admin;

public sealed class AdminUserService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    UserOwnedDataDeletionService userOwnedDataDeletionService)
    : IAdminUserService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private const int DefaultMonthlyLimitChars = 200000;
    private const int DefaultRemainingOutlineCount = 40;
    private const string UserEntityType = "AspNetUsers";
    private const string UsageLimitEntityType = "UserUsageLimits";

    private static readonly string[] UserManagementActions =
    [
        "UserCreated",
        "UserUpdated",
        "RoleChanged",
        "UsageLimitUpdated",
        "UserDeleted"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AdminUserListResponse> ListUsersAsync(
        AdminUserListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, DefaultPage);
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var users = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            users = users.Where(user =>
                (user.Email != null && EF.Functions.ILike(user.Email, pattern))
                || (user.DisplayName != null && EF.Functions.ILike(user.DisplayName, pattern)));
        }

        if (query.IsEnabled.HasValue)
        {
            users = users.Where(user => user.IsEnabled == query.IsEnabled.Value);
        }

        var adminRoleId = await dbContext.Roles
            .Where(role => role.Name == ApplicationRoles.Admin)
            .Select(role => role.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var normalizedRole = NormalizeRole(query.Role);
            if (adminRoleId is not null && normalizedRole == ApplicationRoles.Admin)
            {
                users = users.Where(user => dbContext.UserRoles.Any(role => role.UserId == user.Id && role.RoleId == adminRoleId));
            }
            else if (adminRoleId is not null && normalizedRole == ApplicationRoles.User)
            {
                users = users.Where(user => !dbContext.UserRoles.Any(role => role.UserId == user.Id && role.RoleId == adminRoleId));
            }
        }

        users = ApplySort(users, query.Sort, query.Direction);

        var totalCount = await users.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var pageUsers = await users
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserListProjection(
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsEnabled,
                user.CreatedAt,
                user.UpdatedAt,
                user.LastLoginAt))
            .ToListAsync(cancellationToken);

        var userIds = pageUsers.Select(user => user.Id).ToArray();
        var adminUserIds = adminRoleId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await dbContext.UserRoles
                .AsNoTracking()
                .Where(role => role.RoleId == adminRoleId && userIds.Contains(role.UserId))
                .Select(role => role.UserId)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

        var usageLimits = await dbContext.UserUsageLimits
            .AsNoTracking()
            .Where(limit => userIds.Contains(limit.UserId))
            .ToDictionaryAsync(limit => limit.UserId, cancellationToken);

        var items = pageUsers
            .Select(user =>
            {
                usageLimits.TryGetValue(user.Id, out var usageLimit);
                return ToUserResponse(
                    user,
                    adminUserIds.Contains(user.Id) ? ApplicationRoles.Admin : ApplicationRoles.User,
                    usageLimit);
            })
            .ToArray();

        return new AdminUserListResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            page > 1,
            totalPages > 0 && page < totalPages);
    }

    public async Task<AdminUserServiceResult<AdminUserResponse>> CreateUserAsync(
        CreateAdminUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateCreate(command);
        if (validationErrors.Count > 0)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.ValidationFailed,
                validationErrors);
        }

        var role = NormalizeRole(command.Role);
        if (!await roleManager.RoleExistsAsync(role))
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.RoleNotFound);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            UserName = command.Email.Trim(),
            Email = command.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = NormalizeOptional(command.DisplayName),
            IsEnabled = command.IsEnabled
        };

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(createResult));
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(roleResult));
        }

        var usageLimit = new UserUsageLimit
        {
            UserId = user.Id,
            MonthlyLimitChars = command.MonthlyLimitChars ?? DefaultMonthlyLimitChars,
            RemainingOutlineCount = command.RemainingOutlineCount ?? DefaultRemainingOutlineCount
        };
        dbContext.UserUsageLimits.Add(usageLimit);
        AddAuditLog(
            command.Actor.UserId,
            "UserCreated",
            UserEntityType,
            user.Id,
            $"User created with {role} role.",
            new { role, command.IsEnabled });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AdminUserServiceResult<AdminUserResponse>.Success(ToUserResponse(user, role, usageLimit));
    }

    public async Task<AdminUserServiceResult<AdminUserResponse>> UpdateUserAsync(
        UpdateAdminUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateUpdate(command);
        if (validationErrors.Count > 0)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.ValidationFailed,
                validationErrors);
        }

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.NotFound);
        }

        var role = await GetPrimaryRoleAsync(user);
        if (!command.IsEnabled
            && role == ApplicationRoles.Admin
            && !await HasAnotherEnabledAdminAsync(user.Id, cancellationToken))
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.LastAdminUser);
        }

        var previousDisplayName = user.DisplayName;
        var previousIsEnabled = user.IsEnabled;
        user.DisplayName = NormalizeOptional(command.DisplayName);
        user.IsEnabled = command.IsEnabled;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(updateResult));
        }

        AddAuditLog(
            command.Actor.UserId,
            "UserUpdated",
            UserEntityType,
            user.Id,
            $"User updated. IsEnabled: {previousIsEnabled} -> {user.IsEnabled}.",
            new
            {
                previousDisplayName,
                user.DisplayName,
                previousIsEnabled,
                user.IsEnabled
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        var usageLimit = await GetUsageLimitAsync(user.Id, cancellationToken);
        return AdminUserServiceResult<AdminUserResponse>.Success(ToUserResponse(user, role, usageLimit));
    }

    public async Task<AdminUserServiceResult<AdminUserResponse>> UpdateRoleAsync(
        UpdateAdminUserRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var role = NormalizeRole(command.Role);
        if (!ApplicationRoles.All.Contains(role, StringComparer.Ordinal))
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.ValidationFailed,
                [new AdminUserValidationError(nameof(command.Role), "指定できるロールは Admin または User です。")]);
        }

        if (!await roleManager.RoleExistsAsync(role))
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.RoleNotFound);
        }

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.NotFound);
        }

        var previousRole = await GetPrimaryRoleAsync(user);
        if (previousRole == ApplicationRoles.Admin
            && role == ApplicationRoles.User
            && !await HasAnotherEnabledAdminAsync(user.Id, cancellationToken))
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(AdminUserServiceError.LastAdminUser);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);
        var removeResult = await userManager.RemoveFromRolesAsync(
            user,
            currentRoles.Where(item => ApplicationRoles.All.Contains(item, StringComparer.Ordinal)));
        if (!removeResult.Succeeded)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(removeResult));
        }

        var addResult = await userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            return AdminUserServiceResult<AdminUserResponse>.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(addResult));
        }

        AddAuditLog(
            command.Actor.UserId,
            "RoleChanged",
            UserEntityType,
            user.Id,
            $"Role changed from {previousRole} to {role}.",
            new { previousRole, role });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var usageLimit = await GetUsageLimitAsync(user.Id, cancellationToken);
        return AdminUserServiceResult<AdminUserResponse>.Success(ToUserResponse(user, role, usageLimit));
    }

    public async Task<AdminUserServiceResult<AdminUserUsageLimitResponse>> UpdateUsageLimitAsync(
        UpdateAdminUserUsageLimitCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateUsageLimit(command);
        if (validationErrors.Count > 0)
        {
            return AdminUserServiceResult<AdminUserUsageLimitResponse>.Failure(
                AdminUserServiceError.ValidationFailed,
                validationErrors);
        }

        var exists = await dbContext.Users.AnyAsync(user => user.Id == command.UserId, cancellationToken);
        if (!exists)
        {
            return AdminUserServiceResult<AdminUserUsageLimitResponse>.Failure(AdminUserServiceError.NotFound);
        }

        var usageLimit = await dbContext.UserUsageLimits.FirstOrDefaultAsync(
            limit => limit.UserId == command.UserId,
            cancellationToken);

        var previousMonthlyLimitChars = usageLimit?.MonthlyLimitChars ?? DefaultMonthlyLimitChars;
        var previousRemainingOutlineCount = usageLimit?.RemainingOutlineCount ?? DefaultRemainingOutlineCount;

        if (usageLimit is null)
        {
            usageLimit = new UserUsageLimit { UserId = command.UserId };
            dbContext.UserUsageLimits.Add(usageLimit);
        }

        usageLimit.MonthlyLimitChars = command.MonthlyLimitChars;
        usageLimit.RemainingOutlineCount = command.RemainingOutlineCount;

        AddAuditLog(
            command.Actor.UserId,
            "UsageLimitUpdated",
            UsageLimitEntityType,
            command.UserId,
            "Usage limit updated.",
            new
            {
                previousMonthlyLimitChars,
                usageLimit.MonthlyLimitChars,
                previousRemainingOutlineCount,
                usageLimit.RemainingOutlineCount
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        return AdminUserServiceResult<AdminUserUsageLimitResponse>.Success(
            new AdminUserUsageLimitResponse(
                command.UserId,
                usageLimit.MonthlyLimitChars,
                usageLimit.RemainingOutlineCount,
                usageLimit.UpdatedAt));
    }

    public async Task<AdminUserServiceResult> DeleteUserAsync(
        AdminUserActor actor,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(actor.UserId, userId, StringComparison.Ordinal))
        {
            return AdminUserServiceResult.Failure(AdminUserServiceError.SelfOperationNotAllowed);
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AdminUserServiceResult.Failure(AdminUserServiceError.NotFound);
        }

        var role = await GetPrimaryRoleAsync(user);
        if (role == ApplicationRoles.Admin && !await HasAnotherEnabledAdminAsync(user.Id, cancellationToken))
        {
            return AdminUserServiceResult.Failure(AdminUserServiceError.LastAdminUser);
        }

        var hasRunningJobs = await dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.UserId == userId && job.Status == JobStatus.Running,
            cancellationToken);
        if (hasRunningJobs)
        {
            return AdminUserServiceResult.Failure(AdminUserServiceError.RunningJobExists);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var deletionSummary = await userOwnedDataDeletionService.DeleteOwnedDataAsync(
            user.Id,
            cancellationToken);

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            return AdminUserServiceResult.Failure(
                AdminUserServiceError.IdentityFailure,
                ToValidationErrors(deleteResult));
        }

        AddAuditLog(
            actor.UserId,
            "UserDeleted",
            UserEntityType,
            user.Id,
            $"User deleted. Deleted related rows: {deletionSummary.Total}.",
            deletionSummary);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AdminUserServiceResult.Success;
    }

    public async Task<AdminAuditLogListResponse> ListAuditLogsAsync(
        AdminAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, DefaultPage);
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var logs = dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => UserManagementActions.Contains(log.Action));

        if (!string.IsNullOrWhiteSpace(query.TargetUserId))
        {
            logs = logs.Where(log => log.EntityId == query.TargetUserId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Action)
            && UserManagementActions.Contains(query.Action.Trim(), StringComparer.Ordinal))
        {
            var action = query.Action.Trim();
            logs = logs.Where(log => log.Action == action);
        }

        if (query.From.HasValue)
        {
            logs = logs.Where(log => log.CreatedAt >= ToUtcStart(query.From.Value));
        }

        if (query.To.HasValue)
        {
            logs = logs.Where(log => log.CreatedAt < ToUtcStart(query.To.Value.AddDays(1)));
        }

        logs = logs.OrderByDescending(log => log.CreatedAt);

        var totalCount = await logs.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await logs
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AdminAuditLogResponse(
                log.Id,
                log.Action,
                log.UserId,
                log.Action == "UserDeleted" ? null : log.EntityId,
                log.Action == "UserDeleted" ? log.EntityId : null,
                log.Summary,
                log.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminAuditLogListResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            page > 1,
            totalPages > 0 && page < totalPages);
    }

    private static IReadOnlyList<AdminUserValidationError> ValidateCreate(CreateAdminUserCommand command)
    {
        var errors = new List<AdminUserValidationError>();
        ValidateRequiredLength(nameof(command.Email), command.Email, 1, 256, errors);
        ValidateMaxLength(nameof(command.DisplayName), command.DisplayName, 100, errors);
        ValidateRequiredLength(nameof(command.Password), command.Password, 1, 512, errors);
        ValidateRole(command.Role, errors);
        ValidateNonNegative(nameof(command.MonthlyLimitChars), command.MonthlyLimitChars, errors);
        ValidateNonNegative(nameof(command.RemainingOutlineCount), command.RemainingOutlineCount, errors);
        return errors;
    }

    private static IReadOnlyList<AdminUserValidationError> ValidateUpdate(UpdateAdminUserCommand command)
    {
        var errors = new List<AdminUserValidationError>();
        ValidateMaxLength(nameof(command.DisplayName), command.DisplayName, 100, errors);
        return errors;
    }

    private static IReadOnlyList<AdminUserValidationError> ValidateUsageLimit(
        UpdateAdminUserUsageLimitCommand command)
    {
        var errors = new List<AdminUserValidationError>();
        ValidateNonNegative(nameof(command.MonthlyLimitChars), command.MonthlyLimitChars, errors);
        ValidateNonNegative(nameof(command.RemainingOutlineCount), command.RemainingOutlineCount, errors);
        return errors;
    }

    private static void ValidateRole(string? role, ICollection<AdminUserValidationError> errors)
    {
        var normalizedRole = NormalizeRole(role);
        if (!ApplicationRoles.All.Contains(normalizedRole, StringComparer.Ordinal))
        {
            errors.Add(new AdminUserValidationError(nameof(role), "指定できるロールは Admin または User です。"));
        }
    }

    private static void ValidateRequiredLength(
        string field,
        string? value,
        int min,
        int max,
        ICollection<AdminUserValidationError> errors)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < min || length > max)
        {
            errors.Add(new AdminUserValidationError(field, $"{min}から{max}文字で入力してください。"));
        }
    }

    private static void ValidateMaxLength(
        string field,
        string? value,
        int max,
        ICollection<AdminUserValidationError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > max)
        {
            errors.Add(new AdminUserValidationError(field, $"{max}文字以内で入力してください。"));
        }
    }

    private static void ValidateNonNegative(
        string field,
        int? value,
        ICollection<AdminUserValidationError> errors)
    {
        if (value < 0)
        {
            errors.Add(new AdminUserValidationError(field, "0以上で入力してください。"));
        }
    }

    private static IReadOnlyList<AdminUserValidationError> ToValidationErrors(IdentityResult result)
    {
        return result.Errors
            .Select(error => new AdminUserValidationError(error.Code, error.Description))
            .ToArray();
    }

    private async Task<string> GetPrimaryRoleAsync(ApplicationUser user)
    {
        return await userManager.IsInRoleAsync(user, ApplicationRoles.Admin)
            ? ApplicationRoles.Admin
            : ApplicationRoles.User;
    }

    private async Task<bool> HasAnotherEnabledAdminAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var adminRoleId = await dbContext.Roles
            .Where(role => role.Name == ApplicationRoles.Admin)
            .Select(role => role.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRoleId is null)
        {
            return false;
        }

        return await dbContext.Users.AnyAsync(
            user => user.Id != userId
                && user.IsEnabled
                && dbContext.UserRoles.Any(role => role.UserId == user.Id && role.RoleId == adminRoleId),
            cancellationToken);
    }

    private async Task<UserUsageLimit?> GetUsageLimitAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.UserUsageLimits
            .AsNoTracking()
            .FirstOrDefaultAsync(limit => limit.UserId == userId, cancellationToken);
    }

    private void AddAuditLog(
        string adminUserId,
        string action,
        string entityType,
        string entityId,
        string summary,
        object? metadata)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions)
        });
    }

    private static AdminUserResponse ToUserResponse(
        UserListProjection user,
        string role,
        UserUsageLimit? usageLimit)
    {
        return new AdminUserResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            role,
            user.IsEnabled,
            usageLimit?.MonthlyLimitChars ?? DefaultMonthlyLimitChars,
            usageLimit?.RemainingOutlineCount ?? DefaultRemainingOutlineCount,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt);
    }

    private static AdminUserResponse ToUserResponse(
        ApplicationUser user,
        string role,
        UserUsageLimit? usageLimit)
    {
        return new AdminUserResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            role,
            user.IsEnabled,
            usageLimit?.MonthlyLimitChars ?? DefaultMonthlyLimitChars,
            usageLimit?.RemainingOutlineCount ?? DefaultRemainingOutlineCount,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt);
    }

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role?.Trim(), ApplicationRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRoles.Admin;
        }

        if (string.Equals(role?.Trim(), ApplicationRoles.User, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationRoles.User;
        }

        return string.IsNullOrWhiteSpace(role) ? ApplicationRoles.User : role.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IQueryable<ApplicationUser> ApplySort(
        IQueryable<ApplicationUser> query,
        string? sort,
        string? direction)
    {
        var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        return sort?.ToLowerInvariant() switch
        {
            "email" => ascending
                ? query.OrderBy(user => user.Email)
                : query.OrderByDescending(user => user.Email),
            "displayname" => ascending
                ? query.OrderBy(user => user.DisplayName).ThenBy(user => user.Email)
                : query.OrderByDescending(user => user.DisplayName).ThenBy(user => user.Email),
            _ => ascending
                ? query.OrderBy(user => user.CreatedAt)
                : query.OrderByDescending(user => user.CreatedAt)
        };
    }

    private static DateTimeOffset ToUtcStart(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed record UserListProjection(
        string Id,
        string? Email,
        string? DisplayName,
        bool IsEnabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? LastLoginAt);
}
