using System.Text.Json;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class TitleGenerationJobHandler(
    ApplicationDbContext dbContext,
    IAiTextGenerationClient aiClient,
    IOptions<GeminiOptions> geminiOptions,
    TitleGenerationPromptBuilder promptBuilder)
    : AiGenerationJobHandlerBase(dbContext, aiClient, geminiOptions), IJobHandler
{
    public JobType JobType => JobType.TitleGeneration;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<TitleGenerationPayload>(job);
        var articleId = payload.ArticleId == Guid.Empty
            ? job.ArticleId ?? Guid.Empty
            : payload.ArticleId;
        var article = await GetArticleAsync(articleId, job.UserId, cancellationToken);
        var model = ResolveModel(payload.GenerationModel, article);
        var prompt = promptBuilder.Build(CreatePromptContext(article, []), payload);
        var operation = AiOperations.TitleGeneration;

        try
        {
            var result = await GenerateAsync(operation, model, prompt, temperature: 0.7, cancellationToken);
            var candidates = TitleCandidateParser.Parse(result.Text, payload.CandidateCount ?? 5);
            if (candidates.Count == 0)
            {
                throw new JsonException("No title candidates returned.");
            }

            AddSuccessAccounting(job, article, operation, prompt, result);
            await DbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                candidates
            }));
        }
        catch (ExternalIntegrationException ex)
        {
            AddFailureLog(job, article, operation, model, prompt, ex.ErrorCode);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToJobExecutionException(ex);
        }
        catch (JsonException ex)
        {
            AddFailureLog(job, article, operation, model, prompt, JobErrorCodes.ExternalBadResponse);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToBadResponseException(ex);
        }
    }
}
