using System.Text.Json;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public sealed class OutlineGenerationJobHandler(
    ApplicationDbContext dbContext,
    IAiTextGenerationClient aiClient,
    IOptions<GeminiOptions> geminiOptions,
    OutlineGenerationPromptBuilder promptBuilder,
    ITopicRiskClassifier topicRiskClassifier)
    : AiGenerationJobHandlerBase(dbContext, aiClient, geminiOptions), IJobHandler
{
    private const int MetaDescriptionMaxLength = 320;

    public JobType JobType => JobType.OutlineGeneration;

    public async Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload<OutlineGenerationPayload>(job);
        var articleId = payload.ArticleId == Guid.Empty
            ? job.ArticleId ?? Guid.Empty
            : payload.ArticleId;
        var article = await GetArticleAsync(articleId, job.UserId, cancellationToken);
        var existingHeadings = await GetHeadingsAsync(article.Id, cancellationToken);
        var model = ResolveModel(payload.GenerationModel, article);
        var prompt = promptBuilder.Build(CreatePromptContext(article, existingHeadings), payload);
        var operation = AiOperations.OutlineGeneration;

        article.Status = ArticleStatus.OutlineGenerating;
        await DbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await GenerateAsync(operation, model, prompt, temperature: 0.4, cancellationToken);
            var outline = OutlineGenerationParser.Parse(result.Text);
            if (outline.Headings.Count == 0)
            {
                throw new JsonException("No outline headings returned.");
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var heading in existingHeadings)
            {
                heading.DeletedAt = now;
            }

            var addedHeadings = AddHeadings(article.Id, outline.Headings);
            ApplyMetaDescription(article, outline.MetaDescription);
            ApplyTopicRisk(article, outline.Headings);
            article.InvalidateHumanReview();
            article.Status = ArticleStatus.OutlineReady;
            AddSuccessAccounting(job, article, operation, prompt, result);
            await DbContext.SaveChangesAsync(cancellationToken);

            return new JobExecutionResult(SerializeResult(new
            {
                articleId = article.Id,
                headingCount = addedHeadings.Count
            }));
        }
        catch (ExternalIntegrationException ex)
        {
            article.Status = ArticleStatus.Failed;
            AddFailureLog(job, article, operation, model, prompt, ex.ErrorCode);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToJobExecutionException(ex);
        }
        catch (JsonException ex)
        {
            article.Status = ArticleStatus.Failed;
            AddFailureLog(job, article, operation, model, prompt, JobErrorCodes.ExternalBadResponse);
            await DbContext.SaveChangesAsync(CancellationToken.None);
            throw ToBadResponseException(ex);
        }
    }

    private static void ApplyMetaDescription(Article article, string? metaDescription)
    {
        var normalized = metaDescription?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        article.MetaDescription = normalized.Length > MetaDescriptionMaxLength
            ? normalized[..MetaDescriptionMaxLength]
            : normalized;
    }

    private void ApplyTopicRisk(Article article, IReadOnlyList<OutlineHeadingItem> outline)
    {
        var headingTitles = string.Join(
            " ",
            outline.SelectMany(heading => new[] { heading.Title }
                .Concat(heading.Children.Select(child => child.Title))));

        var classification = topicRiskClassifier.Classify(
            article.Keyword,
            article.Title,
            article.SuggestedKeywords,
            article.RelatedKeywords,
            article.AdditionalPrompt,
            headingTitles);

        article.ApplyTopicRiskEscalation(classification);
    }

    private List<ArticleHeading> AddHeadings(
        Guid articleId,
        IReadOnlyList<OutlineHeadingItem> outline)
    {
        var displayOrder = 10;
        var added = new List<ArticleHeading>();

        foreach (var item in outline)
        {
            var h2 = CreateHeading(articleId, parentId: null, level: 2, item.Title, displayOrder, item.TargetLength);
            displayOrder += 10;
            DbContext.ArticleHeadings.Add(h2);
            added.Add(h2);

            foreach (var child in item.Children)
            {
                var h3 = CreateHeading(articleId, h2.Id, 3, child.Title, displayOrder, child.TargetLength);
                displayOrder += 10;
                DbContext.ArticleHeadings.Add(h3);
                added.Add(h3);
            }
        }

        return added;
    }

    private static ArticleHeading CreateHeading(
        Guid articleId,
        Guid? parentId,
        int level,
        string title,
        int displayOrder,
        int? targetLength)
    {
        return new ArticleHeading
        {
            ArticleId = articleId,
            ParentId = parentId,
            Level = level,
            Title = title.Trim(),
            DisplayOrder = displayOrder,
            TargetLength = targetLength,
            Status = HeadingStatus.Pending
        };
    }
}
