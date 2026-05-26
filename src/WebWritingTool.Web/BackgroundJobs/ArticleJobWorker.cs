using Microsoft.Extensions.Options;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.BackgroundJobs;

namespace WebWritingTool.Web.BackgroundJobs;

public sealed class ArticleJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<ArticleJobWorker> logger,
    ISecretMasker secretMasker)
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
                logger.LogError(
                    "Background job worker loop failed. exceptionType={ExceptionType} message={Message}",
                    ex.GetType().Name,
                    MaskExceptionMessage(ex));
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
        var notificationJobService = scope.ServiceProvider.GetRequiredService<INotificationJobService>();

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

            await ProcessJobAsync(job, dispatcher, leaseService, notificationJobService, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(
        LeasedJob job,
        JobDispatcher dispatcher,
        JobLeaseService leaseService,
        INotificationJobService notificationJobService,
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
            await QueueSucceededNotificationAsync(job, result.ResultJson, notificationJobService, stoppingToken);

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
                    "Job retry scheduled. jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts} errorCode={ErrorCode} nextRunAt={NextRunAt} exceptionType={ExceptionType} message={Message}",
                    decision.JobId,
                    decision.JobType,
                    decision.AttemptCount,
                    decision.MaxAttempts,
                    decision.ErrorCode,
                    decision.NextRunAt,
                    ex.GetType().Name,
                    MaskExceptionMessage(ex));
            }
            else
            {
                await QueueFailedNotificationAsync(job, decision, notificationJobService);

                logger.LogError(
                    "Job failed. jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts} errorCode={ErrorCode} exceptionType={ExceptionType} message={Message}",
                    decision.JobId,
                    decision.JobType,
                    decision.AttemptCount,
                    decision.MaxAttempts,
                    decision.ErrorCode,
                    ex.GetType().Name,
                    MaskExceptionMessage(ex));
            }
        }
    }

    private async Task QueueSucceededNotificationAsync(
        LeasedJob job,
        string? resultJson,
        INotificationJobService notificationJobService,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationJobService.QueueForSucceededJobAsync(
                new QueueNotificationForSucceededJobCommand(
                    job.UserId,
                    job.Id,
                    job.ArticleId,
                    job.JobType,
                    resultJson),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Notification job enqueue failed after job success. jobId={JobId} exceptionType={ExceptionType} message={Message}",
                job.Id,
                ex.GetType().Name,
                MaskExceptionMessage(ex));
        }
    }

    private async Task QueueFailedNotificationAsync(
        LeasedJob job,
        JobFailureDecision decision,
        INotificationJobService notificationJobService)
    {
        try
        {
            await notificationJobService.QueueForFailedJobAsync(
                new QueueNotificationForFailedJobCommand(
                    job.UserId,
                    job.Id,
                    job.ArticleId,
                    job.JobType,
                    decision.ErrorCode,
                    decision.ErrorMessage),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Notification job enqueue failed after job failure. jobId={JobId} exceptionType={ExceptionType} message={Message}",
                job.Id,
                ex.GetType().Name,
                MaskExceptionMessage(ex));
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

    private string MaskExceptionMessage(Exception exception)
    {
        return secretMasker.Mask(exception.Message);
    }
}
