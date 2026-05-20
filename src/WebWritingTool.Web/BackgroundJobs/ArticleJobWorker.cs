using Microsoft.Extensions.Options;
using WebWritingTool.Infrastructure.BackgroundJobs;

namespace WebWritingTool.Web.BackgroundJobs;

public sealed class ArticleJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<ArticleJobWorker> logger)
    : BackgroundService
{
    private readonly string _workerId = CreateWorkerId(options.Value.WorkerIdPrefix);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Background job worker is disabled.");
            return;
        }

        logger.LogInformation("Background job worker started. workerId={WorkerId}", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAvailableJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job worker loop failed.");
            }

            await DelayAsync(stoppingToken);
        }

        logger.LogInformation("Background job worker stopped. workerId={WorkerId}", _workerId);
    }

    private async Task ProcessAvailableJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var leaseService = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<JobDispatcher>();

        var recoveredCount = await leaseService.RecoverExpiredLocksAsync(stoppingToken);
        if (recoveredCount > 0)
        {
            logger.LogWarning("Recovered expired background job locks. count={RecoveredCount}", recoveredCount);
        }

        var maxJobs = Math.Max(1, options.Value.MaxJobsPerLoop);
        for (var index = 0; index < maxJobs && !stoppingToken.IsCancellationRequested; index++)
        {
            var job = await leaseService.TryAcquireAsync(_workerId, stoppingToken);
            if (job is null)
            {
                return;
            }

            await ProcessJobAsync(job, dispatcher, leaseService, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(
        LeasedJob job,
        JobDispatcher dispatcher,
        JobLeaseService leaseService,
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Job started. jobId={JobId} jobType={JobType} attemptCount={AttemptCount}",
            job.Id,
            job.JobType,
            job.AttemptCount);

        try
        {
            var result = await dispatcher.DispatchAsync(job, stoppingToken);
            await leaseService.MarkSucceededAsync(job.Id, result.ResultJson, stoppingToken);

            logger.LogInformation(
                "Job succeeded. jobId={JobId} jobType={JobType} attemptCount={AttemptCount}",
                job.Id,
                job.JobType,
                job.AttemptCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var decision = await leaseService.MarkFailureOrRetryAsync(job.Id, ex, CancellationToken.None);
            if (decision is null)
            {
                logger.LogWarning("Job failure could not be recorded because the job was not found. jobId={JobId}", job.Id);
                return;
            }

            if (decision.Retried)
            {
                logger.LogWarning(
                    ex,
                    "Job retry scheduled. jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts} errorCode={ErrorCode} nextRunAt={NextRunAt}",
                    decision.JobId,
                    decision.JobType,
                    decision.AttemptCount,
                    decision.MaxAttempts,
                    decision.ErrorCode,
                    decision.NextRunAt);
            }
            else
            {
                logger.LogError(
                    ex,
                    "Job failed. jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts} errorCode={ErrorCode}",
                    decision.JobId,
                    decision.JobType,
                    decision.AttemptCount,
                    decision.MaxAttempts,
                    decision.ErrorCode);
            }
        }
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.Value.IdleDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static string CreateWorkerId(string prefix)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "app" : prefix.Trim();
        return $"{safePrefix}:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }
}
