namespace WebWritingTool.Application.Articles;

public interface IArticleQueryService
{
    Task<ArticleListResponse> SearchAsync(
        ArticleActor actor,
        ArticleListQuery query,
        CancellationToken cancellationToken = default);

    Task<ArticleDetailResponse?> GetAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default);

    Task<ArticleFormOptionsResponse> GetFormOptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
