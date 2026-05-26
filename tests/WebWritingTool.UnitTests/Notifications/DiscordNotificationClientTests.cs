using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Infrastructure.Notifications;

namespace WebWritingTool.UnitTests.Notifications;

public class DiscordNotificationClientTests
{
    private const string WebhookId = "webhook-id";
    private const string WebhookToken = "webhook-token";

    [Fact]
    public async Task SendAsync_WithSuccessfulResponse_PostsDiscordPayload()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("discord.com", request.RequestUri?.Host);
            Assert.Equal($"/api/webhooks/{WebhookId}/{WebhookToken}", request.RequestUri?.AbsolutePath);

            using var body = JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("通知テスト", body.RootElement.GetProperty("content").GetString());
            Assert.Equal("記事作成完了", body.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString());
            Assert.Empty(body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = CreateClient(httpClient);

        var result = await client.SendAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_WithRateLimitedResponse_ReturnsRetryableError()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return response;
        }));
        var client = CreateClient(httpClient);

        var result = await client.SendAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Equal(ExternalIntegrationErrorCodes.RateLimited, result.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(12), result.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WithInvalidWebhookUrl_ReturnsValidationErrorWithoutSending()
    {
        var sent = false;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            sent = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = CreateClient(httpClient);

        var result = await client.SendAsync(CreateRequest(webhookUrl: BuildWebhookUrl("example.com")));

        Assert.False(result.Success);
        Assert.Equal(ExternalIntegrationErrorCodes.ValidationError, result.ErrorCode);
        Assert.False(sent);
    }

    [Fact]
    public void Mask_DoesNotExposeWebhookIdOrToken()
    {
        var url = BuildWebhookUrl();

        Assert.True(DiscordWebhookUrl.TryNormalize(url, out var normalized, out _));
        var masked = DiscordWebhookUrl.Mask(normalized);

        Assert.Equal("https://discord.com/api/webhooks/.../...", masked);
        Assert.DoesNotContain(WebhookId, masked, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookToken, masked, StringComparison.Ordinal);
    }

    private static DiscordNotificationClient CreateClient(HttpClient httpClient)
    {
        return new DiscordNotificationClient(httpClient, NullLogger<DiscordNotificationClient>.Instance);
    }

    private static DiscordNotificationRequest CreateRequest(
        string? webhookUrl = null)
    {
        return new DiscordNotificationRequest(
            webhookUrl ?? BuildWebhookUrl(),
            "通知テスト",
            [
                new DiscordNotificationEmbed(
                    "記事作成完了",
                    "本文生成が完了しました。",
                    null,
                    5763719,
                    [new DiscordNotificationEmbedField("記事ID", "article-id", true)])
            ]);
    }

    private static string BuildWebhookUrl(string host = "discord.com")
    {
        return new UriBuilder(Uri.UriSchemeHttps, host)
        {
            Path = $"api/webhooks/{WebhookId}/{WebhookToken}"
        }.Uri.ToString().TrimEnd('/');
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
