using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class HealthEndpointTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task HealthEndpoints_WithRequireHttps_AreNotRedirected()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString, requireHttps: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // The test host removes hosted services, so /health/ready may legitimately report 503.
        // The point here is that the request reaches the health endpoint without an HTTPS redirect.
        Assert.Null(ready.Headers.Location);
        Assert.Contains(
            ready.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
    }

    [Fact]
    public async Task NonHealthEndpoint_WithRequireHttps_RedirectsToHttps()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString, requireHttps: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith(
            "https://",
            response.Headers.Location?.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthDeps_WithRequireHttps_RedirectsToHttps()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString, requireHttps: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/deps");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith(
            "https://",
            response.Headers.Location?.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
