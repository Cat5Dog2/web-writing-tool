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

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = null,
            Action = "SelfWithdraw",
            EntityType = "AspNetUsers",
            EntityId = user.Id,
            Summary = "User withdrew their account."
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

    private async Task DeleteUserOwnedDataAsync(string userId, CancellationToken cancellationToken)
    {
        var articleIds = dbContext.Articles
            .IgnoreQueryFilters()
            .Where(article => article.UserId == userId)
            .Select(article => article.Id);

        await dbContext.NotificationLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WordpressPosts
            .Where(post => post.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.XSearchPosts
            .Where(post => post.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SearchResults
            .Where(result => result.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UsageLedgers
            .Where(ledger => ledger.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AiGenerationLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AuditLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ArticleGenerationJobs
            .Where(job => job.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ArticleHeadings
            .IgnoreQueryFilters()
            .Where(heading => heading.ParentId != null && articleIds.Contains(heading.ArticleId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ArticleHeadings
            .IgnoreQueryFilters()
            .Where(heading => articleIds.Contains(heading.ArticleId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Articles
            .IgnoreQueryFilters()
            .Where(article => article.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.WordpressSites
            .IgnoreQueryFilters()
            .Where(site => site.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationSettings
            .IgnoreQueryFilters()
            .Where(setting => setting.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserUsageLimits
            .Where(limit => limit.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
