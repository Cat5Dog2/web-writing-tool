using Microsoft.AspNetCore.Antiforgery;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/security/antiforgery-token", (HttpContext httpContext, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(httpContext);
                return Results.Ok(new AntiforgeryTokenResponse(
                    CsrfEndpointFilter.HeaderName,
                    tokens.FormFieldName,
                    tokens.RequestToken ?? string.Empty));
            })
            .RequireAuthorization()
            .WithName("GetAntiforgeryToken")
            .WithSummary("JSON API向けCSRFトークンを取得します。")
            .WithTags("Security");

        return endpoints;
    }

    private sealed record AntiforgeryTokenResponse(
        string HeaderName,
        string FormFieldName,
        string RequestToken);
}
