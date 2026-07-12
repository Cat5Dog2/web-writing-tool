using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Infrastructure.Accounts;

public sealed class AccountPasswordService(
    UserManager<ApplicationUser> userManager,
    ILogger<AccountPasswordService> logger)
    : IAccountPasswordService
{
    public async Task<ChangePasswordResult> ChangePasswordAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.CurrentPassword))
        {
            return new ChangePasswordResult(ChangePasswordError.InvalidCurrentPassword);
        }

        if (string.IsNullOrWhiteSpace(command.NewPassword))
        {
            return new ChangePasswordResult(ChangePasswordError.InvalidNewPassword);
        }

        if (!string.Equals(command.NewPassword, command.ConfirmNewPassword, StringComparison.Ordinal))
        {
            return new ChangePasswordResult(ChangePasswordError.NewPasswordMismatch);
        }

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return new ChangePasswordResult(ChangePasswordError.UserNotFound);
        }

        if (!await userManager.CheckPasswordAsync(user, command.CurrentPassword))
        {
            return new ChangePasswordResult(ChangePasswordError.InvalidCurrentPassword);
        }

        if (await userManager.CheckPasswordAsync(user, command.NewPassword))
        {
            return new ChangePasswordResult(ChangePasswordError.NewPasswordSameAsCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var changeResult = await userManager.ChangePasswordAsync(
            user,
            command.CurrentPassword,
            command.NewPassword);

        if (!changeResult.Succeeded)
        {
            var error = ResolveChangeError(changeResult.Errors);
            return new ChangePasswordResult(error);
        }

        logger.LogInformation("User changed their password. userId={UserId}", user.Id);
        return ChangePasswordResult.Success;
    }

    private static ChangePasswordError ResolveChangeError(IEnumerable<IdentityError> errors)
    {
        var codes = errors.Select(error => error.Code).ToArray();
        if (codes.Contains("PasswordMismatch", StringComparer.Ordinal))
        {
            return ChangePasswordError.InvalidCurrentPassword;
        }

        if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            return ChangePasswordError.InvalidNewPassword;
        }

        return ChangePasswordError.ChangeFailed;
    }
}
