namespace WebWritingTool.Application.Usage;

public sealed record UsageActor(string UserId, bool IsAdmin);

public sealed record UsageSummaryResponse(
    int MonthlyLimitChars,
    int RemainingOutlineCount,
    string? DefaultModel,
    bool MonthlyUsageAggregationEnabled);

public sealed record UsageLedgerQuery(
    int Page,
    int PageSize,
    DateOnly? From,
    DateOnly? To);

public sealed record UsageLedgerItemResponse(
    Guid Id,
    Guid? ArticleId,
    Guid? JobId,
    string Provider,
    string Model,
    string Operation,
    int PromptChars,
    int OutputChars,
    int UsageChars,
    DateTimeOffset OccurredAt);

public sealed record UsageLedgerListResponse(
    IReadOnlyList<UsageLedgerItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public interface IUsageQueryService
{
    Task<UsageSummaryResponse> GetSummaryAsync(
        UsageActor actor,
        CancellationToken cancellationToken = default);

    Task<UsageLedgerListResponse> ListLedgersAsync(
        UsageActor actor,
        UsageLedgerQuery query,
        CancellationToken cancellationToken = default);
}
