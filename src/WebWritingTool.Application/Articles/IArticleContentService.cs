namespace WebWritingTool.Application.Articles;

public interface IArticleContentService
{
    Task<ArticleServiceResult<ConvertArticleHtmlResponse>> ConvertHtmlAsync(
        ConvertArticleHtmlCommand command,
        CancellationToken cancellationToken = default);
}
