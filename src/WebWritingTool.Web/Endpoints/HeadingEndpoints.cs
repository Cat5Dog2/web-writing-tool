using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class HeadingEndpoints
{
    public static IEndpointRouteBuilder MapHeadingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/articles/{articleId:guid}/headings")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("Headings");

        api.MapGet("", GetHeadingsAsync)
            .WithName("GetArticleHeadings")
            .WithSummary("記事の見出し一覧を取得します。");

        api.MapPost("", CreateHeadingAsync)
            .WithName("CreateArticleHeading")
            .WithSummary("記事へ見出しを追加します。");

        api.MapPut("/{headingId:guid}", UpdateHeadingAsync)
            .WithName("UpdateArticleHeading")
            .WithSummary("記事見出しと本文を更新します。");

        api.MapDelete("/{headingId:guid}", DeleteHeadingAsync)
            .WithName("DeleteArticleHeading")
            .WithSummary("記事見出しを削除します。");

        api.MapPut("/order", UpdateHeadingOrderAsync)
            .WithName("UpdateArticleHeadingOrder")
            .WithSummary("記事見出しの表示順を更新します。");

        return endpoints;
    }

    private static async Task<IResult> GetHeadingsAsync(
        Guid articleId,
        ClaimsPrincipal principal,
        IArticleHeadingService headingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await headingService.GetHeadingsAsync(actor, articleId, cancellationToken);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> CreateHeadingAsync(
        Guid articleId,
        [FromBody] CreateHeadingRequest request,
        ClaimsPrincipal principal,
        IArticleHeadingService headingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await headingService.CreateHeadingAsync(
            new CreateArticleHeadingCommand(
                actor,
                articleId,
                request.ParentId,
                request.Level,
                request.Title,
                request.InsertAfterHeadingId,
                request.TargetLength,
                request.UseWebSearch),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Created($"/api/articles/{articleId}/headings/{result.Value.Id}", result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateHeadingAsync(
        Guid articleId,
        Guid headingId,
        [FromBody] UpdateHeadingRequest request,
        ClaimsPrincipal principal,
        IArticleHeadingService headingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await headingService.UpdateHeadingAsync(
            new UpdateArticleHeadingCommand(
                actor,
                articleId,
                headingId,
                request.Title,
                request.Body,
                request.TargetLength,
                request.UseWebSearch,
                request.RowVersion),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> DeleteHeadingAsync(
        Guid articleId,
        Guid headingId,
        ClaimsPrincipal principal,
        IArticleHeadingService headingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await headingService.DeleteHeadingAsync(actor, articleId, headingId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> UpdateHeadingOrderAsync(
        Guid articleId,
        [FromBody] UpdateHeadingOrderRequest request,
        ClaimsPrincipal principal,
        IArticleHeadingService headingService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await headingService.UpdateHeadingOrderAsync(
            new UpdateArticleHeadingOrderCommand(
                actor,
                articleId,
                request.Items ?? []),
            cancellationToken);

        return result.Succeeded
            ? Results.Ok(new { updated = true })
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static ArticleActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new ArticleActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private static IResult ToProblemResult(
        ArticleServiceError error,
        IReadOnlyList<ArticleValidationError> validationErrors)
    {
        return error switch
        {
            ArticleServiceError.ValidationFailed => Results.ValidationProblem(
                validationErrors
                    .GroupBy(item => item.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Message).ToArray())),
            ArticleServiceError.NotFound => Results.NotFound(),
            ArticleServiceError.ConflictGeneratingHeading => Results.Problem(
                title: "Conflict",
                detail: "A queued or running generation job exists for this heading.",
                statusCode: StatusCodes.Status409Conflict),
            ArticleServiceError.ConcurrencyConflict => Results.Problem(
                title: "Conflict",
                detail: "The heading was updated by another request.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Heading operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private sealed record CreateHeadingRequest(
        Guid? ParentId,
        int Level,
        string Title,
        Guid? InsertAfterHeadingId,
        int? TargetLength,
        bool UseWebSearch);

    private sealed record UpdateHeadingRequest(
        string Title,
        string? Body,
        int? TargetLength,
        bool UseWebSearch,
        string? RowVersion);

    private sealed record UpdateHeadingOrderRequest(IReadOnlyList<ArticleHeadingOrderItem>? Items);
}
