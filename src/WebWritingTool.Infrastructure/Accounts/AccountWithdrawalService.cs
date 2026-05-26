using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Audit;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Infrastructure.Accounts;

public sealed class AccountWithdrawalService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    UserOwnedDataDeletionService userOwnedDataDeletionService)
    : IAccountWithdrawalService
{
    public async Task<WithdrawAccountResult> WithdrawAsync(
        WithdrawAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(command.ConfirmText, "DELETE", StringComparison.Ordinal))
        {
            return new WithdrawAccountResult(WithdrawAccountError.InvalidConfirmationText);
        }

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return new WithdrawAccountResult(WithdrawAccountError.UserNotFound);
        }

        if (!await userManager.CheckPasswordAsync(user, command.CurrentPassword))
        {
            return new WithdrawAccountResult(WithdrawAccountError.InvalidPassword);
        }

        if (await IsLastAdminUserAsync(user))
        {
            return new WithdrawAccountResult(WithdrawAccountError.LastAdminUser);
        }

        if (await HasRunningJobsAsync(user.Id, cancellationToken))
        {
            return new WithdrawAccountResult(WithdrawAccountError.RunningJobExists);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var deletionSummary = await userOwnedDataDeletionService.DeleteOwnedDataAsync(
            user.Id,
            cancellationToken);

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            return new WithdrawAccountResult(WithdrawAccountError.DeleteFailed);
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = null,
            Action = "SelfWithdraw",
            EntityType = "AspNetUsers",
            EntityId = user.Id,
            Summary = $"User withdrew their account. Deleted related rows: {deletionSummary.Total}.",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(
                deletionSummary,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return WithdrawAccountResult.Success;
    }

    private async Task<bool> IsLastAdminUserAsync(ApplicationUser user)
    {
        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Admin))
        {
            return false;
        }

        var admins = await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin);
        return admins.Count <= 1;
    }

    private Task<bool> HasRunningJobsAsync(string userId, CancellationToken cancellationToken)
    {
        return dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.UserId == userId && job.Status == JobStatus.Running,
            cancellationToken);
    }

}
