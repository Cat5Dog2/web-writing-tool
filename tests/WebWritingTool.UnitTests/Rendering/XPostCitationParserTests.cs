using WebWritingTool.Application.Rendering;

namespace WebWritingTool.UnitTests.Rendering;

public class XPostCitationParserTests
{
    [Fact]
    public void ExtractPostIds_WithXAndTwitterCitations_ReturnsDistinctPostIds()
    {
        const string html = """
            <blockquote cite="https://x.com/example/status/1234567890">
              <p>first</p>
            </blockquote>
            <blockquote cite="https://twitter.com/other/status/9876543210">
              <p>second</p>
            </blockquote>
            <blockquote cite="https://x.com/example/status/1234567890">
              <p>duplicate</p>
            </blockquote>
            """;

        var postIds = XPostCitationParser.ExtractPostIds(html);

        Assert.Equal(["1234567890", "9876543210"], postIds);
    }

    [Fact]
    public void ExtractPostIds_WithUnrelatedOrInvalidCitations_IgnoresThem()
    {
        const string html = """
            <blockquote cite="https://example.com/status/111"><p>other site</p></blockquote>
            <blockquote cite="https://x.com/example/status/not-numeric"><p>invalid id</p></blockquote>
            <a href="https://x.com/example/status/222">plain link</a>
            """;

        var postIds = XPostCitationParser.ExtractPostIds(html);

        Assert.Empty(postIds);
    }
}
