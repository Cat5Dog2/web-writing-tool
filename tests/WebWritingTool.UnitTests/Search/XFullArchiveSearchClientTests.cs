using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;
using WebWritingTool.Infrastructure.Search;

namespace WebWritingTool.UnitTests.Search;

public class XFullArchiveSearchClientTests
{
    [Fact]
    public async Task SearchAsync_WithSuccessfulResponse_MapsPostsAndAppliesFilters()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/2/tweets/search/all", request.RequestUri?.AbsolutePath);

            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("query=AI lang:ja -is:retweet -is:reply", query);
            Assert.Contains("max_results=100", query);
            Assert.Contains("tweet.fields=author_id,created_at,lang", query);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                    {
                      "data": [
                        {
                          "id": "123",
                          "text": "post text",
                          "author_id": "456",
                          "created_at": "2026-05-20T10:00:00Z",
                          "lang": "ja"
                        }
                      ]
                    }
                    """)
            };
        }));
        var client = CreateClient(httpClient);

        var results = await client.SearchAsync(new XFullArchiveSearchRequest(
            "AI",
            "ja",
            StartTime: null,
            EndTime: null,
            MaxResults: 120,
            LargeResearchMode: false,
            ExcludeRetweets: true,
            ExcludeReplies: true,
            RawCacheTtl: null,
            RehydrateBeforeDisplay: false));

        var result = Assert.Single(results);
        Assert.Equal("123", result.PostId);
        Assert.Equal("456", result.AuthorId);
        Assert.Equal("post text", result.Text);
        Assert.Equal("https://x.com/i/web/status/123", result.Url);
        Assert.Equal("ja", result.Language);
        Assert.Equal(DateTimeOffset.Parse("2026-05-20T10:00:00Z"), result.PostedAt);
    }

    [Fact]
    public async Task SearchAsync_WithLargeResearchMode_AllowsBulkMaxResults()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("max_results=500", query);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{ "data": [] }""")
            };
        }));
        var client = CreateClient(httpClient);

        var results = await client.SearchAsync(new XFullArchiveSearchRequest(
            "AI",
            "ja",
            null,
            null,
            600,
            LargeResearchMode: true,
            ExcludeRetweets: true,
            ExcludeReplies: true,
            RawCacheTtl: null,
            RehydrateBeforeDisplay: false));

        Assert.Empty(results);
    }

    [Fact]
    public async Task RehydrateAsync_UsesTweetLookupEndpoint()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/2/tweets", request.RequestUri?.AbsolutePath);
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("ids=123,456", query);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                    {
                      "data": [
                        { "id": "123", "text": "fresh text", "author_id": "456", "lang": "ja" }
                      ]
                    }
                    """)
            };
        }));
        var client = CreateClient(httpClient);

        var results = await client.RehydrateAsync(new XPostRehydrationRequest(["123", "456"]));

        var result = Assert.Single(results);
        Assert.Equal("123", result.PostId);
        Assert.Equal("fresh text", result.Text);
    }

    [Fact]
    public async Task SearchAsync_WithoutBearerToken_ThrowsUnauthorizedException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
                throw new InvalidOperationException("HTTP should not be called."))));
        var client = CreateClient(httpClient, bearerToken: "");

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.SearchAsync(new XFullArchiveSearchRequest(
                "AI",
                "ja",
                null,
                null,
                100,
                false,
                true,
                true,
                null,
                false)));

        Assert.Equal(ExternalIntegrationErrorCodes.UnauthorizedExternalApi, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WhenMonthlySafetyLimitWouldBeExceeded_ThrowsUsageLimitException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
                throw new InvalidOperationException("HTTP should not be called."))));
        var client = CreateClient(httpClient, monthlySafetyLimitPosts: 50);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.SearchAsync(new XFullArchiveSearchRequest(
                "AI",
                "ja",
                null,
                null,
                100,
                false,
                true,
                true,
                null,
                false)));

        Assert.Equal(ExternalIntegrationErrorCodes.UsageLimitExceeded, exception.ErrorCode);
    }

    private static XFullArchiveSearchClient CreateClient(
        HttpClient httpClient,
        string bearerToken = "test-token",
        int monthlySafetyLimitPosts = 10000)
    {
        return new XFullArchiveSearchClient(
            httpClient,
            Options.Create(new SearchProviderOptions
            {
                X = new XProviderOptions
                {
                    BearerToken = bearerToken,
                    MonthlySafetyLimitPosts = monthlySafetyLimitPosts
                }
            }),
            NullLogger<XFullArchiveSearchClient>.Instance);
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
