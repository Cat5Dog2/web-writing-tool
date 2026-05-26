using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Security;

namespace WebWritingTool.UnitTests.Security;

public class InMemorySecurityRateLimiterTests
{
    [Fact]
    public async Task IsAllowedAsync_WhenPolicyLimitExceeded_ReturnsFalseForSamePartition()
    {
        var limiter = new InMemorySecurityRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(await limiter.IsAllowedAsync(SecurityRateLimitPolicyNames.NotificationTest, "user-1"));
        }

        Assert.False(await limiter.IsAllowedAsync(SecurityRateLimitPolicyNames.NotificationTest, "user-1"));
        Assert.True(await limiter.IsAllowedAsync(SecurityRateLimitPolicyNames.NotificationTest, "user-2"));
    }
}
