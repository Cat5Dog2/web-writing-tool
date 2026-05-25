namespace WebWritingTool.Application.Articles;

public interface IArticleHeadingService
{
    Task<ArticleServiceResult<ArticleHeadingListResponse>> GetHeadingsAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult<ArticleHeadingResponse>> CreateHeadingAsync(
        CreateArticleHeadingCommand command,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult<ArticleHeadingResponse>> UpdateHeadingAsync(
        UpdateArticleHeadingCommand command,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult> DeleteHeadingAsync(
        ArticleActor actor,
        Guid articleId,
        Guid headingId,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult> UpdateHeadingOrderAsync(
        UpdateArticleHeadingOrderCommand command,
        CancellationToken cancellationToken = default);
}
