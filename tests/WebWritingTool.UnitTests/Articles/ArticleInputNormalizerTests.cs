using WebWritingTool.Application.Articles;

namespace WebWritingTool.UnitTests.Articles;

public class ArticleInputNormalizerTests
{
    [Fact]
    public void NormalizeTags_TrimsAndRemovesDuplicateTags()
    {
        var tags = ArticleInputNormalizer.NormalizeTags([" SEO ", "seo", "", "Blazor", " Blazor "]);

        Assert.Equal(["SEO", "Blazor"], tags);
    }

    [Fact]
    public void ParseBulkLine_WithKeywordOnly_ReturnsKeyword()
    {
        var result = ArticleInputNormalizer.ParseBulkLine("  クラヲアクト  ", 3);

        Assert.NotNull(result.ArticleLine);
        Assert.Null(result.RejectedLine);
        Assert.Equal(3, result.ArticleLine.LineNumber);
        Assert.Equal("クラヲアクト", result.ArticleLine.Keyword);
        Assert.Null(result.ArticleLine.Title);
    }

    [Fact]
    public void ParseBulkLine_WithKeywordAndTitle_ReturnsKeywordAndTitle()
    {
        var result = ArticleInputNormalizer.ParseBulkLine("クラヲアクト|魅力を徹底解説", 1);

        Assert.NotNull(result.ArticleLine);
        Assert.Equal("クラヲアクト", result.ArticleLine.Keyword);
        Assert.Equal("魅力を徹底解説", result.ArticleLine.Title);
    }

    [Fact]
    public void ParseBulkLine_WithEmptyLine_ReturnsRejectedLine()
    {
        var result = ArticleInputNormalizer.ParseBulkLine("   ", 2);

        Assert.Null(result.ArticleLine);
        Assert.NotNull(result.RejectedLine);
        Assert.Equal(2, result.RejectedLine.LineNumber);
        Assert.Contains("空", result.RejectedLine.Reason);
    }

    [Fact]
    public void ParseBulkLine_WithTooManySeparators_ReturnsRejectedLine()
    {
        var result = ArticleInputNormalizer.ParseBulkLine("キーワード|タイトル|余分", 4);

        Assert.Null(result.ArticleLine);
        Assert.NotNull(result.RejectedLine);
        Assert.Equal(4, result.RejectedLine.LineNumber);
        Assert.Contains("キーワード|タイトル", result.RejectedLine.Reason);
    }
}
