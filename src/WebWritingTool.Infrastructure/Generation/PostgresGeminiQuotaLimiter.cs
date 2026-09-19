using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Generation;

public sealed class PostgresGeminiQuotaLimiter(
    DbContextOptions<ApplicationDbContext> dbOptions,
    IOptions<GeminiOptions> options,
    TimeProvider timeProvider) : IGeminiQuotaLimiter
{
    public async Task<GeminiQuotaReservation> ReserveAsync(string model, int inputTokens, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputTokens);
        var reservation = new GeminiQuotaReservation(Guid.NewGuid(), model, timeProvider.GetUtcNow());
        var next = await UpdateAsync(model, window =>
        {
            reservation = reservation with { ReservedAt = timeProvider.GetUtcNow() };
            return window.TryReserve(reservation, inputTokens, options.Value.RateLimits.ForModel(model), options.Value.RateLimits.SafetyRatio);
        }, cancellationToken);
        if (next.HasValue) throw new ExternalApiDeferredException(next.Value);
        return reservation;
    }

    public async Task RecordSuccessAsync(GeminiQuotaReservation reservation, int? inputTokens, CancellationToken cancellationToken)
    {
        await UpdateAsync(reservation.Model, window =>
        {
            window.RecordSuccess(reservation, inputTokens, timeProvider.GetUtcNow());
            return true;
        }, cancellationToken);
    }

    public Task<TimeSpan> RecordRateLimitAsync(GeminiQuotaReservation reservation, TimeSpan? retryAfter,
        bool dailyLimit, CancellationToken cancellationToken) => UpdateAsync(reservation.Model,
            window => window.RecordRateLimit(timeProvider.GetUtcNow(), retryAfter, dailyLimit, Random.Shared.NextDouble()), cancellationToken);

    private async Task<T> UpdateAsync<T>(string model, Func<GeminiQuotaWindow, T> update, CancellationToken cancellationToken)
    {
        var limits = options.Value.RateLimits;
        var group = limits.ForModel(model).QuotaGroup ?? model;
        // 生成Handlerが追跡中のEntityを保存せず、短い独立トランザクションで枠だけ予約する。
        await using var db = new ApplicationDbContext(dbOptions);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        const string initialState = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "GeminiQuotaStates" ("Scope", "Model", "StateJson")
            VALUES ({limits.Scope}, {group}, {initialState}::jsonb)
            ON CONFLICT ("Scope", "Model") DO NOTHING
            """, cancellationToken);
        var state = await db.GeminiQuotaStates.FromSqlInterpolated($"""
            SELECT * FROM "GeminiQuotaStates"
            WHERE "Scope" = {limits.Scope} AND "Model" = {group}
            FOR UPDATE
            """).SingleAsync(cancellationToken);
        var window = JsonSerializer.Deserialize<GeminiQuotaWindow>(state.StateJson)
            ?? throw new InvalidOperationException("Gemini quota state is invalid.");
        var result = update(window);
        state.StateJson = JsonSerializer.Serialize(window);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
