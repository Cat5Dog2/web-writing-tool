namespace WebWritingTool.Application.Search;

public sealed class SearchCachePolicyOptions
{
    public const string SectionName = "SearchCache";

    public string Policy { get; init; } = SearchCachePolicies.Dev;
}

public static class SearchCachePolicies
{
    public const string Dev = "dev";
    public const string Staging = "staging";
    public const string Production = "production";
    public const string Strict = "strict";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Dev,
        Staging,
        Production,
        Strict
    };
}

public sealed record SearchCacheTtlSet(
    TimeSpan TavilyRawJsonTtl,
    TimeSpan TavilyContentTtl,
    TimeSpan TavilyMetadataTtl,
    TimeSpan XRawDataTtl,
    TimeSpan XMetadataTtl,
    bool RehydrateXBeforeDisplay);

public sealed record ResolvedSearchCacheTtl(
    DateTimeOffset FetchedAt,
    DateTimeOffset CacheExpiresAt,
    DateTimeOffset RawJsonExpiresAt,
    DateTimeOffset ContentExpiresAt,
    DateTimeOffset MetadataExpiresAt);

public sealed class SearchCachePolicyResolver(SearchCachePolicyOptions options)
{
    public SearchCacheTtlSet CurrentPolicy => ResolvePolicy(options.Policy);

    public ResolvedSearchCacheTtl ResolveTavily(
        DateTimeOffset fetchedAt,
        TopicRiskMode topicRiskMode,
        TimeSpan? searchCacheTtl = null,
        TimeSpan? contentCacheTtl = null)
    {
        var policy = AdjustForTopic(CurrentPolicy, topicRiskMode);
        var rawJsonTtl = ShortestPositive(policy.TavilyRawJsonTtl, searchCacheTtl);
        var contentTtl = ShortestPositive(policy.TavilyContentTtl, contentCacheTtl);
        var cacheTtl = rawJsonTtl <= contentTtl ? rawJsonTtl : contentTtl;

        return new ResolvedSearchCacheTtl(
            fetchedAt,
            fetchedAt.Add(cacheTtl),
            fetchedAt.Add(rawJsonTtl),
            fetchedAt.Add(contentTtl),
            fetchedAt.Add(policy.TavilyMetadataTtl));
    }

    public ResolvedSearchCacheTtl ResolveX(
        DateTimeOffset fetchedAt,
        TopicRiskMode topicRiskMode,
        TimeSpan? rawCacheTtl = null)
    {
        var policy = AdjustForTopic(CurrentPolicy, topicRiskMode);
        var rawTtl = ShortestPositive(policy.XRawDataTtl, rawCacheTtl);

        return new ResolvedSearchCacheTtl(
            fetchedAt,
            fetchedAt.Add(rawTtl),
            fetchedAt.Add(rawTtl),
            fetchedAt.Add(rawTtl),
            fetchedAt.Add(policy.XMetadataTtl));
    }

    public bool RequiresXRehydrationBeforeDisplay(TopicRiskMode topicRiskMode)
    {
        return CurrentPolicy.RehydrateXBeforeDisplay || topicRiskMode != TopicRiskMode.Normal;
    }

    public static SearchCacheTtlSet ResolvePolicy(string? policy)
    {
        return NormalizePolicy(policy) switch
        {
            SearchCachePolicies.Staging => new SearchCacheTtlSet(
                TimeSpan.FromHours(6),
                TimeSpan.FromHours(24),
                TimeSpan.FromDays(90),
                TimeSpan.FromHours(6),
                TimeSpan.FromDays(180),
                false),
            SearchCachePolicies.Production => new SearchCacheTtlSet(
                TimeSpan.FromHours(24),
                TimeSpan.FromDays(7),
                TimeSpan.FromDays(90),
                TimeSpan.FromHours(24),
                TimeSpan.FromDays(180),
                true),
            SearchCachePolicies.Strict => new SearchCacheTtlSet(
                TimeSpan.FromHours(24),
                TimeSpan.FromHours(24),
                TimeSpan.FromDays(90),
                TimeSpan.FromHours(1),
                TimeSpan.FromDays(180),
                true),
            _ => new SearchCacheTtlSet(
                TimeSpan.FromHours(24),
                TimeSpan.FromHours(24),
                TimeSpan.FromDays(90),
                TimeSpan.FromHours(6),
                TimeSpan.FromDays(180),
                false)
        };
    }

    public static string NormalizePolicy(string? policy)
    {
        return string.IsNullOrWhiteSpace(policy)
            ? SearchCachePolicies.Dev
            : policy.Trim().ToLowerInvariant();
    }

    private static SearchCacheTtlSet AdjustForTopic(SearchCacheTtlSet ttl, TopicRiskMode topicRiskMode)
    {
        if (topicRiskMode == TopicRiskMode.Normal)
        {
            return ttl;
        }

        return ttl with
        {
            TavilyRawJsonTtl = ttl.TavilyRawJsonTtl <= TimeSpan.FromHours(24)
                ? ttl.TavilyRawJsonTtl
                : TimeSpan.FromHours(24),
            TavilyContentTtl = ttl.TavilyContentTtl <= TimeSpan.FromHours(24)
                ? ttl.TavilyContentTtl
                : TimeSpan.FromHours(24),
            XRawDataTtl = ttl.XRawDataTtl <= TimeSpan.FromHours(1)
                ? ttl.XRawDataTtl
                : TimeSpan.FromHours(1),
            RehydrateXBeforeDisplay = true
        };
    }

    private static TimeSpan ShortestPositive(TimeSpan first, TimeSpan? second)
    {
        if (!second.HasValue || second.Value <= TimeSpan.Zero)
        {
            return first;
        }

        return first <= second.Value ? first : second.Value;
    }
}
