using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Usage;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Usage;

public sealed class UsageQueryService(ApplicationDbContext dbContext) : IUsageQueryService
{
    private const int DefaultMonthlyLimitChars = 200000;
    private const int DefaultRemainingOutlineCount = 40;
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    public async Task<UsageSummaryResponse> GetSummaryAsync(
        UsageActor actor,
        CancellationToken cancellationToken = default)
    {
        var usageLimit = await dbContext.UserUsageLimits
            .AsNoTracking()
            .FirstOrDefaultAsync(limit => limit.UserId == actor.UserId, cancellationToken);

        var defaultModel = await dbContext.AiModelSettings
            .AsNoTracking()
            .Where(model => model.Enabled)
            .OrderBy(model => model.SortOrder)
            .ThenBy(model => model.DisplayName)
            .Select(model => model.Model)
            .FirstOrDefaultAsync(cancellationToken);

        return new UsageSummaryResponse(
            usageLimit?.MonthlyLimitChars ?? DefaultMonthlyLimitChars,
            usageLimit?.RemainingOutlineCount ?? DefaultRemainingOutlineCount,
            defaultModel,
            MonthlyUsageAggregationEnabled: false);
    }

    public async Task<UsageLedgerListResponse> ListLedgersAsync(
        UsageActor actor,
        UsageLedgerQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, DefaultPage);
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var ledgers = dbContext.UsageLedgers
            .AsNoTracking()
            .Where(ledger => ledger.UserId == actor.UserId);

        if (query.From.HasValue)
        {
            var from = ToUtcStart(query.From.Value);
            ledgers = ledgers.Where(ledger => ledger.OccurredAt >= from);
        }

        if (query.To.HasValue)
        {
            var toExclusive = ToUtcStart(query.To.Value.AddDays(1));
            ledgers = ledgers.Where(ledger => ledger.OccurredAt < toExclusive);
        }

        var totalCount = await ledgers.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await ledgers
            .OrderByDescending(ledger => ledger.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ledger => new UsageLedgerItemResponse(
                ledger.Id,
                ledger.ArticleId,
                ledger.JobId,
                ledger.Provider,
                ledger.Model,
                ledger.Operation,
                ledger.PromptChars,
                ledger.OutputChars,
                ledger.UsageChars,
                ledger.OccurredAt))
            .ToListAsync(cancellationToken);

        return new UsageLedgerListResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            page > 1,
            totalPages > 0 && page < totalPages);
    }

    private static DateTimeOffset ToUtcStart(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
