namespace WebWritingTool.Domain.Jobs;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled
}
