using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;
using WebWritingTool.Infrastructure.Search;

namespace WebWritingTool.UnitTests.Search;

public class TavilyWebSearchClientTests
{
    [Fact]
    public async Task SearchAsync_WithSuccessfulResponse_MapsCommonResults()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.tavily.com/search", request.RequestUri?.ToString());

            var body = await request.Content!.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal("test-key", json.RootElement.GetProperty("api_key").GetString());
            Assert.Equal("basic", json.RootElement.GetProperty("search_depth").GetString());
            Assert.Equal(3, json.RootElement.GetProperty("max_results").GetInt32());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                    {
                      "results": [
                        { "title": "Result A", "url": "https://example.com/a", "content": "Snippet A" },
                        { "title": "Result B", "url": "https://example.com/b", "content": "Snippet B" }
                      ]
                    }
                    """)
            };
        }));
        var client = CreateClient(httpClient);

        var results = await client.SearchAsync(new WebSearchRequest(
            "AI ライティング",
            "Japan",
            "ja",
            3,
            DomesticOnly: true,
            Topic: null,
            SearchDepth: "basic",
            StartDate: null,
            EndDate: null,
            SearchCacheTtl: null,
            ContentCacheTtl: null));

        Assert.Equal(2, results.Count);
        Assert.Equal("Result A", results[0].Title);
        Assert.Equal("https://example.com/a", results[0].Url);
        Assert.Equal("Snippet A", results[0].Snippet);
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(SearchProviders.Tavily, results[0].Provider);
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ThrowsUnauthorizedException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
                throw new InvalidOperationException("HTTP should not be called."))));
        var client = CreateClient(httpClient, apiKey: "");

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.SearchAsync(new WebSearchRequest(
                "query",
                null,
                null,
                10,
                false,
                null,
                "basic",
                null,
                null,
                null,
                null)));

        Assert.Equal(ExternalIntegrationErrorCodes.UnauthorizedExternalApi, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WithRateLimitedResponse_ThrowsRateLimitedException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        }));
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.SearchAsync(new WebSearchRequest(
                "query",
                null,
                null,
                10,
                false,
                null,
                "basic",
                null,
                null,
                null,
                null)));

        Assert.Equal(ExternalIntegrationErrorCodes.RateLimited, exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    private static TavilyWebSearchClient CreateClient(HttpClient httpClient, string apiKey = "test-key")
    {
        return new TavilyWebSearchClient(
            httpClient,
            Options.Create(new SearchProviderOptions
            {
                Tavily = new TavilyProviderOptions
                {
                    ApiKey = apiKey
                }
            }),
            NullLogger<TavilyWebSearchClient>.Instance);
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }
}
