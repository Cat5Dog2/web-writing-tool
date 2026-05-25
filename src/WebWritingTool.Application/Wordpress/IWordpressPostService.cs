using WebWritingTool.Application.Jobs;

namespace WebWritingTool.Application.Wordpress;

public interface IWordpressPostCommandService
{
    Task<WordpressServiceResult<JobAcceptedResponse>> CreatePostJobAsync(
        CreateWordpressPostJobCommand command,
        CancellationToken cancellationToken = default);

    Task QueueAutoPostIfReadyAsync(
        string userId,
        Guid articleId,
        CancellationToken cancellationToken = default);
}

public interface IWordpressPostQueryService
{
    Task<WordpressPostPreviewResponse?> GetPreviewAsync(
        WordpressActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default);

    Task<WordpressPostHistoryResponse?> GetHistoryAsync(
        WordpressActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default);
}
