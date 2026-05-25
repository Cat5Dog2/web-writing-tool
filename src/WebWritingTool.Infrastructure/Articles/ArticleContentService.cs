using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

public sealed partial class ArticleContentService(
    ApplicationDbContext dbContext,
    IContentRenderingService contentRenderingService)
    : IArticleContentService
{
    public async Task<ArticleServiceResult<ConvertArticleHtmlResponse>> ConvertHtmlAsync(
        ConvertArticleHtmlCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult<ConvertArticleHtmlResponse>.Failure(ArticleServiceError.NotFound);
        }

        var headings = await dbContext.ArticleHeadings
            .AsNoTracking()
            .Where(heading => heading.ArticleId == article.Id)
            .OrderBy(heading => heading.DisplayOrder)
            .Select(heading => new ArticleHeadingContent(
                heading.Level,
                heading.Title,
                heading.Body,
                heading.DisplayOrder))
            .ToListAsync(cancellationToken);

        var markdown = headings.Count > 0
            ? contentRenderingService.BuildArticleMarkdown(headings)
            : article.Body;

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return ArticleServiceResult<ConvertArticleHtmlResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                [new ArticleValidationError(nameof(markdown), "HTML変換する本文がありません。")]);
        }

        var htmlBody = contentRenderingService.ConvertMarkdownToHtml(
            markdown,
            command.InsertLineBreakAfterPeriod);

        if (string.IsNullOrWhiteSpace(GetVisibleText(htmlBody)))
        {
            return ArticleServiceResult<ConvertArticleHtmlResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                [new ArticleValidationError(nameof(htmlBody), "HTML変換後の本文が空です。")]);
        }

        var convertedAt = DateTimeOffset.UtcNow;
        var reviewSensitiveValueChanged =
            !string.Equals(article.Body, markdown, StringComparison.Ordinal)
            || !string.Equals(article.HtmlBody, htmlBody, StringComparison.Ordinal);

        article.Body = markdown;
        article.HtmlBody = htmlBody;

        if (reviewSensitiveValueChanged)
        {
            article.HumanReviewedAt = null;
            article.HumanReviewedByUserId = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult<ConvertArticleHtmlResponse>.Success(
            new ConvertArticleHtmlResponse(article.Id, htmlBody, convertedAt));
    }

    private static string GetVisibleText(string html)
    {
        var withoutTags = TagsPattern().Replace(html, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static bool CanAccess(ArticleActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagsPattern();
}
