using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Web.Security;

namespace WebWritingTool.Web.Endpoints;

public static class ArticleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapArticleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/articles")
            .RequireAuthorization()
            .RequireCsrfToken()
            .WithTags("Articles");

        api.MapGet("", GetArticlesAsync)
            .WithName("GetArticles")
            .WithSummary("記事一覧を取得します。");

        api.MapPost("", CreateArticleAsync)
            .WithName("CreateArticle")
            .WithSummary("記事を作成します。");

        api.MapPost("/bulk", BulkCreateArticlesAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.BulkArticleRegistration)
            .WithName("BulkCreateArticles")
            .WithSummary("複数の記事を一括作成します。");

        api.MapGet("/{articleId:guid}", GetArticleAsync)
            .WithName("GetArticle")
            .WithSummary("記事詳細を取得します。");

        api.MapPut("/{articleId:guid}", UpdateArticleAsync)
            .WithName("UpdateArticle")
            .WithSummary("記事を更新します。");

        api.MapDelete("/{articleId:guid}", DeleteArticleAsync)
            .WithName("DeleteArticle")
            .WithSummary("記事を論理削除します。");

        api.MapPost("/{articleId:guid}/generation/title-candidates", GenerateTitleCandidatesAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("GenerateTitleCandidates")
            .WithSummary("タイトル候補生成ジョブを登録します。");

        api.MapPost("/{articleId:guid}/generation/outline", GenerateOutlineAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("GenerateOutline")
            .WithSummary("見出し構成生成ジョブを登録します。");

        api.MapPost("/{articleId:guid}/generation/headings/{headingId:guid}/body", GenerateHeadingBodyAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("GenerateHeadingBody")
            .WithSummary("見出し本文生成ジョブを登録します。");

        api.MapPost("/{articleId:guid}/generation/body", GenerateArticleBodyAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("GenerateArticleBody")
            .WithSummary("本文一括生成ジョブを登録します。");

        api.MapPost("/{articleId:guid}/generation/headings/{headingId:guid}/rewrite", RewriteHeadingBodyAsync)
            .RequireRateLimiting(SecurityRateLimitPolicyNames.JobRegistration)
            .WithName("RewriteHeadingBody")
            .WithSummary("見出し本文リライトジョブを登録します。");

        api.MapPost("/{articleId:guid}/generation/html", ConvertArticleHtmlAsync)
            .WithName("ConvertArticleHtml")
            .WithSummary("記事本文をHTMLへ変換します。");

        return endpoints;
    }

    private static async Task<IResult> GetArticlesAsync(
        [AsParameters] ArticleListRequest request,
        ClaimsPrincipal principal,
        IArticleQueryService articleQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await articleQueryService.SearchAsync(
            actor,
            new ArticleListQuery(
                request.Page ?? 1,
                request.PageSize ?? 10,
                request.Q,
                ArticleInputNormalizer.SplitTags(request.Tags),
                request.Status,
                request.CreatedFrom,
                request.CreatedTo,
                request.Sort,
                request.Direction),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateArticleAsync(
        [FromBody] CreateArticleRequest request,
        ClaimsPrincipal principal,
        IArticleCommandService articleCommandService,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var result = await articleCommandService.CreateAsync(
            new CreateArticleCommand(
                userId,
                request.Keyword,
                request.Title,
                request.GenerateImage,
                request.H2Count,
                request.H3Count,
                request.Tone,
                request.Tags ?? [],
                request.Memo,
                request.SuggestedKeywords,
                request.RelatedKeywords,
                request.LearningType,
                request.LearningText,
                request.AdditionalPrompt,
                request.WritingProfileWordpressSiteId,
                request.OutlineMethod,
                request.GenerationModel,
                request.SearchMode,
                request.IsDomesticOnly,
                request.NotificationMode),
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            return ToProblemResult(result.Error, result.ValidationErrors);
        }

        return Results.Created(result.Value.DetailUrl, result.Value);
    }

    private static async Task<IResult> BulkCreateArticlesAsync(
        [FromBody] BulkCreateArticlesRequest request,
        ClaimsPrincipal principal,
        IArticleCommandService articleCommandService,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var result = await articleCommandService.BulkCreateAsync(
            new BulkCreateArticlesCommand(
                userId,
                request.Lines ?? [],
                request.H2Count,
                request.H3Count,
                request.IsDomesticOnly,
                request.TitleMethod,
                request.OutlineMethod,
                request.GenerationModel,
                request.SearchMode,
                request.WritingProfileWordpressSiteId,
                request.AutoPostToWordpress,
                request.AutoPostWordpressSiteId,
                request.AutoPostWordpressCategoryId),
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            return ToProblemResult(result.Error, result.ValidationErrors);
        }

        return Results.Accepted("/api/articles", result.Value);
    }

    private static async Task<IResult> GetArticleAsync(
        Guid articleId,
        ClaimsPrincipal principal,
        IArticleQueryService articleQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await articleQueryService.GetAsync(actor, articleId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> UpdateArticleAsync(
        Guid articleId,
        [FromBody] UpdateArticleRequest request,
        ClaimsPrincipal principal,
        IArticleCommandService articleCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await articleCommandService.UpdateAsync(
            new UpdateArticleCommand(
                actor,
                articleId,
                request.Keyword,
                request.Title,
                request.Tags ?? [],
                request.Memo,
                request.Tone,
                request.SuggestedKeywords,
                request.RelatedKeywords,
                request.LearningType,
                request.LearningText,
                request.AdditionalPrompt,
                request.MetaDescription,
                request.GenerationModel,
                request.OutlineMethod,
                request.SearchMode,
                request.IsDomesticOnly,
                request.NotificationMode,
                request.WritingProfileWordpressSiteId,
                request.Body,
                request.HtmlBody,
                request.RowVersion),
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            return ToProblemResult(result.Error, result.ValidationErrors);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteArticleAsync(
        Guid articleId,
        ClaimsPrincipal principal,
        IArticleCommandService articleCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await articleCommandService.DeleteAsync(actor, articleId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static Task<IResult> GenerateTitleCandidatesAsync(
        Guid articleId,
        [FromBody] GenerateTitleCandidatesRequest request,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var payload = new TitleGenerationPayload(
            articleId,
            request.Keyword,
            request.GenerationModel,
            request.CandidateCount,
            request.TitleMethod,
            request.SuggestedKeywords,
            request.RelatedKeywords,
            request.AdditionalPrompt);

        return EnqueueGenerationJobAsync(
            principal,
            jobCommandService,
            JobType.TitleGeneration,
            articleId,
            headingId: null,
            payload,
            cancellationToken);
    }

    private static Task<IResult> GenerateOutlineAsync(
        Guid articleId,
        [FromBody] GenerateOutlineRequest request,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var payload = new OutlineGenerationPayload(
            articleId,
            request.Keyword,
            request.Title,
            request.H2Count,
            request.H3Count,
            request.OutlineMethod,
            request.GenerationModel,
            request.SearchMode,
            request.IsDomesticOnly,
            request.Tone,
            request.SuggestedKeywords,
            request.RelatedKeywords,
            request.LearningType,
            request.LearningText,
            request.AdditionalPrompt);

        return EnqueueGenerationJobAsync(
            principal,
            jobCommandService,
            JobType.OutlineGeneration,
            articleId,
            headingId: null,
            payload,
            cancellationToken);
    }

    private static Task<IResult> GenerateHeadingBodyAsync(
        Guid articleId,
        Guid headingId,
        [FromBody] GenerateHeadingBodyRequest request,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var payload = new BodyGenerationPayload(
            articleId,
            headingId,
            Scope: null,
            request.GenerationModel,
            request.TargetLength,
            request.UseWebSearch,
            request.AdditionalPrompt);

        return EnqueueGenerationJobAsync(
            principal,
            jobCommandService,
            JobType.BodyGeneration,
            articleId,
            headingId,
            payload,
            cancellationToken);
    }

    private static Task<IResult> GenerateArticleBodyAsync(
        Guid articleId,
        [FromBody] GenerateArticleBodyRequest request,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var payload = new BodyGenerationPayload(
            articleId,
            HeadingId: null,
            request.Scope,
            request.GenerationModel,
            TargetLength: null,
            request.UseWebSearch,
            request.AdditionalPrompt);

        return EnqueueGenerationJobAsync(
            principal,
            jobCommandService,
            JobType.BodyGeneration,
            articleId,
            headingId: null,
            payload,
            cancellationToken);
    }

    private static Task<IResult> RewriteHeadingBodyAsync(
        Guid articleId,
        Guid headingId,
        [FromBody] RewriteHeadingBodyRequest request,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var payload = new RewritePayload(
            articleId,
            headingId,
            request.Operation,
            request.GenerationModel,
            request.AdditionalPrompt);

        return EnqueueGenerationJobAsync(
            principal,
            jobCommandService,
            JobType.Rewrite,
            articleId,
            headingId,
            payload,
            cancellationToken);
    }

    private static async Task<IResult> ConvertArticleHtmlAsync(
        Guid articleId,
        [FromBody] ConvertArticleHtmlRequest request,
        ClaimsPrincipal principal,
        IArticleContentService articleContentService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await articleContentService.ConvertHtmlAsync(
            new ConvertArticleHtmlCommand(
                actor,
                articleId,
                request.InsertLineBreakAfterPeriod),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> EnqueueGenerationJobAsync<TPayload>(
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        JobType jobType,
        Guid articleId,
        Guid? headingId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var actor = GetJobActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await jobCommandService.EnqueueAsync(
            new EnqueueJobCommand(
                actor,
                jobType,
                articleId,
                headingId,
                JsonSerializer.Serialize(payload, JsonOptions),
                Priority: 0),
            cancellationToken);

        return result.Succeeded && result.Value is not null
            ? Results.Accepted(result.Value.StatusUrl, result.Value)
            : ToJobProblemResult(result.Error, result.ValidationErrors);
    }

    private static ArticleActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new ArticleActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private static JobActor? GetJobActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new JobActor(userId, principal.IsInRole(ApplicationRoles.Admin));
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
            ArticleServiceError.ConflictRunningJob => Results.Problem(
                title: "Conflict",
                detail: "Running jobs exist for this article.",
                statusCode: StatusCodes.Status409Conflict),
            ArticleServiceError.ConcurrencyConflict => Results.Problem(
                title: "Conflict",
                detail: "The article was updated by another request.",
                statusCode: StatusCodes.Status409Conflict),
            ArticleServiceError.RateLimited => Results.Problem(
                title: "RateLimited",
                detail: "Too many requests.",
                statusCode: StatusCodes.Status429TooManyRequests),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Article operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private static IResult ToJobProblemResult(
        JobServiceError error,
        IReadOnlyList<JobValidationError> validationErrors)
    {
        return error switch
        {
            JobServiceError.ValidationFailed => Results.ValidationProblem(
                validationErrors
                    .GroupBy(item => item.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Message).ToArray())),
            JobServiceError.NotFound => Results.NotFound(),
            JobServiceError.RunningJobExists => Results.Problem(
                title: "Conflict",
                detail: "A queued or running job already exists for this target.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Job operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private sealed class ArticleListRequest
    {
        public int? Page { get; init; }

        public int? PageSize { get; init; }

        public string? Q { get; init; }

        public string? Tags { get; init; }

        public string? Status { get; init; }

        public DateOnly? CreatedFrom { get; init; }

        public DateOnly? CreatedTo { get; init; }

        public string? Sort { get; init; }

        public string? Direction { get; init; }
    }

    private sealed record CreateArticleRequest(
        string Keyword,
        string? Title,
        bool GenerateImage,
        int? H2Count,
        int? H3Count,
        string? Tone,
        IReadOnlyList<string>? Tags,
        string? Memo,
        string? SuggestedKeywords,
        string? RelatedKeywords,
        string? LearningType,
        string? LearningText,
        string? AdditionalPrompt,
        Guid? WritingProfileWordpressSiteId,
        string OutlineMethod,
        string GenerationModel,
        bool SearchMode,
        bool IsDomesticOnly,
        string? NotificationMode);

    private sealed record BulkCreateArticlesRequest(
        IReadOnlyList<string>? Lines,
        int? H2Count,
        int? H3Count,
        bool IsDomesticOnly,
        string TitleMethod,
        string OutlineMethod,
        string GenerationModel,
        bool SearchMode,
        Guid? WritingProfileWordpressSiteId,
        bool AutoPostToWordpress,
        Guid? AutoPostWordpressSiteId,
        int? AutoPostWordpressCategoryId);

    private sealed record UpdateArticleRequest(
        string Keyword,
        string Title,
        IReadOnlyList<string>? Tags,
        string? Memo,
        string? Tone,
        string? SuggestedKeywords,
        string? RelatedKeywords,
        string? LearningType,
        string? LearningText,
        string? AdditionalPrompt,
        string? MetaDescription,
        string? GenerationModel,
        string? OutlineMethod,
        bool SearchMode,
        bool IsDomesticOnly,
        string? NotificationMode,
        Guid? WritingProfileWordpressSiteId,
        string? Body,
        string? HtmlBody,
        string? RowVersion);

    private sealed record GenerateTitleCandidatesRequest(
        string? Keyword,
        string? TitleMethod,
        string GenerationModel,
        int? CandidateCount,
        string? SuggestedKeywords,
        string? RelatedKeywords,
        string? AdditionalPrompt);

    private sealed record GenerateOutlineRequest(
        string? Keyword,
        string? Title,
        int? H2Count,
        int? H3Count,
        string OutlineMethod,
        string GenerationModel,
        bool SearchMode,
        bool IsDomesticOnly,
        string? Tone,
        string? SuggestedKeywords,
        string? RelatedKeywords,
        string? LearningType,
        string? LearningText,
        string? AdditionalPrompt);

    private sealed record GenerateHeadingBodyRequest(
        string GenerationModel,
        int? TargetLength,
        bool UseWebSearch,
        string? AdditionalPrompt);

    private sealed record GenerateArticleBodyRequest(
        string Scope,
        string GenerationModel,
        bool UseWebSearch,
        string? AdditionalPrompt);

    private sealed record RewriteHeadingBodyRequest(
        string Operation,
        string GenerationModel,
        string? AdditionalPrompt);

    private sealed record ConvertArticleHtmlRequest(bool InsertLineBreakAfterPeriod);
}
