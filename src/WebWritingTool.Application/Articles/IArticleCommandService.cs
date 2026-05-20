namespace WebWritingTool.Application.Articles;

public interface IArticleCommandService
{
    Task<ArticleServiceResult<CreateArticleResponse>> CreateAsync(
        CreateArticleCommand command,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult<BulkCreateArticlesResponse>> BulkCreateAsync(
        BulkCreateArticlesCommand command,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult<ArticleDetailResponse>> UpdateAsync(
        UpdateArticleCommand command,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult> DeleteAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default);
}
