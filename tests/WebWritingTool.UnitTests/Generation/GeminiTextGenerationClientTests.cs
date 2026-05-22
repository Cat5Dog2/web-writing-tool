using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Infrastructure.Generation;

namespace WebWritingTool.UnitTests.Generation;

public class GeminiTextGenerationClientTests
{
    [Fact]
    public async Task GenerateAsync_WithSuccessfulResponse_ReturnsTextResult()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent",
                request.RequestUri?.ToString());
            Assert.True(request.Headers.TryGetValues("x-goog-api-key", out var values));
            Assert.Equal("test-key", Assert.Single(values));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "生成本文" }
                            ]
                          }
                        }
                      ],
                      "responseId": "response-1"
                    }
                    """)
            };
        }))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = CreateClient(httpClient, apiKey: "test-key");

        var result = await client.GenerateAsync(new AiTextGenerationRequest(
            AiProviders.Gemini,
            "gemini-3.1-pro-preview",
            AiOperations.BodyGeneration,
            "system",
            "user",
            null,
            0.2,
            []));

        Assert.Equal("生成本文", result.Text);
        Assert.Equal(AiProviders.Gemini, result.Provider);
        Assert.Equal("gemini-3.1-pro-preview", result.Model);
        Assert.Equal("response-1", result.RawResponseId);
        Assert.Equal("systemuser".Length, result.PromptChars);
        Assert.Equal("生成本文".Length, result.OutputChars);
    }

    [Fact]
    public async Task GenerateAsync_WithRateLimitedResponse_ThrowsRetryableException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = CreateClient(httpClient, apiKey: "test-key");

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.GenerateAsync(CreateRequest()));

        Assert.Equal(ExternalIntegrationErrorCodes.RateLimited, exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task GenerateAsync_WithoutApiKey_ThrowsUnauthorizedException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP should not be called.")))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = CreateClient(httpClient, apiKey: "");

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.GenerateAsync(CreateRequest()));

        Assert.Equal(ExternalIntegrationErrorCodes.UnauthorizedExternalApi, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_WithMalformedSuccessResponse_ThrowsBadResponseException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{ "candidates": [] }""")
            }))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = CreateClient(httpClient, apiKey: "test-key");

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.GenerateAsync(CreateRequest()));

        Assert.Equal(ExternalIntegrationErrorCodes.ExternalBadResponse, exception.ErrorCode);
    }

    private static GeminiTextGenerationClient CreateClient(HttpClient httpClient, string apiKey)
    {
        return new GeminiTextGenerationClient(
            httpClient,
            Options.Create(new GeminiOptions { ApiKey = apiKey }),
            NullLogger<GeminiTextGenerationClient>.Instance);
    }

    private static AiTextGenerationRequest CreateRequest()
    {
        return new AiTextGenerationRequest(
            AiProviders.Gemini,
            "gemini-3.1-pro-preview",
            AiOperations.BodyGeneration,
            "system",
            "user",
            null,
            null,
            []);
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
