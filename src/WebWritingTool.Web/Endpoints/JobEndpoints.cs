using System.Security.Claims;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Web.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/jobs")
            .RequireAuthorization()
            .WithTags("Jobs");

        api.MapGet("/{jobId:guid}", GetJobAsync)
            .WithName("GetJob")
            .WithSummary("ジョブ状態を取得します。");

        api.MapPost("/{jobId:guid}/cancel", CancelJobAsync)
            .WithName("CancelJob")
            .WithSummary("Queuedジョブをキャンセルします。");

        api.MapPost("/{jobId:guid}/retry", RetryJobAsync)
            .WithName("RetryJob")
            .WithSummary("Failedジョブを再実行します。");

        return endpoints;
    }

    private static async Task<IResult> GetJobAsync(
        Guid jobId,
        ClaimsPrincipal principal,
        IJobQueryService jobQueryService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = await jobQueryService.GetAsync(actor, jobId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> CancelJobAsync(
        Guid jobId,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await jobCommandService.CancelAsync(actor, jobId, cancellationToken);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static async Task<IResult> RetryJobAsync(
        Guid jobId,
        ClaimsPrincipal principal,
        IJobCommandService jobCommandService,
        CancellationToken cancellationToken)
    {
        var actor = GetActor(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await jobCommandService.RetryAsync(actor, jobId, cancellationToken);
        return result.Succeeded && result.Value is not null
            ? Results.Accepted(result.Value.StatusUrl, result.Value)
            : ToProblemResult(result.Error, result.ValidationErrors);
    }

    private static JobActor? GetActor(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : new JobActor(userId, principal.IsInRole(ApplicationRoles.Admin));
    }

    private static IResult ToProblemResult(
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
            JobServiceError.JobNotCancelable => Results.Problem(
                title: "Conflict",
                detail: "Only queued jobs can be canceled.",
                statusCode: StatusCodes.Status409Conflict),
            JobServiceError.JobNotRetryable => Results.Problem(
                title: "Conflict",
                detail: "Only failed jobs with retryable errors can be retried.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                title: "Bad Request",
                detail: "Job operation failed.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }
}
