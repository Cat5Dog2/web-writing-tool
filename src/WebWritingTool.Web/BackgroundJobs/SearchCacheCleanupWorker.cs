using Microsoft.Extensions.Options;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Search;

namespace WebWritingTool.Web.BackgroundJobs;

public sealed class SearchCacheCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<SearchCacheCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Search cache cleanup worker is disabled.");
            return;
        }

        logger.LogInformation("Search cache cleanup worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Search cache cleanup worker loop failed.");
            }

            await DelayAsync(stoppingToken);
        }

        logger.LogInformation("Search cache cleanup worker stopped.");
    }

    private async Task CleanupAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<SearchCacheCleanupService>();
        var result = await cleanupService.CleanupExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
        if (result.TotalChanged > 0)
        {
            logger.LogInformation(
                "Expired search cache cleanup completed. searchContentCleared={SearchContentCleared} searchDeleted={SearchDeleted} xContentCleared={XContentCleared} xDeleted={XDeleted}",
                result.SearchResultsContentCleared,
                result.SearchResultsDeleted,
                result.XSearchPostsContentCleared,
                result.XSearchPostsDeleted);
        }
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.Value.SearchCacheCleanupInterval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
