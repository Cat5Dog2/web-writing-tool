namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; init; } = true;

    public int IdleDelaySeconds { get; init; } = 3;

    public int LockTimeoutMinutes { get; init; } = 30;

    public int MaxJobsPerLoop { get; init; } = 1;

    public string WorkerIdPrefix { get; init; } = "app";

    public int SearchCacheCleanupIntervalMinutes { get; init; } = 60;

    public TimeSpan IdleDelay => TimeSpan.FromSeconds(Math.Max(1, IdleDelaySeconds));

    public TimeSpan LockTimeout => TimeSpan.FromMinutes(Math.Max(1, LockTimeoutMinutes));

    public TimeSpan SearchCacheCleanupInterval =>
        TimeSpan.FromMinutes(Math.Max(1, SearchCacheCleanupIntervalMinutes));
}
