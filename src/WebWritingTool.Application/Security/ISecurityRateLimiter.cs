namespace WebWritingTool.Application.Security;

public interface ISecurityRateLimiter
{
    ValueTask<bool> IsAllowedAsync(
        string policyName,
        string partitionKey,
        CancellationToken cancellationToken = default);
}
