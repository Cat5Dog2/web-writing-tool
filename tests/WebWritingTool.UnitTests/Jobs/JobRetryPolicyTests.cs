using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;

namespace WebWritingTool.UnitTests.Jobs;

public class JobRetryPolicyTests
{
    [Theory]
    [InlineData(JobType.TitleGeneration, 3)]
    [InlineData(JobType.OutlineGeneration, 3)]
    [InlineData(JobType.BodyGeneration, 3)]
    [InlineData(JobType.Rewrite, 2)]
    [InlineData(JobType.WebSearch, 2)]
    [InlineData(JobType.XFullArchiveSearch, 2)]
    [InlineData(JobType.WordpressPost, 3)]
    [InlineData(JobType.Notification, 3)]
    public void GetMaxAttempts_ForJobType_ReturnsDesignedLimit(JobType jobType, int expected)
    {
        var policy = new JobRetryPolicy();

        var actual = policy.GetMaxAttempts(jobType);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(JobErrorCodes.RateLimited)]
    [InlineData(JobErrorCodes.Timeout)]
    [InlineData(JobErrorCodes.ExternalServerError)]
    [InlineData(JobErrorCodes.NetworkError)]
    [InlineData(JobErrorCodes.UnknownError)]
    public void CanRetry_WithRetryableErrorAndRemainingAttempt_ReturnsTrue(string errorCode)
    {
        var policy = new JobRetryPolicy();

        var canRetry = policy.CanRetry(errorCode, attemptCount: 1, maxAttempts: 3);

        Assert.True(canRetry);
    }

    [Theory]
    [InlineData(JobErrorCodes.ValidationError)]
    [InlineData(JobErrorCodes.UnauthorizedExternalApi)]
    [InlineData(JobErrorCodes.ForbiddenExternalApi)]
    [InlineData(JobErrorCodes.UsageLimitExceeded)]
    [InlineData(JobErrorCodes.NotFound)]
    [InlineData(JobErrorCodes.Conflict)]
    public void CanRetry_WithNonRetryableError_ReturnsFalse(string errorCode)
    {
        var policy = new JobRetryPolicy();

        var canRetry = policy.CanRetry(errorCode, attemptCount: 1, maxAttempts: 3);

        Assert.False(canRetry);
    }

    [Fact]
    public void CanRetry_WhenMaxAttemptsReached_ReturnsFalse()
    {
        var policy = new JobRetryPolicy();

        var canRetry = policy.CanRetry(JobErrorCodes.Timeout, attemptCount: 3, maxAttempts: 3);

        Assert.False(canRetry);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    public void CalculateNextRunAt_WithoutRetryAfter_UsesDesignedBackoff(
        int attemptCount,
        int expectedDelayMinutes)
    {
        var now = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var policy = new JobRetryPolicy();

        var nextRunAt = policy.CalculateNextRunAt(now, attemptCount);

        Assert.Equal(now.AddMinutes(expectedDelayMinutes), nextRunAt);
    }

    [Fact]
    public void CalculateNextRunAt_WithRetryAfter_UsesRetryAfter()
    {
        var now = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var policy = new JobRetryPolicy();

        var nextRunAt = policy.CalculateNextRunAt(now, attemptCount: 1, TimeSpan.FromSeconds(90));

        Assert.Equal(now.AddSeconds(90), nextRunAt);
    }
}
