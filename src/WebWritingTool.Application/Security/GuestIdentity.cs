using System.Security.Claims;

namespace WebWritingTool.Application.Security;

public static class GuestIdentity
{
    public const string ClaimType = "web-writing-tool:guest";
    public const string ClaimValue = "true";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static bool IsGuest(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true && principal.HasClaim(ClaimType, ClaimValue);
}
