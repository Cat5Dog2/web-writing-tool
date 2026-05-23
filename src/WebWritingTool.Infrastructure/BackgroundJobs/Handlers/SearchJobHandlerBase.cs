using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs.Handlers;

public abstract class SearchJobHandlerBase(
    ApplicationDbContext dbContext,
    SearchCachePolicyResolver cachePolicyResolver,
    ITopicRiskClassifier topicRiskClassifier)
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected ApplicationDbContext DbContext { get; } = dbContext;

    protected SearchCachePolicyResolver CachePolicyResolver { get; } = cachePolicyResolver;

    protected ITopicRiskClassifier TopicRiskClassifier { get; } = topicRiskClassifier;

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

    protected async Task<(Article Article, ArticleHeading? Heading)> GetTargetsAsync(
        Guid articleId,
        Guid? headingId,
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

        ArticleHeading? heading = null;
        if (headingId.HasValue)
        {
            heading = await DbContext.ArticleHeadings.FirstOrDefaultAsync(
                item => item.Id == headingId.Value && item.ArticleId == article.Id,
                cancellationToken);

            if (heading is null)
            {
                throw new JobExecutionException(
                    JobErrorCodes.NotFound,
                    "見出しが見つかりません。");
            }
        }

        return (article, heading);
    }

    protected TopicRiskMode ClassifyAndApplyTopicRisk(
        Article article,
        string query,
        string? headingTitle = null)
    {
        var result = TopicRiskClassifier.Classify(
            article.Keyword,
            article.Title,
            article.SuggestedKeywords,
            article.RelatedKeywords,
            article.AdditionalPrompt,
            headingTitle,
            query);

        if (result.Mode == TopicRiskMode.Normal)
        {
            return result.Mode;
        }

        article.StrictMode = true;
        article.TopicRisk = ToPersistedTopicRisk(result.Mode);
        if (result.HumanReviewRequired)
        {
            article.HumanReviewRequired = true;
        }

        return result.Mode;
    }

    protected static JobExecutionException ToJobExecutionException(Exception exception)
    {
        return exception switch
        {
            WebWritingTool.Application.Generation.ExternalIntegrationException external => new JobExecutionException(
                external.ErrorCode,
                external.UserMessage,
                external,
                external.RetryAfter),
            _ => new JobExecutionException(
                JobErrorCodes.UnknownError,
                "検索ジョブに失敗しました。",
                exception)
        };
    }

    protected static string SerializeResult(object result)
    {
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string ToPersistedTopicRisk(TopicRiskMode mode)
    {
        return mode switch
        {
            TopicRiskMode.Strict => "strict",
            TopicRiskMode.ComplianceStrict => "compliance_strict",
            _ => "normal"
        };
    }
}
