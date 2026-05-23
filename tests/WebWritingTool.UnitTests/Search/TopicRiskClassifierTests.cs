using System.Text;
using WebWritingTool.Application.Search;

namespace WebWritingTool.UnitTests.Search;

public class TopicRiskClassifierTests
{
    [Theory]
    [InlineData("法律相談の注意点", "legalFinanceHealth")]
    [InlineData("政治ニュースの見方", "politicsSafetyReputation")]
    public void Classify_ComplianceKeywords_ReturnsComplianceStrict(
        string input,
        string expectedCategory)
    {
        var classifier = new TopicRiskClassifier(TopicRiskKeywordDictionary.Default);

        var result = classifier.Classify(input);

        Assert.Equal(TopicRiskMode.ComplianceStrict, result.Mode);
        Assert.True(result.HumanReviewRequired);
        Assert.Equal(expectedCategory, result.MatchedCategory);
    }

    [Fact]
    public void Classify_StrictKeywords_ReturnsStrict()
    {
        var classifier = new TopicRiskClassifier(TopicRiskKeywordDictionary.Default);

        var result = classifier.Classify("Gemini API料金の最新比較");

        Assert.Equal(TopicRiskMode.Strict, result.Mode);
        Assert.False(result.HumanReviewRequired);
    }

    [Fact]
    public void Classify_NoKeyword_ReturnsNormal()
    {
        var classifier = new TopicRiskClassifier(TopicRiskKeywordDictionary.Default);

        var result = classifier.Classify("ブログ記事の書き方");

        Assert.Equal(TopicRiskMode.Normal, result.Mode);
        Assert.False(result.HumanReviewRequired);
    }

    [Fact]
    public void LoadJson_LoadsCustomDictionary()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "strict": {
                "customStrict": [ "独自strict" ]
              },
              "complianceStrict": {
                "customCompliance": [ "独自compliance" ]
              }
            }
            """));
        var dictionary = TopicRiskKeywordDictionaryLoader.LoadJson(stream);
        var classifier = new TopicRiskClassifier(dictionary);

        Assert.Equal(TopicRiskMode.Strict, classifier.Classify("独自strict").Mode);
        Assert.Equal(TopicRiskMode.ComplianceStrict, classifier.Classify("独自compliance").Mode);
    }
}
