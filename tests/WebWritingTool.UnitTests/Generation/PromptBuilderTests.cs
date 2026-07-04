using System.Text.Json;
using WebWritingTool.Application.Generation;

namespace WebWritingTool.UnitTests.Generation;

public class PromptBuilderTests
{
    [Fact]
    public void TitleGenerationPrompt_IncludesWritingProfileAsStyleContext()
    {
        var article = CreateArticlePromptContext();
        var builder = new TitleGenerationPromptBuilder();

        var prompt = builder.Build(
            article,
            new TitleGenerationPayload(
                article.ArticleId,
                null,
                "gemini-3.5-flash",
                5,
                "Ai",
                null,
                null,
                null));

        Assert.Contains("サイト別ライティング設定は文体コンテキスト", prompt.SystemInstruction);
        Assert.Contains("編集長プロフィール", prompt.SystemInstruction);
        Assert.Contains("親しみやすい案内役", prompt.SystemInstruction);
        Assert.Contains("20代から40代の読者", prompt.SystemInstruction);
        Assert.DoesNotContain("編集長プロフィール", prompt.UserPrompt);
    }

    [Fact]
    public void PromptHashCalculator_ReturnsStableHashForSameInputs()
    {
        var first = PromptHashCalculator.Compute("system\r\n", "user");
        var second = PromptHashCalculator.Compute("system\n", "user");
        var changed = PromptHashCalculator.Compute("system\n", "changed");

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void TitleCandidateParser_ParsesCandidatesAndRemovesDuplicates()
    {
        const string response = """
            {
              "candidates": [
                { "title": "タイトルA", "reason": "SEO" },
                { "title": "タイトルA", "reason": "重複" },
                { "title": "タイトルB" }
              ]
            }
            """;

        var candidates = TitleCandidateParser.Parse(response, maxCount: 5);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("タイトルA", candidates[0].Title);
        Assert.Equal("SEO", candidates[0].Reason);
        Assert.Equal("タイトルB", candidates[1].Title);
    }

    [Fact]
    public void OutlineGenerationParser_ParsesH2AndH3HierarchyAndMetaDescription()
    {
        const string response = """
            {
              "metaDescription": "記事の概要を説明するメタディスクリプション。",
              "headings": [
                {
                  "level": 2,
                  "title": "概要",
                  "targetLength": 800,
                  "children": [
                    { "level": 3, "title": "背景", "targetLength": 400 }
                  ]
                }
              ]
            }
            """;

        var outline = OutlineGenerationParser.Parse(response);

        Assert.Equal("記事の概要を説明するメタディスクリプション。", outline.MetaDescription);

        var h2 = Assert.Single(outline.Headings);
        Assert.Equal(2, h2.Level);
        Assert.Equal("概要", h2.Title);
        Assert.Equal(800, h2.TargetLength);

        var h3 = Assert.Single(h2.Children);
        Assert.Equal(3, h3.Level);
        Assert.Equal("背景", h3.Title);
        Assert.Equal(400, h3.TargetLength);
    }

    private static ArticlePromptContext CreateArticlePromptContext()
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            siteName = "The Mind Journal",
            siteAdminProfile = "編集長プロフィール",
            writingCharacter = "親しみやすい案内役",
            readerPersona = "20代から40代の読者"
        });

        return new ArticlePromptContext(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "AIライティング",
            "AIライティング入門",
            "Friendly",
            "AI SEO",
            "記事 作成",
            null,
            null,
            null,
            "Ai",
            SearchMode: false,
            IsDomesticOnly: true,
            StrictMode: false,
            TopicRisk: null,
            snapshot,
            []);
    }
}
