using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Infrastructure.Wordpress;

namespace WebWritingTool.UnitTests.Wordpress;

public class WordpressClientTests
{
    [Fact]
    public async Task TestConnectionAsync_WithUnauthorizedResponse_ReturnsSuccessFalse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "https://example.com/wp-json/wp/v2/users/me?context=edit",
                request.RequestUri?.ToString());
            AssertAuthorizationHeader(request);

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }));
        var client = CreateClient(httpClient);

        var result = await client.TestConnectionAsync(CreateConnection());

        Assert.False(result.Success);
        Assert.DoesNotContain("app-pass", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithSuccessfulResponse_ReturnsCategories()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                    [
                      { "id": 16, "name": "MBTI", "slug": "mbti" },
                      { "id": 20, "name": "SEO &amp; AI", "slug": "seo-ai" }
                    ]
                    """)
            }));
        var client = CreateClient(httpClient);

        var categories = await client.GetCategoriesAsync(CreateConnection());

        Assert.Collection(
            categories,
            category =>
            {
                Assert.Equal(16, category.Id);
                Assert.Equal("MBTI", category.Name);
                Assert.Equal("mbti", category.Slug);
            },
            category =>
            {
                Assert.Equal(20, category.Id);
                Assert.Equal("SEO & AI", category.Name);
                Assert.Equal("seo-ai", category.Slug);
            });
    }

    [Fact]
    public async Task CreatePostAsync_WithDraftRequest_SendsWordpressPostBody()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://example.com/wp-json/wp/v2/posts",
                request.RequestUri?.ToString());
            AssertAuthorizationHeader(request);

            using var body = JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("投稿タイトル", body.RootElement.GetProperty("title").GetString());
            Assert.Equal("<h2>見出し</h2>", body.RootElement.GetProperty("content").GetString());
            Assert.Equal("draft", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(16, body.RootElement.GetProperty("categories")[0].GetInt32());

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent("""{ "id": 123, "link": "https://example.com/post/sample" }""")
            };
        }));
        var client = CreateClient(httpClient);

        var result = await client.CreatePostAsync(new WordpressPostRequest(
            CreateConnection(),
            "投稿タイトル",
            "<h2>見出し</h2>",
            16,
            WordpressPostStatuses.Draft));

        Assert.True(result.Success);
        Assert.Equal(123, result.PostId);
        Assert.Equal("https://example.com/post/sample", result.PostUrl);
    }

    [Fact]
    public async Task CreatePostAsync_WithRateLimitedResponse_ReturnsRetryableErrorCode()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var client = CreateClient(httpClient);

        var result = await client.CreatePostAsync(new WordpressPostRequest(
            CreateConnection(),
            "投稿タイトル",
            "<p>本文</p>",
            null,
            WordpressPostStatuses.Draft));

        Assert.False(result.Success);
        Assert.Equal(ExternalIntegrationErrorCodes.RateLimited, result.ErrorCode);
    }

    private static WordpressClient CreateClient(HttpClient httpClient)
    {
        return new WordpressClient(httpClient, NullLogger<WordpressClient>.Instance);
    }

    private static WordpressSiteConnection CreateConnection()
    {
        return new WordpressSiteConnection("https://example.com", "writer", "app-pass");
    }

    private static void AssertAuthorizationHeader(HttpRequestMessage request)
    {
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("writer:app-pass"));
        Assert.Equal(expected, request.Headers.Authorization.Parameter);
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
