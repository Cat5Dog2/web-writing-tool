namespace WebWritingTool.Infrastructure.Search;

public sealed class SearchProviderOptions
{
    public const string SectionName = "SearchProviders";

    public string DefaultRegion { get; init; } = "Japan";

    public TavilyProviderOptions Tavily { get; init; } = new();

    public XProviderOptions X { get; init; } = new();
}

public sealed class TavilyProviderOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public Uri Endpoint { get; init; } = new("https://api.tavily.com/search");

    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class XProviderOptions
{
    public string BearerToken { get; init; } = string.Empty;

    public Uri Endpoint { get; init; } = new("https://api.x.com/2/tweets/search/all");

    public int TimeoutSeconds { get; init; } = 30;

    public int DefaultMaxResults { get; init; } = 100;

    public int BulkMaxResults { get; init; } = 500;

    public int MonthlySafetyLimitPosts { get; init; } = 10000;
}
