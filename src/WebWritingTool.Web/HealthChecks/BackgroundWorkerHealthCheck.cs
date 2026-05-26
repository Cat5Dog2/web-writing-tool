namespace WebWritingTool.Web.HealthChecks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Web.BackgroundJobs;

internal sealed class BackgroundWorkerHealthCheck(
    BackgroundWorkerHealthState healthState,
    IOptions<BackgroundJobOptions> options)
    : IHealthCheck
{
    private static readonly string[] RequiredWorkerNames =
    [
        nameof(ArticleJobWorker),
        nameof(SearchCacheCleanupWorker)
    ];

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Background workers are disabled by configuration."));
        }

        var failures = GetFailures().ToArray();
        if (failures.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Background worker health check failed: {string.Join("; ", failures)}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Background workers are running."));
    }

    private IEnumerable<string> GetFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var staleThreshold = GetStaleThreshold(options.Value);

        foreach (var workerName in RequiredWorkerNames)
        {
            var snapshot = healthState.GetSnapshot(workerName);
            if (snapshot.State == BackgroundWorkerState.NotStarted)
            {
                yield return $"{workerName} has not started";
                continue;
            }

            if (snapshot.State == BackgroundWorkerState.Stopped)
            {
                yield return $"{workerName} stopped";
                continue;
            }

            if (snapshot.State == BackgroundWorkerState.Disabled)
            {
                yield return $"{workerName} is disabled while background jobs are enabled";
                continue;
            }

            if (snapshot.LastHeartbeatAt is null || now - snapshot.LastHeartbeatAt > staleThreshold)
            {
                yield return $"{workerName} heartbeat is stale";
            }
        }
    }

    private static TimeSpan GetStaleThreshold(BackgroundJobOptions options)
    {
        var cleanupThreshold = options.SearchCacheCleanupInterval + TimeSpan.FromMinutes(5);
        return cleanupThreshold > TimeSpan.FromMinutes(2)
            ? cleanupThreshold
            : TimeSpan.FromMinutes(2);
    }
}
