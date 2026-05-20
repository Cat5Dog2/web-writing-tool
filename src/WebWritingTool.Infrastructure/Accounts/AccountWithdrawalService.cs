using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Infrastructure.Accounts;

public sealed class AccountWithdrawalService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager)
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

        await DeleteUserOwnedDataAsync(user.Id, cancellationToken);

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            return new WithdrawAccountResult(WithdrawAccountError.DeleteFailed);
        }

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

    private static Task<bool> HasRunningJobsAsync(string userId, CancellationToken cancellationToken)
    {
        // P2 adds ArticleGenerationJobs. Until then, this database cannot contain running jobs.
        return Task.FromResult(false);
    }

    private static Task DeleteUserOwnedDataAsync(string userId, CancellationToken cancellationToken)
    {
        // P2 adds user-owned business tables. Identity rows are deleted through UserManager here.
        return Task.CompletedTask;
    }
}
