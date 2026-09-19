using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class BodyGenerationJobHandler(
    ApplicationDbContext dbContext,
    IAiTextGenerationClient aiClient,
    IOptions<GeminiOptions> geminiOptions,
    BodyGenerationPromptBuilder promptBuilder,
    IContentRenderingService contentRenderingService,
    IWordpressPostCommandService wordpressPostCommandService,
    IArticleResearchService researchService)
    : AiGenerationJobHandlerBase(dbContext, aiClient, geminiOptions), IJobHandler
{
    public JobType JobType => JobType.BodyGeneration;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<BodyGenerationPayload>(job);
        var articleId = payload.ArticleId == Guid.Empty
            ? job.ArticleId ?? Guid.Empty
            : payload.ArticleId;
        var article = await GetArticleAsync(articleId, job.UserId, cancellationToken);
        var headings = await GetHeadingsAsync(article.Id, cancellationToken);
        var targets = SelectTargets(job, payload, headings);
        if (job.GenerationRunId.HasValue || job.AttemptCount > 1 || job.IsResuming)
            targets = targets.Where(h => h.Status != HeadingStatus.Generated || string.IsNullOrWhiteSpace(h.Body)).ToList();
        if (headings.Count == 0 || targets.Count == 0 && !job.GenerationRunId.HasValue && job.AttemptCount <= 1 && !job.IsResuming)
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "本文生成対象の見出しがありません。");
        }

        var isBatch = targets.Count > 1 || job.HeadingId is null && payload.HeadingId is null;
        if (isBatch)
        {
            article.Status = ArticleStatus.BodyGenerating;
        }

        foreach (var heading in targets)
        {
            heading.Status = HeadingStatus.Generating;
        }

        await DbContext.SaveChangesAsync(cancellationToken);

        var model = ResolveModel(payload.GenerationModel, article);
        var sources = await GetWorkflowSourcesAsync(job, cancellationToken);
        var generatedCount = 0;
        foreach (var heading in targets)
        {
            var prompt = promptBuilder.Build(
                CreatePromptContext(article, headings),
                ToHeadingPromptContext(heading),
                payload);
            var operation = AiOperations.BodyGeneration;

            try
            {
                var useWebSearch = payload.UseWebSearch || heading.UseWebSearch || article.SearchMode;
                var query = heading.SearchQuery ?? article.Keyword;
                var references = sources is not null
                    ? await researchService.GetSelectedReferencesAsync(job.UserId, article.Id, sources, cancellationToken)
                    : await researchService.GetReferencesAsync(job.UserId, article.Id, heading.Id,
                    useWebSearch, query, cancellationToken: cancellationToken);
                prompt = ReferencePromptFormatter.Attach(promptBuilder.Build(CreatePromptContext(article, headings),
                    ToHeadingPromptContext(heading), payload with { UseWebSearch = useWebSearch }), references);
                var result = await GenerateAsync(operation, model, prompt, temperature: 0.5, cancellationToken);
                var body = EnsureGeneratedMarkdown(result.Text);
                heading.Body = body;
                heading.ActualLength = body.Length;
                heading.Status = HeadingStatus.Generated;
                article.InvalidateHumanReview();
                AddSuccessAccounting(job, article, operation, prompt, result);
                generatedCount++;
                await DbContext.SaveChangesAsync(cancellationToken);
                var progress = headings.Count(h => h.Status == HeadingStatus.Generated) * 100 / headings.Count;
                await DbContext.ArticleGenerationJobs.Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Progress, Math.Min(99, progress)), cancellationToken);
            }
            catch (ExternalIntegrationException ex)
            {
                heading.Status = HeadingStatus.Failed;
                if (isBatch)
                {
                    article.Status = ArticleStatus.Failed;
                }

                AddFailureLog(job, article, operation, model, prompt, ex.ErrorCode);
                await DbContext.SaveChangesAsync(CancellationToken.None);
                throw ToJobExecutionException(ex);
            }
            catch (JsonException ex)
            {
                heading.Status = HeadingStatus.Failed;
                if (isBatch)
                {
                    article.Status = ArticleStatus.Failed;
                }

                AddFailureLog(job, article, operation, model, prompt, JobErrorCodes.ExternalBadResponse);
                await DbContext.SaveChangesAsync(CancellationToken.None);
                throw ToBadResponseException(ex);
            }
        }

        article.Body = BuildArticleBody(headings);
        if (headings.All(heading => heading.Status == HeadingStatus.Generated))
        {
            article.Status = ArticleStatus.Completed;
            article.CompletedAt ??= DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(article.Body))
            {
                article.HtmlBody = contentRenderingService.ConvertMarkdownToHtml(
                    article.Body,
                    insertLineBreakAfterPeriod: false);
            }
        }

        await DbContext.SaveChangesAsync(cancellationToken);
        await wordpressPostCommandService.QueueAutoPostIfReadyAsync(
            job.UserId,
            article.Id,
            cancellationToken);

        return new JobExecutionResult(SerializeResult(new
        {
            articleId = article.Id,
            generatedHeadingCount = generatedCount
        }));
    }

    private static List<ArticleHeading> SelectTargets(
        LeasedJob job,
        BodyGenerationPayload payload,
        IReadOnlyList<ArticleHeading> headings)
    {
        var headingId = payload.HeadingId ?? job.HeadingId;
        if (headingId.HasValue)
        {
            return headings.Where(heading => heading.Id == headingId.Value).ToList();
        }

        var scope = string.IsNullOrWhiteSpace(payload.Scope) ? "All" : payload.Scope.Trim();
        var query = headings.AsEnumerable();
        if (string.Equals(scope, "MissingOnly", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(heading => string.IsNullOrWhiteSpace(heading.Body));
        }
        else if (string.Equals(scope, "UnderH3", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(heading => heading.Level == 3);
        }

        return query.OrderBy(heading => heading.DisplayOrder).ToList();
    }
}
