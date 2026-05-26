using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class WordpressEndpoints
{
    public static IEndpointRouteBuilder MapWordpressEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sites = endpoints.MapGroup("/api/wordpress-sites")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("WordPress Sites");

        sites.MapGet("", ListSitesAsync)
            .WithName("ListWordpressSites")
            .WithSummary("WordPressサイト一覧を取得します。");

        sites.MapPost("", CreateSiteAsync)
            .WithName("CreateWordpressSite")
            .WithSummary("WordPressサイトを登録します。");

        sites.MapPut("/{wordpressSiteId:guid}", UpdateSiteAsync)
            .WithName("UpdateWordpressSite")
            .WithSummary("WordPressサイトを更新します。");

        sites.MapDelete("/{wordpressSiteId:guid}", DeleteSiteAsync)
            .WithName("DeleteWordpressSite")
            .WithSummary("WordPressサイトを論理削除します。");

        sites.MapGet("/{wordpressSiteId:guid}/categories", GetCategoriesAsync)
            .WithName("GetWordpressCategories")
            .WithSummary("WordPressカテゴリを取得します。");

        sites.MapPost("/{wordpressSiteId:guid}/test", TestConnectionAsync)
            .WithName("TestWordpressConnection")
            .WithSummary("WordPress接続テストを実行します。");

        var posts = endpoints.MapGroup("/api/articles/{articleId:guid}/wordpress-posts")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("WordPress Posts");

        posts.MapGet("/preview", GetPostPreviewAsync)
            .WithName("GetWordpressPostPreview")
            .WithSummary("WordPress投稿プレビューを取得します。");

        posts.MapGet("", GetPostHistoryAsync)
            .WithName("GetWordpressPostHistory")
            .WithSummary("WordPress投稿履歴を取得します。");

        posts.MapPost("", CreatePostJobAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.WordpressPost)
            .WithName("CreateWordpressPostJob")
            .WithSummary("WordPress投稿ジョブを登録します。");

        return endpoints;
    }

    private static async Task<IResult> ListSitesAsync(
        ClaimsPrincipal principal,
        IWordpressSiteQueryService wordpressSiteQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await wordpressSiteQueryService.ListAsync(actor, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateSiteAsync(
        [FromBody] CreateWordpressSiteRequest request,
        ClaimsPrincipal principal,
        IWordpressSiteCommandService wordpressSiteCommandService,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var result = await wordpressSiteCommandService.CreateAsync(
            new CreateWordpressSiteCommand(
                userId,
                request.SiteName,
                request.BaseUrl,
                request.LoginId,
                request.ApplicationPassword,
                request.DefaultCategoryId,
                request.DefaultCategoryName,
                request.SiteAdminProfile,
                request.WritingCharacter,
                request.ReaderPersona),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Created($"/api/wordpress-sites/{result.Value.Id}", result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateSiteAsync(
        Guid wordpressSiteId,
        [FromBody] UpdateWordpressSiteRequest request,
        ClaimsPrincipal principal,
        IWordpressSiteCommandService wordpressSiteCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await wordpressSiteCommandService.UpdateAsync(
            new UpdateWordpressSiteCommand(
                actor,
                wordpressSiteId,
                request.SiteName,
                request.BaseUrl,
                request.LoginId,
                request.ApplicationPassword,
                request.DefaultCategoryId,
                request.DefaultCategoryName,
                request.SiteAdminProfile,
                request.WritingCharacter,
                request.ReaderPersona,
                request.RowVersion),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> DeleteSiteAsync(
        Guid wordpressSiteId,
        ClaimsPrincipal principal,
        IWordpressSiteCommandService wordpressSiteCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await wordpressSiteCommandService.DeleteAsync(actor, wordpressSiteId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> GetCategoriesAsync(
        Guid wordpressSiteId,
        ClaimsPrincipal principal,
        IWordpressSiteQueryService wordpressSiteQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await wordpressSiteQueryService.GetCategoriesAsync(actor, wordpressSiteId, cancellationToken);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> TestConnectionAsync(
        Guid wordpressSiteId,
        ClaimsPrincipal principal,
        IWordpressSiteCommandService wordpressSiteCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await wordpressSiteCommandService.TestConnectionAsync(actor, wordpressSiteId, cancellationToken);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> GetPostPreviewAsync(
        Guid articleId,
        ClaimsPrincipal principal,
        IWordpressPostQueryService wordpressPostQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await wordpressPostQueryService.GetPreviewAsync(actor, articleId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> GetPostHistoryAsync(
        Guid articleId,
        ClaimsPrincipal principal,
        IWordpressPostQueryService wordpressPostQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await wordpressPostQueryService.GetHistoryAsync(actor, articleId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> CreatePostJobAsync(
        Guid articleId,
        [FromBody] CreateWordpressPostRequest request,
        ClaimsPrincipal principal,
        IWordpressPostCommandService wordpressPostCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await wordpressPostCommandService.CreatePostJobAsync(
            new CreateWordpressPostJobCommand(
                actor,
                articleId,
                request.WordpressSiteId,
                request.Title,
                request.HtmlBody,
                request.CategoryId,
                request.Status),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Accepted(result.Value.StatusUrl, result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static WordpressActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new WordpressActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private static IResult ToProblemResult(
        WordpressServiceError error,
        IReadOnlyList<WordpressValidationError> validationErrors)
    {
        return error switch
        {
            WordpressServiceError.ValidationFailed => Results.ValidationProblem(
                validationErrors
                    .GroupBy(item => item.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Message).ToArray())),
            WordpressServiceError.NotFound => Results.NotFound(),
            WordpressServiceError.Conflict => Results.Problem(
                title: "Conflict",
                detail: "A queued or running WordPress post job already exists.",
                statusCode: StatusCodes.Status409Conflict),
            WordpressServiceError.ConcurrencyConflict => Results.Problem(
                title: "Conflict",
                detail: "The WordPress site was updated by another request.",
                statusCode: StatusCodes.Status409Conflict),
            WordpressServiceError.ExternalFailure => Results.Problem(
                title: "External integration failed",
                detail: validationErrors.FirstOrDefault()?.Message ?? "WordPress連携に失敗しました。",
                statusCode: StatusCodes.Status502BadGateway),
            WordpressServiceError.HumanReviewRequired => Results.Problem(
                title: "HumanReviewRequired",
                detail: "人間確認前の記事はWordPressへ公開投稿できません。",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            WordpressServiceError.NotPostable => Results.Problem(
                title: "NotPostable",
                detail: "投稿可能な記事状態ではありません。",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            WordpressServiceError.RateLimited => Results.Problem(
                title: "RateLimited",
                detail: "Too many requests.",
                statusCode: StatusCodes.Status429TooManyRequests),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "WordPress operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private sealed record CreateWordpressSiteRequest(
        string SiteName,
        string BaseUrl,
        string LoginId,
        string ApplicationPassword,
        int? DefaultCategoryId,
        string? DefaultCategoryName,
        string? SiteAdminProfile,
        string? WritingCharacter,
        string? ReaderPersona);

    private sealed record UpdateWordpressSiteRequest(
        string SiteName,
        string BaseUrl,
        string LoginId,
        string? ApplicationPassword,
        int? DefaultCategoryId,
        string? DefaultCategoryName,
        string? SiteAdminProfile,
        string? WritingCharacter,
        string? ReaderPersona,
        string? RowVersion);

    private sealed record CreateWordpressPostRequest(
        Guid WordpressSiteId,
        string Title,
        string HtmlBody,
        int? CategoryId,
        string? Status);
}
