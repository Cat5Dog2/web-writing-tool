using System.Collections.Concurrent;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Infrastructure.Security;

public sealed class InMemorySecurityRateLimiter : ISecurityRateLimiter
{
    private static readonly IReadOnlyDictionary<string, RateLimitRule> Rules = new Dictionary<string, RateLimitRule>
    {
        [SecurityRateLimitPolicyNames.BulkArticleRegistration] = new(3, TimeSpan.FromMinutes(1)),
        [SecurityRateLimitPolicyNames.JobRegistration] = new(30, TimeSpan.FromMinutes(1)),
        [SecurityRateLimitPolicyNames.NotificationTest] = new(5, TimeSpan.FromMinutes(10)),
        [SecurityRateLimitPolicyNames.WordpressPost] = new(10, TimeSpan.FromMinutes(1))
    };

    private readonly ConcurrentDictionary<string, RateLimitCounter> counters = new(StringComparer.Ordinal);

    public ValueTask<bool> IsAllowedAsync(
        string policyName,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<bool>(cancellationToken);
        }

        if (!Rules.TryGetValue(policyName, out var rule))
        {
            return ValueTask.FromResult(true);
        }

        var safePartition = string.IsNullOrWhiteSpace(partitionKey) ? "anonymous" : partitionKey.Trim();
        var counter = counters.GetOrAdd($"{policyName}:{safePartition}", _ => new RateLimitCounter());
        var now = DateTimeOffset.UtcNow;

        lock (counter.SyncRoot)
        {
            if (counter.WindowStartedAt is null || now - counter.WindowStartedAt >= rule.Window)
            {
                counter.WindowStartedAt = now;
                counter.Count = 0;
            }

            if (counter.Count >= rule.PermitLimit)
            {
                return ValueTask.FromResult(false);
            }

            counter.Count++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed record RateLimitRule(int PermitLimit, TimeSpan Window);

    private sealed class RateLimitCounter
    {
        public object SyncRoot { get; } = new();

        public DateTimeOffset? WindowStartedAt { get; set; }

        public int Count { get; set; }
    }
}
