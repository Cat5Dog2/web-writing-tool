using System.Text.Json;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class RewriteJobHandler(
    ApplicationDbContext dbContext,
    IAiTextGenerationClient aiClient,
    IOptions<GeminiOptions> geminiOptions,
    RewritePromptBuilder promptBuilder)
    : AiGenerationJobHandlerBase(dbContext, aiClient, geminiOptions), IJobHandler
{
    public JobType JobType => JobType.Rewrite;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<RewritePayload>(job);
        var articleId = payload.ArticleId == Guid.Empty
            ? job.ArticleId ?? Guid.Empty
            : payload.ArticleId;
        var headingId = payload.HeadingId == Guid.Empty
            ? job.HeadingId ?? Guid.Empty
            : payload.HeadingId;
        var article = await GetArticleAsync(articleId, job.UserId, cancellationToken);
        var headings = await GetHeadingsAsync(article.Id, cancellationToken);
        var heading = headings.FirstOrDefault(item => item.Id == headingId);
        if (heading is null)
        {
            throw new JobExecutionException(
                JobErrorCodes.NotFound,
                "見出しが見つかりません。");
        }

        if (string.IsNullOrWhiteSpace(heading.Body))
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "リライト対象の本文がありません。");
        }

        var normalizedPayload = payload with { Operation = NormalizeOperation(payload.Operation) };
        var model = ResolveModel(normalizedPayload.GenerationModel, article);
        var prompt = promptBuilder.Build(
            CreatePromptContext(article, headings),
            ToHeadingPromptContext(heading),
            normalizedPayload);
        var operation = normalizedPayload.Operation;

        heading.Status = HeadingStatus.Generating;
        await DbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await GenerateAsync(operation, model, prompt, temperature: 0.4, cancellationToken);
            var body = EnsureGeneratedMarkdown(result.Text);
            heading.Body = body;
            heading.ActualLength = body.Length;
            heading.Status = HeadingStatus.Generated;
            article.Body = BuildArticleBody(headings);
            if (headings.All(item => item.Status == HeadingStatus.Generated))
            {
                article.Status = ArticleStatus.Completed;
                article.CompletedAt ??= DateTimeOffset.UtcNow;
            }

            AddSuccessAccounting(job, article, operation, prompt, result);
            await DbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingId = heading.Id,
                operation
            }));
        }
        catch (ExternalIntegrationException ex)
        {
            heading.Status = HeadingStatus.Failed;
            AddFailureLog(job, article, operation, model, prompt, ex.ErrorCode);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToJobExecutionException(ex);
        }
        catch (JsonException ex)
        {
            heading.Status = HeadingStatus.Failed;
            AddFailureLog(job, article, operation, model, prompt, JobErrorCodes.ExternalBadResponse);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToBadResponseException(ex);
        }
    }

    private static string NormalizeOperation(string operation)
    {
        return operation.Trim() switch
        {
            AiOperations.Summarize => AiOperations.Summarize,
            AiOperations.Expand => AiOperations.Expand,
            AiOperations.Refresh => AiOperations.Refresh,
            _ => AiOperations.Rewrite
        };
    }
}
