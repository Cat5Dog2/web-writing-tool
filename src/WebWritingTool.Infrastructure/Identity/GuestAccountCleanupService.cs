using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Accounts;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Identity;

public sealed class GuestAccountCleanupService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    UserOwnedDataDeletionService ownedDataDeletion)
{
    private const int BatchSize = 100;

    public async Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expiresBefore = now - GuestIdentity.SessionLifetime;
        var candidates = await EligibleGuests(expiresBefore)
            .OrderBy(user => user.CreatedAt).ThenBy(user => user.Id)
            .Select(user => user.Id).Take(BatchSize).ToListAsync(cancellationToken);
        var deleted = 0;

        foreach (var userId in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            // ユーザー行のロックにより、削除中の新規データ挿入をFK側で待機させる。
            var users = await dbContext.Users.FromSqlInterpolated($"""
                SELECT * FROM "AspNetUsers" WHERE "Id" = {userId}
                FOR UPDATE SKIP LOCKED
                """).AsNoTracking().ToListAsync(cancellationToken);
            var user = users.SingleOrDefault();
            if (user is null || !await EligibleGuests(expiresBefore).AnyAsync(item => item.Id == userId, cancellationToken))
            {
                continue;
            }

            // 取得待ちジョブのRunning遷移と削除判定を直列化する。
            var jobs = await dbContext.ArticleGenerationJobs.FromSqlInterpolated($"""
                SELECT * FROM "ArticleGenerationJobs" WHERE "UserId" = {userId}
                FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (jobs.Any(job => job.Status == JobStatus.Running))
            {
                continue;
            }

            await ownedDataDeletion.DeleteOwnedDataAsync(userId, cancellationToken);
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("期限切れゲストアカウントを削除できませんでした。");
            }

            await transaction.CommitAsync(cancellationToken);
            deleted++;
        }

        return deleted;
    }

    private IQueryable<ApplicationUser> EligibleGuests(DateTimeOffset expiresBefore) => dbContext.Users
        .Where(user => user.CreatedAt <= expiresBefore
            && dbContext.UserClaims.Any(claim => claim.UserId == user.Id
                && claim.ClaimType == GuestIdentity.ClaimType && claim.ClaimValue == GuestIdentity.ClaimValue)
            && !dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id
                && dbContext.Roles.Any(role => role.Id == userRole.RoleId && role.Name == ApplicationRoles.Admin))
            && !dbContext.ArticleGenerationJobs.Any(job => job.UserId == user.Id && job.Status == JobStatus.Running));
}
