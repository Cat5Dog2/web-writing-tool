namespace WebWritingTool.Infrastructure.Generation;

public interface IGeminiQuotaLimiter
{
    Task<GeminiQuotaReservation> ReserveAsync(string model, int inputTokens, CancellationToken cancellationToken);

    Task RecordSuccessAsync(GeminiQuotaReservation reservation, int? inputTokens, CancellationToken cancellationToken);

    Task<TimeSpan> RecordRateLimitAsync(GeminiQuotaReservation reservation, TimeSpan? retryAfter,
        bool dailyLimit, CancellationToken cancellationToken);
}

public sealed record GeminiQuotaReservation(Guid Id, string Model, DateTimeOffset ReservedAt);
