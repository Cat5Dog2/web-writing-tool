using WebWritingTool.Domain.Jobs;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class JobRetryPolicy
{
    private static readonly HashSet<string> RetryableErrorCodes = new(StringComparer.Ordinal)
    {
        JobErrorCodes.RateLimited,
        JobErrorCodes.Timeout,
        JobErrorCodes.ExternalServerError,
        JobErrorCodes.ExternalBadResponse,
        JobErrorCodes.NetworkError,
        JobErrorCodes.UnknownError
    };

    public int GetMaxAttempts(JobType jobType)
    {
        return jobType switch
        {
            JobType.Rewrite => 2,
            JobType.WebSearch => 2,
            JobType.XFullArchiveSearch => 2,
            _ => 3
        };
    }

    public bool CanRetry(string? errorCode, int attemptCount, int maxAttempts)
    {
        if (attemptCount >= maxAttempts)
        {
            return false;
        }

        return IsRetryableError(errorCode);
    }

    public bool IsRetryableError(string? errorCode)
    {
        return !string.IsNullOrWhiteSpace(errorCode)
            && RetryableErrorCodes.Contains(errorCode);
    }

    public DateTimeOffset CalculateNextRunAt(
        DateTimeOffset now,
        int attemptCount,
        TimeSpan? retryAfter = null)
    {
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return now.Add(retryAfter.Value);
        }

        var delay = attemptCount switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15)
        };

        return now.Add(delay);
    }
}
