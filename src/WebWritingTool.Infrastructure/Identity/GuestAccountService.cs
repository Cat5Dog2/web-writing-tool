using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Identity;

public sealed class GuestAccountService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext)
{
    public async Task<ApplicationUser> CreateAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = $"guest-{Guid.NewGuid():N}",
            DisplayName = "ゲスト",
            IsEnabled = true,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        EnsureSucceeded(await userManager.CreateAsync(user));
        EnsureSucceeded(await userManager.AddClaimAsync(user,
            new Claim(GuestIdentity.ClaimType, GuestIdentity.ClaimValue)));
        EnsureSucceeded(await userManager.AddToRoleAsync(user, ApplicationRoles.User));
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("ゲストアカウントを作成できませんでした。");
        }
    }
}
