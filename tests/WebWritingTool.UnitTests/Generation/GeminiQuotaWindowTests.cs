using WebWritingTool.Application.Generation;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.UnitTests.Generation;

public class GeminiQuotaWindowTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-19T12:00:00Z");

    [Fact]
    public void Reservations_ArePacedAndUseSafetyMargin()
    {
        var window = new GeminiQuotaWindow();
        var limits = new GeminiModelRateLimits { RequestsPerMinute = 5 };
        Assert.Null(window.TryReserve(Reservation(Start), 100, limits, 0.8));
        Assert.Equal(Start.AddSeconds(15), window.TryReserve(Reservation(Start.AddSeconds(14)), 100, limits, 0.8));
        Assert.Single(window.Requests);
        Assert.Equal(1, window.DailyRequests);
        Assert.Null(window.TryReserve(Reservation(Start.AddSeconds(15)), 100, limits, 0.8));
    }

    [Fact]
    public void TokenBudget_WaitsUntilEnoughReservationsExpire_AndAllowsExactBoundary()
    {
        var window = new GeminiQuotaWindow();
        var limits = new GeminiModelRateLimits { RequestsPerMinute = 60, InputTokensPerMinute = 1000 };
        Assert.Null(window.TryReserve(Reservation(Start), 500, limits, 1));
        Assert.Null(window.TryReserve(Reservation(Start.AddSeconds(1)), 300, limits, 1));
        Assert.Equal(Start.AddMinutes(1), window.TryReserve(Reservation(Start.AddSeconds(2)), 600, limits, 1));
        Assert.Null(window.TryReserve(Reservation(Start.AddMinutes(1)), 700, limits, 1));
        Assert.Equal(1000, window.Requests.Sum(request => request.InputTokens));
    }

    [Fact]
    public void InputLargerThanBudget_FailsWithoutReservingOrWaitingForever()
    {
        var window = new GeminiQuotaWindow();
        var exception = Assert.Throws<ExternalIntegrationException>(() => window.TryReserve(Reservation(Start), 801,
            new GeminiModelRateLimits { InputTokensPerMinute = 1000 }, 0.8));
        Assert.Equal(ExternalIntegrationErrorCodes.ValidationError, exception.ErrorCode);
        Assert.Empty(window.Requests);
        Assert.Equal(0, window.DailyRequests);
    }

    [Fact]
    public void DailyBudget_WaitsForPacificMidnightAndResetsAtBoundary()
    {
        var window = new GeminiQuotaWindow();
        var limits = new GeminiModelRateLimits { RequestsPerDay = 2 };
        Assert.Null(window.TryReserve(Reservation(Start), 100, limits, 0.8));
        var reset = DateTimeOffset.Parse("2026-09-20T07:00:00Z");
        Assert.Equal(reset, window.TryReserve(Reservation(Start.AddMinutes(2)), 100, limits, 0.8));
        Assert.Equal(1, window.DailyRequests);
        Assert.Null(window.TryReserve(Reservation(reset), 100, limits, 0.8));
        Assert.Equal(1, window.DailyRequests);
    }

    [Theory]
    [InlineData("2026-03-08T08:00:00Z", "2026-03-09T07:00:00Z")]
    [InlineData("2026-11-01T07:00:00Z", "2026-11-02T08:00:00Z")]
    public void DailyReset_UsesPacificCalendarAcrossDaylightSavingChanges(string now, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), GeminiQuotaWindow.NextDailyReset(DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void ActualInputTokens_ReplaceEstimate_AndOldSuccessDoesNotClearCooldown()
    {
        var window = new GeminiQuotaWindow();
        var first = Reservation(Start);
        var limits = new GeminiModelRateLimits { RequestsPerMinute = 60, InputTokensPerMinute = 1000 };
        Assert.Null(window.TryReserve(first, 900, limits, 1));
        window.RecordSuccess(first, 200, Start.AddSeconds(1));
        Assert.Null(window.TryReserve(Reservation(Start.AddSeconds(2)), 800, limits, 1));
        window.RecordRateLimit(Start.AddSeconds(3), TimeSpan.FromMinutes(5), false, 0);
        window.RecordSuccess(first, 200, Start.AddSeconds(4));
        Assert.Equal(Start.AddSeconds(303), window.TryReserve(Reservation(Start.AddSeconds(65)), 100, limits, 1));
        Assert.Equal(1, window.ConsecutiveRateLimits);
    }

    [Fact]
    public void Cooldown_PreservesLongerWaitAndBacksOffWithoutServerHint()
    {
        var window = new GeminiQuotaWindow();
        Assert.Equal(TimeSpan.FromSeconds(62.5), window.RecordRateLimit(Start, null, false, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(125), window.RecordRateLimit(Start.AddSeconds(63), null, false, 1));
        window.RecordRateLimit(Start.AddSeconds(64), TimeSpan.FromSeconds(10), false, 0);
        Assert.Equal(Start.AddSeconds(188), window.CooldownUntil);
        Assert.Equal(GeminiQuotaWindow.NextDailyReset(Start) - Start,
            window.RecordRateLimit(Start, TimeSpan.FromSeconds(10), true, 0));
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 100, 0)]
    [InlineData(1, 100, 1.1)]
    public void InvalidOptions_AreRejected(int rpm, int tpm, double ratio)
    {
        Assert.False(new GeminiRateLimitOptions
        {
            SafetyRatio = ratio,
            Default = new GeminiModelRateLimits { RequestsPerMinute = rpm, InputTokensPerMinute = tpm }
        }.IsValid());
    }

    private static GeminiQuotaReservation Reservation(DateTimeOffset now) => new(Guid.NewGuid(), "gemini-3.8-flash", now);
}
