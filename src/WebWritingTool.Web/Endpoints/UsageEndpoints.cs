using System.Security.Claims;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Usage;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/usage")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("Usage");

        api.MapGet("/summary", GetSummaryAsync)
            .WithName("GetUsageSummary")
            .WithSummary("利用設定の概要を取得します。");

        api.MapGet("/ledgers", GetLedgersAsync)
            .WithName("GetUsageLedgers")
            .WithSummary("利用文字数履歴を取得します。");

        return endpoints;
    }

    private static async Task<IResult> GetSummaryAsync(
        ClaimsPrincipal principal,
        IUsageQueryService usageQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await usageQueryService.GetSummaryAsync(actor, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetLedgersAsync(
        [AsParameters] UsageLedgerListRequest request,
        ClaimsPrincipal principal,
        IUsageQueryService usageQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await usageQueryService.ListLedgersAsync(
            actor,
            new UsageLedgerQuery(
                request.Page ?? 1,
                request.PageSize ?? 10,
                request.From,
                request.To),
            cancellationToken);

        return Results.Ok(response);
    }

    private static UsageActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new UsageActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private sealed class UsageLedgerListRequest
    {
        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public DateOnly? From { get; init; }

        public DateOnly? To { get; init; }
    }
}
