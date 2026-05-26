using System.Net;
using WebWritingTool.Application.Rendering;

namespace WebWritingTool.UnitTests.Rendering;

public class ContentRenderingServiceTests
{
    private readonly ContentRenderingService renderer = new();

    [Fact]
    public void BuildArticleMarkdown_CombinesHeadingsInDisplayOrder()
    {
        var markdown = renderer.BuildArticleMarkdown(
            [
                new ArticleHeadingContent(3, "詳細", "H3本文", 20),
                new ArticleHeadingContent(2, "概要", "H2本文", 10)
            ]);

        var expected = string.Join(
            Environment.NewLine + Environment.NewLine,
            "## 概要",
            "H2本文",
            "### 詳細",
            "H3本文");

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void ConvertMarkdownToHtml_ConvertsHeadingsAndParagraphs()
    {
        var html = renderer.ConvertMarkdownToHtml(
            "## 概要\r\n\r\n本文です。\r\n\r\n### 詳細\r\n\r\n補足です。",
            insertLineBreakAfterPeriod: false);
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("<h2>概要</h2>", decoded);
        Assert.Contains("<p>本文です。</p>", decoded);
        Assert.Contains("<h3>詳細</h3>", decoded);
        Assert.Contains("<p>補足です。</p>", decoded);
    }

    [Fact]
    public void ConvertMarkdownToHtml_WhenInsertLineBreakAfterPeriod_InsertsBrInParagraphs()
    {
        var html = renderer.ConvertMarkdownToHtml(
            "## 見出し。\r\n\r\n一文目です。二文目です。",
            insertLineBreakAfterPeriod: true);
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("<h2>見出し。</h2>", decoded);
        Assert.Contains("<br>", html);
        Assert.Contains("一文目です。", decoded);
        Assert.Contains("二文目です。", decoded);
    }

    [Fact]
    public void SanitizeHtml_RemovesScriptTagsAndEventAttributes()
    {
        var html = renderer.SanitizeHtml(
            "<h2 onclick=\"alert(1)\">概要</h2><script>alert(1)</script><p onload=\"alert(2)\">本文</p><img src=\"x\" onerror=\"alert(3)\"><iframe src=\"https://example.com\">危険</iframe>");

        Assert.Equal("<h2>概要</h2><p>本文</p>", html);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertMarkdownToHtml_RemovesUnsafeJavascriptLinks()
    {
        var html = renderer.ConvertMarkdownToHtml(
            "[安全](https://example.com) [危険](javascript:alert(1))",
            insertLineBreakAfterPeriod: false);
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("href=\"https://example.com\"", decoded);
        Assert.Contains("target=\"_blank\"", decoded);
        Assert.Contains("rel=\"noopener noreferrer\"", decoded);
        Assert.Contains("安全", decoded);
        Assert.Contains("危険", decoded);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }
}
