using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Notifications;

namespace WebWritingTool.Infrastructure.Notifications;

public sealed class DiscordNotificationClient(
    HttpClient httpClient,
    ILogger<DiscordNotificationClient> logger)
    : IDiscordNotificationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<DiscordNotificationResult> SendAsync(
        DiscordNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var sentAt = DateTimeOffset.UtcNow;
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return new DiscordNotificationResult(
                false,
                ExternalIntegrationErrorCodes.ValidationError,
                validationError,
                null,
                sentAt);
        }

        DiscordWebhookUrl.TryNormalize(request.WebhookUrl, out var normalizedUrl, out _);

        using var message = new HttpRequestMessage(HttpMethod.Post, normalizedUrl);
        message.Content = JsonContent.Create(CreatePayload(request), options: JsonOptions);

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            sentAt = DateTimeOffset.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Discord notification succeeded. statusCode={StatusCode}", (int)response.StatusCode);
                return new DiscordNotificationResult(true, null, null, null, sentAt);
            }

            var errorCode = MapErrorCode(response.StatusCode);
            return new DiscordNotificationResult(
                false,
                errorCode,
                ToUserMessage(errorCode),
                response.Headers.RetryAfter?.Delta,
                sentAt);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "Discord通知がタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "Discord通知の送信に失敗しました。",
                ex);
        }
    }

    private static string? Validate(DiscordNotificationRequest request)
    {
        if (!DiscordWebhookUrl.TryNormalize(request.WebhookUrl, out _, out var errorMessage))
        {
            return errorMessage ?? "Discord Webhook URLが不正です。";
        }

        var contentLength = request.Content.Trim().Length;
        if (contentLength is < 1 or > 2000)
        {
            return "Discord通知本文は1から2000文字で指定してください。";
        }

        if (request.Embeds.Count > 10)
        {
            return "Discord通知のEmbedは10件以内で指定してください。";
        }

        return null;
    }

    private static DiscordWebhookPayload CreatePayload(DiscordNotificationRequest request)
    {
        return new DiscordWebhookPayload(
            request.Content.Trim(),
            request.Embeds.Select(embed => new DiscordWebhookEmbed(
                embed.Title.Trim(),
                string.IsNullOrWhiteSpace(embed.Description) ? null : embed.Description.Trim(),
                string.IsNullOrWhiteSpace(embed.Url) ? null : embed.Url.Trim(),
                embed.Color,
                embed.Fields.Select(field => new DiscordWebhookEmbedField(
                    field.Name.Trim(),
                    field.Value.Trim(),
                    field.Inline)).ToArray())).ToArray(),
            new DiscordAllowedMentions([], [], [], false));
    }

    private static string MapErrorCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => ExternalIntegrationErrorCodes.ValidationError,
            HttpStatusCode.Unauthorized => ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
            HttpStatusCode.Forbidden => ExternalIntegrationErrorCodes.ForbiddenExternalApi,
            HttpStatusCode.NotFound => ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
            HttpStatusCode.RequestTimeout => ExternalIntegrationErrorCodes.Timeout,
            HttpStatusCode.TooManyRequests => ExternalIntegrationErrorCodes.RateLimited,
            >= HttpStatusCode.InternalServerError => ExternalIntegrationErrorCodes.ExternalServerError,
            _ => ExternalIntegrationErrorCodes.UnknownExternalError
        };
    }

    private static string ToUserMessage(string errorCode)
    {
        return errorCode switch
        {
            ExternalIntegrationErrorCodes.ValidationError => "Discord通知の入力が不正です。",
            ExternalIntegrationErrorCodes.UnauthorizedExternalApi => "Discord Webhook URLが無効です。",
            ExternalIntegrationErrorCodes.ForbiddenExternalApi => "Discord Webhookの利用権限がありません。",
            ExternalIntegrationErrorCodes.Timeout => "Discord通知がタイムアウトしました。",
            ExternalIntegrationErrorCodes.RateLimited => "Discord通知のレート制限に達しました。",
            ExternalIntegrationErrorCodes.ExternalServerError => "Discord側でエラーが発生しました。",
            _ => "Discord通知の送信に失敗しました。"
        };
    }

    private sealed record DiscordWebhookPayload(
        string Content,
        IReadOnlyList<DiscordWebhookEmbed> Embeds,
        [property: JsonPropertyName("allowed_mentions")]
        DiscordAllowedMentions AllowedMentions);

    private sealed record DiscordWebhookEmbed(
        string Title,
        string? Description,
        string? Url,
        int? Color,
        IReadOnlyList<DiscordWebhookEmbedField> Fields);

    private sealed record DiscordWebhookEmbedField(
        string Name,
        string Value,
        bool Inline);

    private sealed record DiscordAllowedMentions(
        IReadOnlyList<string> Parse,
        IReadOnlyList<string> Users,
        IReadOnlyList<string> Roles,
        [property: JsonPropertyName("replied_user")]
        bool RepliedUser);
}
