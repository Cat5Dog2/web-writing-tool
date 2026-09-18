using WebWritingTool.Application.Generation;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.UnitTests.Generation;

public class DummyTextGenerationClientTests
{
    private readonly DummyTextGenerationClient client = new();
    private static readonly ArticlePromptContext Article = new(
        Guid.NewGuid(), "家庭菜園 \"入門\"", null, null, null, null, null, null,
        null, "Ai", true, true, false, "normal", null, []);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    public async Task Titles_RespectCandidateLimitsAndPreserveKeyword(int requested, int expected)
    {
        var prompt = new TitleGenerationPromptBuilder().Build(Article,
            new TitleGenerationPayload(Article.ArticleId, null, null, requested, null, null, null, null));
        var result = await client.GenerateAsync(Request(AiOperations.TitleGeneration, prompt));
        var titles = TitleCandidateParser.Parse(result.Text, 20);
        Assert.Equal(expected, titles.Count);
        Assert.All(titles, title => Assert.Contains(Article.Keyword, title.Title));
        Assert.Equal("Dummy", result.Provider);
        Assert.Equal("dummy-text", result.Model);
        Assert.Equal(result.Text.Length, result.OutputChars);
        Assert.Equal(prompt.PromptChars, result.PromptChars);
    }

    [Theory]
    [InlineData(1, 0, 1, 0)]
    [InlineData(2, 3, 2, 3)]
    [InlineData(0, -1, 1, 0)]
    [InlineData(21, 61, 20, 60)]
    public async Task Outline_DistributesH3UnderH2WithinLimits(int h2, int h3, int expectedH2, int expectedH3)
    {
        var prompt = new OutlineGenerationPromptBuilder().Build(Article,
            new OutlineGenerationPayload(Article.ArticleId, null, null, h2, h3, null,
                null, null, null, null, null, null, null, null, null));
        var result = await client.GenerateAsync(Request(AiOperations.OutlineGeneration, prompt));
        var outline = OutlineGenerationParser.Parse(result.Text);
        Assert.Equal(expectedH2, outline.Headings.Count);
        Assert.Equal(expectedH3, outline.Headings.Sum(heading => heading.Children.Count));
        Assert.All(outline.Headings, heading =>
        {
            Assert.Equal(2, heading.Level);
            Assert.Contains(Article.Keyword, heading.Title);
            Assert.All(heading.Children, child => Assert.Equal(3, child.Level));
        });
    }

    [Theory]
    [InlineData(AiOperations.BodyGeneration, "本文生成")]
    [InlineData(AiOperations.Rewrite, "リライト")]
    [InlineData(AiOperations.Summarize, "要約")]
    [InlineData(AiOperations.Expand, "長文化")]
    [InlineData(AiOperations.Refresh, "更新")]
    public async Task BodyOperations_ReturnMarkedMarkdownWithoutLeakingInstructions(string operation, string label)
    {
        var heading = new HeadingPromptContext(Guid.NewGuid(), null, 2, "道具の準備", "既存の本文", 0, 400);
        var article = Article with { Keyword = "家庭菜園", AdditionalPrompt = "内部の追加指示" };
        var prompt = operation == AiOperations.BodyGeneration
            ? new BodyGenerationPromptBuilder().Build(article, heading,
                new BodyGenerationPayload(article.ArticleId, heading.Id, null, null, null, true, null))
            : new RewritePromptBuilder().Build(article, heading,
                new RewritePayload(article.ArticleId, heading.Id, operation, null, "内部の追加指示"));
        var result = await client.GenerateAsync(Request(operation, prompt));
        Assert.Contains($"【ダミー：{label}】", result.Text);
        Assert.Contains("家庭菜園", result.Text);
        Assert.Contains("道具の準備", result.Text);
        Assert.DoesNotContain("内部の追加指示", result.Text);
        Assert.DoesNotContain("##", result.Text);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnsupportedOperation()
    {
        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.GenerateAsync(Request("Unknown", new PromptDocument("", "", "", 0))));
        Assert.Equal(ExternalIntegrationErrorCodes.ValidationError, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_HonorsCancellation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateAsync(
            Request(AiOperations.BodyGeneration, new PromptDocument("", "", "", 0)), new CancellationToken(true)));
    }

    private static AiTextGenerationRequest Request(string operation, PromptDocument prompt) =>
        new(AiProviders.Gemini, "gemini-3.8-flash", operation,
            prompt.SystemInstruction, prompt.UserPrompt, null, null, []);
}
