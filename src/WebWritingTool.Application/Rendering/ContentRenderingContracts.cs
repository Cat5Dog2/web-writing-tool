namespace WebWritingTool.Application.Rendering;

public sealed record ArticleHeadingContent(
    int Level,
    string Title,
    string? Body,
    int DisplayOrder);

public interface IContentRenderingService
{
    string BuildArticleMarkdown(IReadOnlyList<ArticleHeadingContent> headings);

    string ConvertMarkdownToHtml(string markdown, bool insertLineBreakAfterPeriod);

    string SanitizeHtml(string html);
}
