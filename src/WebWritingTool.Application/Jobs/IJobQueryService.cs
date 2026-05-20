namespace WebWritingTool.Application.Jobs;

public interface IJobQueryService
{
    Task<JobStatusResponse?> GetAsync(
        JobActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
