using WebWritingTool.Application.Search;

namespace WebWritingTool.UnitTests.Search;

public class SearchQueryNormalizerTests
{
    [Fact]
    public void NormalizeWeb_ReturnsSameHashForEquivalentConditions()
    {
        var first = SearchQueryNormalizer.NormalizeWeb(new WebSearchRequest(
            "  AI   Writing  ",
            "Japan",
            "JA",
            10,
            DomesticOnly: true,
            Topic: "General",
            SearchDepth: "Basic",
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 5, 23),
            SearchCacheTtl: null,
            ContentCacheTtl: null));

        var second = SearchQueryNormalizer.NormalizeWeb(new WebSearchRequest(
            "ai writing",
            " japan ",
            "ja",
            10,
            DomesticOnly: true,
            Topic: "general",
            SearchDepth: "basic",
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 5, 23),
            SearchCacheTtl: null,
            ContentCacheTtl: null));

        Assert.Equal(first.QueryHash, second.QueryHash);
    }

    [Fact]
    public void NormalizeWeb_ReturnsDifferentHashWhenConditionsDiffer()
    {
        var first = SearchQueryNormalizer.NormalizeWeb(new WebSearchRequest(
            "AI Writing",
            "Japan",
            "ja",
            10,
            true,
            null,
            "basic",
            null,
            null,
            null,
            null));

        var second = SearchQueryNormalizer.NormalizeWeb(new WebSearchRequest(
            "AI Writing",
            "Japan",
            "ja",
            20,
            true,
            null,
            "basic",
            null,
            null,
            null,
            null));

        Assert.NotEqual(first.QueryHash, second.QueryHash);
    }

    [Fact]
    public void NormalizeX_IncludesDateRangeAndFilters()
    {
        var normalized = SearchQueryNormalizer.NormalizeX(new XFullArchiveSearchRequest(
            "AI",
            "ja",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero),
            100,
            LargeResearchMode: false,
            ExcludeRetweets: true,
            ExcludeReplies: true,
            RawCacheTtl: null,
            RehydrateBeforeDisplay: false));

        Assert.Equal(SearchProviders.X, normalized.Values["provider"]);
        Assert.Equal("ai", normalized.Values["query"]);
        Assert.Equal("true", normalized.Values["excludeRetweets"]);
        Assert.Equal("2026-05-01T00:00:00.0000000Z", normalized.Values["startTime"]);
        Assert.False(string.IsNullOrWhiteSpace(normalized.QueryHash));
    }
}
