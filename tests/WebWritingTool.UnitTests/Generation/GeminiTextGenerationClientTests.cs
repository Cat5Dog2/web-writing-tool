using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.7-flash:generateContent",
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
            "gemini-3.7-flash",
            AiOperations.BodyGeneration,
            "system",
            "user",
            null,
            0.2,
            []));

        Assert.Equal("生成本文", result.Text);
        Assert.Equal(AiProviders.Gemini, result.Provider);
        Assert.Equal("gemini-3.7-flash", result.Model);
        Assert.Equal("response-1", result.RawResponseId);
        Assert.Equal("systemuser".Length, result.PromptChars);
        Assert.Equal("生成本文".Length, result.OutputChars);
    }

    [Fact]
    public async Task GenerateAsync_WithoutRequestModel_UsesConfiguredDefaultModel()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(
                $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiOptions.DefaultModel}:generateContent",
                request.RequestUri?.ToString());

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
                      ]
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
            string.Empty,
            AiOperations.BodyGeneration,
            "system",
            "user",
            null,
            null,
            []));

        Assert.Equal("gemini-3.7-flash", result.Model);
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

    // Gemini 3.xではtemperature、top_p、top_kが全リクエストから削除するよう案内されており、
    // 将来モデルでは送信するとHTTP 400になる。3.6と3.5も対象に含む。
    [Theory]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.6-flash")]
    [InlineData("gemini-3.5-flash")]
    [InlineData("gemini-4-flash-future")]
    public async Task GenerateAsync_WithAnyModel_NeverSendsSamplingParameters(string model)
    {
        var handler = new StubHttpMessageHandler(_ => SuccessResponse());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = CreateClient(httpClient, apiKey: "test-key");

        await client.GenerateAsync(CreateRequest(model, temperature: 0.5));

        Assert.NotNull(handler.LastRequestBody);
        using var document = JsonDocument.Parse(handler.LastRequestBody);
        Assert.False(document.RootElement.TryGetProperty("generationConfig", out _));
        Assert.DoesNotContain("temperature", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top_p", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top_k", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    private static GeminiTextGenerationClient CreateClient(HttpClient httpClient, string apiKey)
    {
        return new GeminiTextGenerationClient(
            httpClient,
            Options.Create(new GeminiOptions { ApiKey = apiKey }),
            NullLogger<GeminiTextGenerationClient>.Instance);
    }

    private static HttpResponseMessage SuccessResponse()
    {
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
                  ]
                }
                """)
        };
    }

    private static AiTextGenerationRequest CreateRequest(string model, double? temperature)
    {
        return new AiTextGenerationRequest(
            AiProviders.Gemini,
            model,
            AiOperations.BodyGeneration,
            "system",
            "user",
            null,
            temperature,
            []);
    }

    private static AiTextGenerationRequest CreateRequest()
    {
        return new AiTextGenerationRequest(
            AiProviders.Gemini,
            "gemini-3.7-flash",
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
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return handler(request);
        }
    }
}
