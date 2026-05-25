namespace WebWritingTool.Application.Wordpress;

public interface IWordpressSiteCommandService
{
    Task<WordpressServiceResult<WordpressSiteResponse>> CreateAsync(
        CreateWordpressSiteCommand command,
        CancellationToken cancellationToken = default);

    Task<WordpressServiceResult<WordpressSiteResponse>> UpdateAsync(
        UpdateWordpressSiteCommand command,
        CancellationToken cancellationToken = default);

    Task<WordpressServiceResult> DeleteAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default);

    Task<WordpressServiceResult<WordpressConnectionTestResponse>> TestConnectionAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default);
}

public interface IWordpressSiteQueryService
{
    Task<WordpressSiteListResponse> ListAsync(
        WordpressActor actor,
        CancellationToken cancellationToken = default);

    Task<WordpressServiceResult<WordpressCategoryListResponse>> GetCategoriesAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default);
}
