using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class GeminiQuotaIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task ConcurrentWorkers_ShareReservationAndCooldownAcrossClients()
    {
        var options = OptionsForTest();
        var clock = new QuotaClock();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { return await Limiter(options, clock).ReserveAsync("model-a", 100, CancellationToken.None); }
            catch (ExternalApiDeferredException) { return null; }
        }));
        var reservation = Assert.Single(results.OfType<GeminiQuotaReservation>());
        var wait = await Limiter(options, clock).RecordRateLimitAsync(reservation, TimeSpan.FromMinutes(10), false, CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(10), wait);

        clock.Now = clock.Now.AddMinutes(2);
        var deferred = await Assert.ThrowsAsync<ExternalApiDeferredException>(() =>
            Limiter(options, clock).ReserveAsync("model-a", 100, CancellationToken.None));
        Assert.Equal(reservation.ReservedAt.AddMinutes(10), deferred.NextRunAt);
        Assert.NotNull(await Limiter(options, clock).ReserveAsync("model-b", 100, CancellationToken.None));
    }

    [Fact]
    public async Task TokenUsage_IsCorrectedAndDailyBudgetSurvivesNewInstances()
    {
        var options = OptionsForTest(new GeminiModelRateLimits
        {
            RequestsPerMinute = 60,
            InputTokensPerMinute = 1000,
            RequestsPerDay = 2
        });
        var clock = new QuotaClock();
        var first = await Limiter(options, clock).ReserveAsync("model-a", 900, CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(2);
        await Limiter(options, clock).RecordSuccessAsync(first, 200, CancellationToken.None);
        Assert.NotNull(await Limiter(options, clock).ReserveAsync("model-a", 800, CancellationToken.None));
        clock.Now = clock.Now.AddMinutes(2);
        var deferred = await Assert.ThrowsAsync<ExternalApiDeferredException>(() =>
            Limiter(options, clock).ReserveAsync("model-a", 100, CancellationToken.None));
        Assert.Equal(GeminiQuotaWindow.NextDailyReset(clock.Now), deferred.NextRunAt);
    }

    [Fact]
    public async Task ModelAliases_ShareQuotaGroup_AndCanceledRequestDoesNotReserve()
    {
        var options = new GeminiOptions
        {
            RateLimits = new GeminiRateLimitOptions
            {
                Scope = Guid.NewGuid().ToString("N"),
                SafetyRatio = 1,
                Models = new()
                {
                    ["alias-a"] = new() { QuotaGroup = "shared" },
                    ["alias-b"] = new() { QuotaGroup = "shared" }
                }
            }
        };
        var clock = new QuotaClock();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Limiter(options, clock).ReserveAsync("alias-a", 100, canceled.Token));
        Assert.NotNull(await Limiter(options, clock).ReserveAsync("alias-a", 100, CancellationToken.None));
        await Assert.ThrowsAsync<ExternalApiDeferredException>(() =>
            Limiter(options, clock).ReserveAsync("alias-b", 100, CancellationToken.None));
    }

    private PostgresGeminiQuotaLimiter Limiter(GeminiOptions options, TimeProvider clock) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString).Options,
        Options.Create(options), clock);

    private static GeminiOptions OptionsForTest(GeminiModelRateLimits? limits = null) => new()
    {
        RateLimits = new GeminiRateLimitOptions
        {
            Scope = Guid.NewGuid().ToString("N"),
            SafetyRatio = 1,
            Default = limits ?? new GeminiModelRateLimits()
        }
    };

    private sealed class QuotaClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
