using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", LoginFromFormAsync)
            .AllowAnonymous()
            .RequireRateLimiting(SecurityRateLimitPolicyNames.Login)
            .RequireCsrfToken();

        var api = endpoints.MapGroup("/api/account")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("Account");

        api.MapPut("/password", ChangePasswordAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.PasswordChange)
            .WithName("ChangePassword")
            .WithSummary("ログインユーザー本人のパスワードを変更します。");

        api.MapDelete("", WithdrawAccountAsync)
            .WithName("WithdrawAccount")
            .WithSummary("ログインユーザー本人のアカウントを削除します。");

        endpoints.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.Redirect("/login");
            })
            .RequireAuthorization()
            .RequireCsrfToken();

        endpoints.MapPost("/account/withdraw", WithdrawAccountFromFormAsync)
            .RequireAuthorization()
            .RequireCsrfToken();

        endpoints.MapPost("/account/password", ChangePasswordFromFormAsync)
            .RequireAuthorization()
            .RequireRateLimiting(SecurityRateLimitPolicyNames.PasswordChange)
            .RequireCsrfToken();

        return endpoints;
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        ClaimsPrincipal principal,
        IAccountPasswordService passwordService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var result = await passwordService.ChangePasswordAsync(
            new ChangePasswordCommand(
                userId,
                request.CurrentPassword,
                request.NewPassword,
                request.ConfirmNewPassword),
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToProblemResult(result.Error);
        }

        await RefreshSignInAsync(userId, userManager, signInManager);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordFromFormAsync(
        [FromForm] ChangePasswordForm form,
        ClaimsPrincipal principal,
        IAccountPasswordService passwordService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Redirect("/login");
        }

        var result = await passwordService.ChangePasswordAsync(
            new ChangePasswordCommand(
                userId,
                form.CurrentPassword,
                form.NewPassword,
                form.ConfirmNewPassword),
            cancellationToken);

        if (result.Succeeded)
        {
            await RefreshSignInAsync(userId, userManager, signInManager);
            return Results.Redirect("/account?passwordChanged=true");
        }

        var error = result.Error switch
        {
            ChangePasswordError.UserNotFound => "user",
            ChangePasswordError.InvalidCurrentPassword => "current",
            ChangePasswordError.NewPasswordMismatch => "confirm",
            ChangePasswordError.NewPasswordSameAsCurrent => "same",
            ChangePasswordError.InvalidNewPassword => "new",
            _ => "failed"
        };

        return Results.Redirect($"/account?passwordError={error}");
    }

    private static async Task RefreshSignInAsync(
        string userId,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }

    private static async Task<IResult> LoginFromFormAsync(
        [FromForm] LoginForm form,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrWhiteSpace(form.Password))
        {
            return Results.Redirect(GetLoginErrorUrl(form.ReturnUrl, "invalid"));
        }

        var user = await userManager.FindByEmailAsync(form.Email);
        if (user is null)
        {
            return Results.Redirect(GetLoginErrorUrl(form.ReturnUrl, "invalid"));
        }

        if (!user.IsEnabled)
        {
            return Results.Redirect(GetLoginErrorUrl(form.ReturnUrl, "disabled"));
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            form.Password,
            form.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Results.Redirect(GetLoginErrorUrl(form.ReturnUrl, "invalid"));
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        return Results.Redirect(GetSafeReturnUrl(form.ReturnUrl));
    }

    private static async Task<IResult> WithdrawAccountAsync(
        [FromBody] WithdrawAccountRequest request,
        ClaimsPrincipal principal,
        IAccountWithdrawalService withdrawalService,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var result = await withdrawalService.WithdrawAsync(
            new WithdrawAccountCommand(userId, request.CurrentPassword, request.ConfirmText),
            cancellationToken);

        if (result.Succeeded)
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        }

        return ToProblemResult(result.Error);
    }

    private static async Task<IResult> WithdrawAccountFromFormAsync(
        [FromForm] WithdrawAccountForm form,
        ClaimsPrincipal principal,
        IAccountWithdrawalService withdrawalService,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Redirect("/login");
        }

        var result = await withdrawalService.WithdrawAsync(
            new WithdrawAccountCommand(userId, form.CurrentPassword, form.ConfirmText),
            cancellationToken);

        if (result.Succeeded)
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login?accountDeleted=true");
        }

        var error = result.Error switch
        {
            WithdrawAccountError.RunningJobExists => "running-jobs",
            WithdrawAccountError.LastAdminUser => "last-admin",
            WithdrawAccountError.InvalidConfirmationText => "confirm",
            WithdrawAccountError.InvalidPassword => "password",
            _ => "failed"
        };

        return Results.Redirect($"/account?withdrawError={error}");
    }

    private static IResult ToProblemResult(WithdrawAccountError error)
    {
        return error switch
        {
            WithdrawAccountError.RunningJobExists => Results.Problem(
                title: "Conflict",
                detail: "Running jobs exist for this account.",
                statusCode: StatusCodes.Status409Conflict),
            WithdrawAccountError.LastAdminUser => Results.Problem(
                title: "Bad Request",
                detail: "The last Admin user cannot withdraw.",
                statusCode: StatusCodes.Status400BadRequest),
            WithdrawAccountError.InvalidConfirmationText => Results.Problem(
                title: "Bad Request",
                detail: "Confirmation text is invalid.",
                statusCode: StatusCodes.Status400BadRequest),
            WithdrawAccountError.InvalidPassword => Results.Problem(
                title: "Bad Request",
                detail: "Current password is invalid.",
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Account withdrawal failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private static IResult ToProblemResult(ChangePasswordError error)
    {
        return error switch
        {
            ChangePasswordError.UserNotFound => Results.Problem(
                title: "Not Found",
                detail: "User was not found.",
                statusCode: StatusCodes.Status404NotFound),
            ChangePasswordError.InvalidCurrentPassword => Results.Problem(
                title: "Bad Request",
                detail: "Current password is invalid.",
                statusCode: StatusCodes.Status400BadRequest),
            ChangePasswordError.NewPasswordMismatch => Results.Problem(
                title: "Bad Request",
                detail: "New password confirmation does not match.",
                statusCode: StatusCodes.Status400BadRequest),
            ChangePasswordError.NewPasswordSameAsCurrent => Results.Problem(
                title: "Bad Request",
                detail: "New password must differ from the current password.",
                statusCode: StatusCodes.Status400BadRequest),
            ChangePasswordError.InvalidNewPassword => Results.Problem(
                title: "Bad Request",
                detail: "New password does not meet the password policy.",
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem(
                title: "Internal Server Error",
                detail: "Password change failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static string GetLoginErrorUrl(string? returnUrl, string error)
    {
        var url = $"/login?loginError={Uri.EscapeDataString(error)}";
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);

        if (safeReturnUrl != "/")
        {
            url += $"&returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
        }

        return url;
    }

    private static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith("/", StringComparison.Ordinal)
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    private sealed class LoginForm
    {
        public string? Email { get; set; }

        public string? Password { get; set; }

        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }

    private sealed record WithdrawAccountRequest(string CurrentPassword, string ConfirmText);

    private sealed record WithdrawAccountForm(string CurrentPassword, string ConfirmText);

    private sealed record ChangePasswordRequest(
        string CurrentPassword,
        string NewPassword,
        string ConfirmNewPassword);

    private sealed record ChangePasswordForm(
        string CurrentPassword,
        string NewPassword,
        string ConfirmNewPassword);
}
