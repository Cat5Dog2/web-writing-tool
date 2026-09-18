using System.Security.Claims;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class ArticleResearchEndpoints
{
    public static IEndpointRouteBuilder MapArticleResearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/articles/{articleId:guid}/research")
            .RequireAuthorization().RequireCsrfToken().WithTags("Research");
        group.MapGet("", async (Guid articleId, Guid? headingId, ClaimsPrincipal principal,
            IArticleResearchService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(Actor(principal), articleId, headingId, cancellationToken);
            return result is null ? NotFound() : Results.Ok(result);
        }).WithName("GetArticleResearch").WithSummary("現在のモードの有効な参考情報を取得します。");
        group.MapPost("/{source}", async (Guid articleId, string source, ArticleResearchRequest request,
            ClaimsPrincipal principal, IArticleResearchService service, CancellationToken cancellationToken) =>
        {
            var result = await service.EnqueueAsync(Actor(principal), articleId, source, request, cancellationToken);
            return ToResult(result);
        }).RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("QueueArticleResearch").WithSummary("Web検索またはX検索ジョブを登録します。");
        return endpoints;
    }

    private static ArticleActor Actor(ClaimsPrincipal principal) => new(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)!, principal.IsInRole(ApplicationRoles.Admin));

    private static IResult ToResult(JobServiceResult<JobAcceptedResponse> result) => result.Error switch
    {
        JobServiceError.None => Results.Accepted(result.Value!.StatusUrl, result.Value),
        JobServiceError.NotFound => NotFound(),
        JobServiceError.RunningJobExists => Results.Problem(statusCode: 409, detail: "同じ対象の検索ジョブが実行中です。"),
        JobServiceError.RateLimited => Results.Problem(statusCode: 429, detail: "検索の実行間隔を空けてください。"),
        _ => Results.ValidationProblem(result.ValidationErrors.GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()))
    };

    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "Not Found",
        detail: "記事または見出しが見つかりません。");
}
