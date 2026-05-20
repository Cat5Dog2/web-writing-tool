using WebWritingTool.Domain.Jobs;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public interface IJobHandler
{
    JobType JobType { get; }

    Task<JobExecutionResult> HandleAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default);
}
