namespace WebWritingTool.Application.Articles;

public sealed record BulkGenerationOptions(
    string Scope = "OutlineOnly",
    bool UseWebSearch = true,
    bool UseXSearch = false,
    int WebResultCount = 10,
    int XResultCount = 10,
    int XSearchDays = 30);

public sealed record BulkGenerationProgress(
    Guid RunId, Guid BatchId, Guid ArticleId, string Keyword, string Status, string Stage,
    int CompletedHeadings, int TotalHeadings, string? Error, string? Warning,
    bool StopRequested, BulkGenerationOptions Options, string? Title = null)
{
    public bool IsActive => Status is "Queued" or "Running";
}

public sealed record BulkGenerationBatch(Guid BatchId, DateTimeOffset CreatedAt, int ArticleCount);

public interface IBulkGenerationService
{
    Task<IReadOnlyList<BulkGenerationBatch>> GetBatchesAsync(ArticleActor actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BulkGenerationProgress>> GetOverviewAsync(
        ArticleActor actor, Guid? batchId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BulkGenerationProgress>> GetProgressAsync(
        ArticleActor actor, Guid? batchId = null, Guid? articleId = null,
        CancellationToken cancellationToken = default);

    Task<ArticleServiceResult> RetryAsync(ArticleActor actor, Guid articleId, CancellationToken cancellationToken = default);

    Task<ArticleServiceResult> StopAsync(ArticleActor actor, Guid articleId, CancellationToken cancellationToken = default);
}
