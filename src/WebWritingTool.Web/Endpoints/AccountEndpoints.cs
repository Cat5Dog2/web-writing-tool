using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/login", LoginFromFormAsync)
            .AllowAnonymous();

        var api = endpoints.MapGroup("/api/account")
            .RequireAuthorization()
            .WithTags("Account");

        api.MapDelete("", WithdrawAccountAsync)
            .WithName("WithdrawAccount")
            .WithSummary("ログインユーザー本人のアカウントを削除します。");

        endpoints.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.Redirect("/login");
            })
            .RequireAuthorization();

        endpoints.MapPost("/account/withdraw", WithdrawAccountFromFormAsync)
            .RequireAuthorization();

        return endpoints;
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
}
