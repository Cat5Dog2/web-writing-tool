using WebWritingTool.Domain.Jobs;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class JobDispatcher(IEnumerable<IJobHandler> handlers)
{
    private readonly Dictionary<JobType, IJobHandler> _handlers = handlers
        .GroupBy(handler => handler.JobType)
        .ToDictionary(group => group.Key, group => group.First());

    public Task<JobExecutionResult> DispatchAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            throw new JobExecutionException(
                JobErrorCodes.Conflict,
                "このジョブ種別の処理はまだ実装されていません。");
        }

        return handler.HandleAsync(job, cancellationToken);
    }
}
