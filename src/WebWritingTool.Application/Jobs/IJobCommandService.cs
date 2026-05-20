namespace WebWritingTool.Application.Jobs;

public interface IJobCommandService
{
    Task<JobServiceResult<JobAcceptedResponse>> EnqueueAsync(
        EnqueueJobCommand command,
        CancellationToken cancellationToken = default);

    Task<JobServiceResult<JobCancelResponse>> CancelAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobServiceResult<JobAcceptedResponse>> RetryAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
