using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Domain.Ai;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Usage;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public abstract class AiGenerationJobHandlerBase(
    ApplicationDbContext dbContext,
    IAiTextGenerationClient aiClient,
    IOptions<GeminiOptions> geminiOptions)
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected ApplicationDbContext DbContext { get; } = dbContext;

    protected IAiTextGenerationClient AiClient { get; } = aiClient;

    protected GeminiOptions GeminiOptions { get; } = geminiOptions.Value;

    protected TPayload ReadPayload<TPayload>(LeasedJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<TPayload>(job.PayloadJson, JsonOptions)
                ?? throw new JsonException("Payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new JobExecutionException(
                JobErrorCodes.ValidationError,
                "ジョブPayloadが不正です。",
                ex);
        }
    }

    protected async Task<Article> GetArticleAsync(
        Guid articleId,
        string userId,
        CancellationToken cancellationToken)
    {
        var article = await DbContext.Articles.FirstOrDefaultAsync(
            item => item.Id == articleId && item.UserId == userId,
            cancellationToken);

        if (article is null)
        {
            throw new JobExecutionException(
                JobErrorCodes.NotFound,
                "記事が見つかりません。");
        }

        return article;
    }

    protected async Task<List<ArticleHeading>> GetHeadingsAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        return await DbContext.ArticleHeadings
            .Where(heading => heading.ArticleId == articleId)
            .OrderBy(heading => heading.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    protected ArticlePromptContext CreatePromptContext(
        Article article,
        IReadOnlyList<ArticleHeading> headings)
    {
        return new ArticlePromptContext(
            article.Id,
            article.Keyword,
            article.Title,
            article.Tone,
            article.SuggestedKeywords,
            article.RelatedKeywords,
            article.LearningType,
            article.LearningText,
            article.AdditionalPrompt,
            article.OutlineMethod,
            article.SearchMode,
            article.IsDomesticOnly,
            article.StrictMode,
            article.TopicRisk,
            article.WritingProfileSnapshotJson,
            headings
                .OrderBy(heading => heading.DisplayOrder)
                .Select(ToHeadingPromptContext)
                .ToArray());
    }

    protected static HeadingPromptContext ToHeadingPromptContext(ArticleHeading heading)
    {
        return new HeadingPromptContext(
            heading.Id,
            heading.ParentId,
            heading.Level,
            heading.Title,
            heading.Body,
            heading.DisplayOrder,
            heading.TargetLength);
    }

    protected string ResolveModel(string? requestedModel, Article article)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(article.GenerationModel))
        {
            return article.GenerationModel.Trim();
        }

        return GeminiOptions.Model;
    }

    protected Task<AiTextGenerationResult> GenerateAsync(
        string operation,
        string model,
        PromptDocument prompt,
        double? temperature,
        CancellationToken cancellationToken)
    {
        return AiClient.GenerateAsync(
            new AiTextGenerationRequest(
                AiProviders.Gemini,
                model,
                operation,
                prompt.SystemInstruction,
                prompt.UserPrompt,
                null,
                temperature,
                []),
            cancellationToken);
    }

    protected void AddSuccessAccounting(
        LeasedJob job,
        Article article,
        string operation,
        PromptDocument prompt,
        AiTextGenerationResult result)
    {
        var now = DateTimeOffset.UtcNow;
        var usageChars = result.PromptChars + result.OutputChars;

        DbContext.AiGenerationLogs.Add(new AiGenerationLog
        {
            UserId = job.UserId,
            ArticleId = article.Id,
            JobId = job.Id,
            Provider = result.Provider,
            Model = result.Model,
            Operation = operation,
            PromptHash = prompt.PromptHash,
            PromptChars = result.PromptChars,
            OutputChars = result.OutputChars,
            UsageChars = usageChars,
            Succeeded = true,
            CreatedAt = now
        });

        DbContext.UsageLedgers.Add(new UsageLedger
        {
            UserId = job.UserId,
            ArticleId = article.Id,
            JobId = job.Id,
            Provider = result.Provider,
            Model = result.Model,
            Operation = operation,
            PromptChars = result.PromptChars,
            OutputChars = result.OutputChars,
            UsageChars = usageChars,
            OccurredAt = now
        });
    }

    protected void AddFailureLog(
        LeasedJob job,
        Article article,
        string operation,
        string model,
        PromptDocument prompt,
        string errorCode)
    {
        DbContext.AiGenerationLogs.Add(new AiGenerationLog
        {
            UserId = job.UserId,
            ArticleId = article.Id,
            JobId = job.Id,
            Provider = AiProviders.Gemini,
            Model = model,
            Operation = operation,
            PromptHash = prompt.PromptHash,
            PromptChars = prompt.PromptChars,
            OutputChars = 0,
            UsageChars = prompt.PromptChars,
            Succeeded = false,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    protected static JobExecutionException ToJobExecutionException(ExternalIntegrationException exception)
    {
        return new JobExecutionException(
            exception.ErrorCode,
            exception.UserMessage,
            exception,
            exception.RetryAfter);
    }

    protected static JobExecutionException ToBadResponseException(Exception exception)
    {
        return new JobExecutionException(
            JobErrorCodes.ExternalBadResponse,
            "AI生成結果の形式が不正です。",
            exception);
    }

    protected static string SerializeResult(object result)
    {
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    protected static string EnsureGeneratedMarkdown(string text)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Generated body is empty or contains unsafe markup.");
        }

        return normalized;
    }

    protected static string BuildArticleBody(IReadOnlyList<ArticleHeading> headings)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            headings
                .OrderBy(heading => heading.DisplayOrder)
                .Select(heading =>
                {
                    var prefix = heading.Level == 3 ? "###" : "##";
                    var body = string.IsNullOrWhiteSpace(heading.Body)
                        ? string.Empty
                        : Environment.NewLine + Environment.NewLine + heading.Body.Trim();
                    return $"{prefix} {heading.Title}{body}";
                }));
    }
}
