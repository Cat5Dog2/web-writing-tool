using WebWritingTool.Application.Search;

namespace WebWritingTool.UnitTests.Search;

public class SearchCachePolicyResolverTests
{
    [Theory]
    [InlineData("dev", 24, 24, 6, false)]
    [InlineData("staging", 6, 24, 6, false)]
    [InlineData("production", 24, 168, 24, true)]
    [InlineData("strict", 24, 24, 1, true)]
    public void ResolvePolicy_ReturnsEnvironmentTtl(
        string policy,
        int tavilyRawHours,
        int tavilyContentHours,
        int xRawHours,
        bool rehydrateX)
    {
        var ttl = SearchCachePolicyResolver.ResolvePolicy(policy);

        Assert.Equal(TimeSpan.FromHours(tavilyRawHours), ttl.TavilyRawJsonTtl);
        Assert.Equal(TimeSpan.FromHours(tavilyContentHours), ttl.TavilyContentTtl);
        Assert.Equal(TimeSpan.FromHours(xRawHours), ttl.XRawDataTtl);
        Assert.Equal(rehydrateX, ttl.RehydrateXBeforeDisplay);
    }

    [Fact]
    public void ResolveTavily_UsesShortestRequestedTtl()
    {
        var now = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var resolver = new SearchCachePolicyResolver(new SearchCachePolicyOptions
        {
            Policy = SearchCachePolicies.Production
        });

        var resolved = resolver.ResolveTavily(
            now,
            TopicRiskMode.Normal,
            searchCacheTtl: TimeSpan.FromHours(2),
            contentCacheTtl: TimeSpan.FromHours(3));

        Assert.Equal(now.AddHours(2), resolved.CacheExpiresAt);
        Assert.Equal(now.AddHours(2), resolved.RawJsonExpiresAt);
        Assert.Equal(now.AddHours(3), resolved.ContentExpiresAt);
    }

    [Fact]
    public void ResolveX_StrictTopicShortensRawTtlAndRequiresRehydration()
    {
        var now = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var resolver = new SearchCachePolicyResolver(new SearchCachePolicyOptions
        {
            Policy = SearchCachePolicies.Production
        });

        var resolved = resolver.ResolveX(now, TopicRiskMode.Strict);

        Assert.Equal(now.AddHours(1), resolved.CacheExpiresAt);
        Assert.Equal(now.AddHours(1), resolved.ContentExpiresAt);
        Assert.True(resolver.RequiresXRehydrationBeforeDisplay(TopicRiskMode.Strict));
    }
}
