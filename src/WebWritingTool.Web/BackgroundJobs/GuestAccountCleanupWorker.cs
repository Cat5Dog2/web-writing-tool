using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Web.HealthChecks;

namespace WebWritingTool.Web.BackgroundJobs;

public sealed class GuestAccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<GuestAccountCleanupWorker> logger,
    ISecretMasker secretMasker,
    BackgroundWorkerHealthState healthState) : BackgroundService
{
    private const string WorkerName = nameof(GuestAccountCleanupWorker);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            healthState.MarkDisabled(WorkerName);
            logger.LogInformation("Guest account cleanup worker is disabled.");
            return;
        }

        healthState.MarkStarted(WorkerName);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                healthState.MarkHeartbeat(WorkerName);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var cleanup = scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>();
                    var deleted = await cleanup.CleanupExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Expired guest account cleanup completed. deletedUsers={DeletedUsers}", deleted);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError("Guest account cleanup failed. exceptionType={ExceptionType} message={Message}",
                        exception.GetType().Name, secretMasker.Mask(exception.Message));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Guest account cleanup worker is stopping.");
        }
        finally
        {
            healthState.MarkStopped(WorkerName);
        }
    }
}
