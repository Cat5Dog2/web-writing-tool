using WebWritingTool.Application.Generation;

namespace WebWritingTool.Infrastructure.Generation;

public sealed class GeminiQuotaWindow
{
    private static readonly TimeZoneInfo QuotaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public List<GeminiQuotaUsage> Requests { get; set; } = [];

    public DateTimeOffset NextRequestAt { get; set; }

    public DateTimeOffset DayResetAt { get; set; }

    public long DailyRequests { get; set; }

    public DateTimeOffset CooldownUntil { get; set; }

    public int ConsecutiveRateLimits { get; set; }

    public DateTimeOffset? TryReserve(GeminiQuotaReservation reservation, int inputTokens,
        GeminiModelRateLimits limits, double safetyRatio)
    {
        var now = reservation.ReservedAt;
        var rpm = Budget(limits.RequestsPerMinute, safetyRatio);
        var tpm = Budget(limits.InputTokensPerMinute, safetyRatio);
        if (inputTokens > tpm)
            throw new ExternalIntegrationException(ExternalIntegrationErrorCodes.ValidationError,
                "生成入力がGeminiの入力トークン予算を超えています。参考資料を減らすか、確認済みの上限設定を見直してください。");

        Requests.RemoveAll(request => request.At <= now.AddMinutes(-1));
        if (now >= DayResetAt)
        {
            DayResetAt = NextDailyReset(now);
            DailyRequests = 0;
        }

        var next = now;
        if (NextRequestAt > next) next = NextRequestAt;
        if (CooldownUntil > next) next = CooldownUntil;
        if (limits.RequestsPerDay is int rpd && DailyRequests >= Budget(rpd, safetyRatio))
            if (DayResetAt > next) next = DayResetAt;

        var count = Requests.Count;
        var tokens = Requests.Sum(request => (long)request.InputTokens);
        foreach (var request in Requests.OrderBy(request => request.At))
        {
            if (count < rpm && tokens + inputTokens <= tpm) break;
            var expiresAt = request.At.AddMinutes(1);
            if (expiresAt > next) next = expiresAt;
            count--;
            tokens -= request.InputTokens;
        }

        if (next > now) return next;

        Requests.Add(new GeminiQuotaUsage(reservation.Id, now, inputTokens));
        DailyRequests++;
        NextRequestAt = now.AddSeconds(60d / rpm);
        return null;
    }

    public void RecordSuccess(GeminiQuotaReservation reservation, int? inputTokens, DateTimeOffset now)
    {
        Requests.RemoveAll(request => request.At <= now.AddMinutes(-1));
        var index = Requests.FindIndex(request => request.Id == reservation.Id);
        if (index >= 0 && inputTokens is >= 0)
            Requests[index] = Requests[index] with { InputTokens = inputTokens.Value };
        // 429より前に開始した並行リクエストの成功で、新しい待機を解除しない。
        if (reservation.ReservedAt >= CooldownUntil) ConsecutiveRateLimits = 0;
    }

    public TimeSpan RecordRateLimit(DateTimeOffset now, TimeSpan? retryAfter, bool dailyLimit, double jitter)
    {
        ConsecutiveRateLimits = Math.Min(ConsecutiveRateLimits + 1, 10);
        var delay = retryAfter is { } serverDelay && serverDelay > TimeSpan.Zero
            ? serverDelay
            : TimeSpan.FromSeconds(Math.Min(900, 60 * Math.Pow(2, ConsecutiveRateLimits - 1)) + jitter * 5);
        var next = now.Add(delay);
        if (dailyLimit && NextDailyReset(now) > next) next = NextDailyReset(now);
        if (next > CooldownUntil) CooldownUntil = next;
        return CooldownUntil - now;
    }

    public static DateTimeOffset NextDailyReset(DateTimeOffset now)
    {
        var tomorrow = TimeZoneInfo.ConvertTime(now, QuotaTimeZone).Date.AddDays(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrow, QuotaTimeZone));
    }

    private static int Budget(int limit, double safetyRatio) => Math.Max(1, (int)Math.Floor(limit * safetyRatio));
}

public sealed record GeminiQuotaUsage(Guid Id, DateTimeOffset At, int InputTokens);
