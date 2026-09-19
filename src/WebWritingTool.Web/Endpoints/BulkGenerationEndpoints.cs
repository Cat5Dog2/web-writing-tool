using System.Security.Claims;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class BulkGenerationEndpoints
{
    public static IEndpointRouteBuilder MapBulkGenerationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/bulk-generations").RequireAuthorization().RequireCsrfToken().WithTags("Articles");
        api.MapGet("/batches", async (ClaimsPrincipal principal, IBulkGenerationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetBatchesAsync(Actor(principal), cancellationToken)));
        api.MapGet("/overview", async (Guid? batchId, ClaimsPrincipal principal, IBulkGenerationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetOverviewAsync(Actor(principal), batchId, cancellationToken)));
        api.MapGet("/", async (Guid? batchId, Guid? articleId, ClaimsPrincipal principal,
            IBulkGenerationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetProgressAsync(Actor(principal), batchId, articleId, cancellationToken)));
        api.MapPost("/{articleId:guid}/retry", async (Guid articleId, ClaimsPrincipal principal,
            IBulkGenerationService service, CancellationToken cancellationToken) =>
            Result(await service.RetryAsync(Actor(principal), articleId, cancellationToken)))
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration);
        api.MapPost("/{articleId:guid}/stop", async (Guid articleId, ClaimsPrincipal principal,
            IBulkGenerationService service, CancellationToken cancellationToken) =>
            Result(await service.StopAsync(Actor(principal), articleId, cancellationToken)));
        return endpoints;
    }

    private static ArticleActor Actor(ClaimsPrincipal principal) =>
        new(principal.FindFirstValue(ClaimTypes.NameIdentifier)!, principal.IsInRole(ApplicationRoles.Admin));

    private static IResult Result(ArticleServiceResult result) => result.Succeeded ? Results.NoContent()
        : Results.Problem(statusCode: result.Error == ArticleServiceError.NotFound ? 404 : 409,
            detail: result.Error == ArticleServiceError.NotFound ? "記事が見つかりません。" : "処理状況が変更されました。再読み込みしてください。");
}
