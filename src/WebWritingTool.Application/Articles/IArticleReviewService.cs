namespace WebWritingTool.Application.Articles;

public interface IArticleReviewService
{
    Task<ArticleServiceResult<HumanReviewResponse>> CompleteHumanReviewAsync(
        CompleteHumanReviewCommand command,
        CancellationToken cancellationToken = default);
}
