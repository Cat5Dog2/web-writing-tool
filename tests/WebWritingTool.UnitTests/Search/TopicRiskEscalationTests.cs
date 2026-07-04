using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;

namespace WebWritingTool.UnitTests.Search;

public class TopicRiskEscalationTests
{
    private readonly TopicRiskClassifier classifier = new(TopicRiskKeywordDictionary.Default);

    [Fact]
    public void ApplyTopicRiskEscalation_MedicalKeyword_SetsComplianceStrictAndHumanReview()
    {
        var article = new Article { Keyword = "薬の副作用と治療" };

        var changed = article.ApplyTopicRiskEscalation(classifier.Classify(article.Keyword));

        Assert.True(changed);
        Assert.Equal("compliance_strict", article.TopicRisk);
        Assert.True(article.StrictMode);
        Assert.True(article.HumanReviewRequired);
    }

    [Fact]
    public void ApplyTopicRiskEscalation_NormalKeyword_LeavesArticleUnchanged()
    {
        var article = new Article { Keyword = "Blazorのフォーム実装方法" };

        var changed = article.ApplyTopicRiskEscalation(classifier.Classify(article.Keyword));

        Assert.False(changed);
        Assert.Null(article.TopicRisk);
        Assert.False(article.HumanReviewRequired);
    }

    [Fact]
    public void ApplyTopicRiskEscalation_StrictThenNormal_DoesNotDowngrade()
    {
        var article = new Article { Keyword = "投資の税金" };
        article.ApplyTopicRiskEscalation(classifier.Classify(article.Keyword));

        var changed = article.ApplyTopicRiskEscalation(classifier.Classify("料金の比較"));

        Assert.False(changed);
        Assert.Equal("compliance_strict", article.TopicRisk);
        Assert.True(article.HumanReviewRequired);
    }

    [Fact]
    public void ApplyTopicRiskEscalation_StrictToComplianceStrict_Escalates()
    {
        var article = new Article { Keyword = "料金の比較" };
        article.ApplyTopicRiskEscalation(classifier.Classify(article.Keyword));
        Assert.Equal("strict", article.TopicRisk);
        Assert.False(article.HumanReviewRequired);

        var changed = article.ApplyTopicRiskEscalation(classifier.Classify("医療の診断"));

        Assert.True(changed);
        Assert.Equal("compliance_strict", article.TopicRisk);
        Assert.True(article.HumanReviewRequired);
    }

    [Fact]
    public void ApplyTopicRiskEscalation_WhenArticleWasReviewed_ClearsReviewState()
    {
        var article = new Article
        {
            Keyword = "料金の比較",
            HumanReviewedAt = DateTimeOffset.UtcNow,
            HumanReviewedByUserId = "reviewer"
        };
        article.ApplyTopicRiskEscalation(classifier.Classify(article.Keyword));

        article.ApplyTopicRiskEscalation(classifier.Classify("医療の診断"));

        Assert.Null(article.HumanReviewedAt);
        Assert.Null(article.HumanReviewedByUserId);
    }
}
