using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Search;
using WebWritingTool.Web.HealthChecks;

namespace WebWritingTool.Web.BackgroundJobs;

public sealed class SearchCacheCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<SearchCacheCleanupWorker> logger,
    ISecretMasker secretMasker,
    BackgroundWorkerHealthState healthState)
    : BackgroundService
{
    private const string WorkerName = nameof(SearchCacheCleanupWorker);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            healthState.MarkDisabled(WorkerName);
            logger.LogInformation("Search cache cleanup worker is disabled.");
            return;
        }

        healthState.MarkStarted(WorkerName);
        logger.LogInformation("Search cache cleanup worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            healthState.MarkHeartbeat(WorkerName);
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
                logger.LogError(
                    "Search cache cleanup worker loop failed. exceptionType={ExceptionType} message={Message}",
                    ex.GetType().Name,
                    secretMasker.Mask(ex.Message));
            }

            await DelayAsync(stoppingToken);
        }

        healthState.MarkStopped(WorkerName);
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
