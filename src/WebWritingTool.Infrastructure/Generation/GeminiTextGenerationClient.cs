using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;

namespace WebWritingTool.Infrastructure.Generation;

public sealed class GeminiTextGenerationClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiTextGenerationClient> logger)
    : IAiTextGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var geminiOptions = options.Value;
        if (string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
                "Gemini APIキーが設定されていません。");
        }

        var model = ResolveModel(request.Model, geminiOptions.Model);
        if (geminiOptions.MaxInputChars.HasValue && request.PromptChars > geminiOptions.MaxInputChars.Value)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "AI生成の入力文字数が上限を超えています。");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", geminiOptions.ApiKey);
        message.Content = JsonContent.Create(CreateRequest(request), options: JsonOptions);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                throw CreateException(response);
            }

            var payload = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(
                JsonOptions,
                cancellationToken);

            var text = ExtractText(payload);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ExternalIntegrationException(
                    ExternalIntegrationErrorCodes.ExternalBadResponse,
                    "Gemini APIのレスポンス形式が不正です。");
            }

            logger.LogInformation(
                "Gemini generation succeeded. operation={Operation} model={Model} elapsedMs={ElapsedMs}",
                request.Operation,
                model,
                stopwatch.ElapsedMilliseconds);

            return new AiTextGenerationResult(
                text,
                AiProviders.Gemini,
                model,
                request.PromptChars,
                text.Length,
                payload?.ResponseId);
        }
        catch (ExternalIntegrationException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "Gemini APIがタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "Gemini APIへの接続に失敗しました。",
                ex);
        }
        catch (JsonException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalBadResponse,
                "Gemini APIのレスポンス形式が不正です。",
                ex);
        }
    }

    private static string ResolveModel(string requestModel, string optionModel)
    {
        var model = string.IsNullOrWhiteSpace(requestModel) ? optionModel : requestModel;
        model = model.Trim();
        return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model["models/".Length..]
            : model;
    }

    private static GeminiGenerateContentRequest CreateRequest(AiTextGenerationRequest request)
    {
        return new GeminiGenerateContentRequest(
            string.IsNullOrWhiteSpace(request.SystemInstruction)
                ? null
                : new GeminiContent(
                    null,
                    [new GeminiPart(request.SystemInstruction)]),
            [
                new GeminiContent(
                    "user",
                    [new GeminiPart(request.UserPrompt)])
            ],
            request.Temperature.HasValue
                ? new GeminiGenerationConfig(request.Temperature.Value)
                : null);
    }

    private static ExternalIntegrationException CreateException(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "Gemini APIの入力が不正です。"),
            HttpStatusCode.Unauthorized => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
                "Gemini APIの認証に失敗しました。"),
            HttpStatusCode.Forbidden => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ForbiddenExternalApi,
                "Gemini APIの利用権限がありません。"),
            HttpStatusCode.RequestTimeout => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "Gemini APIがタイムアウトしました。"),
            HttpStatusCode.TooManyRequests => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.RateLimited,
                "Gemini APIのレート制限に達しました。",
                retryAfter: retryAfter),
            >= HttpStatusCode.InternalServerError => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalServerError,
                "Gemini API側でエラーが発生しました。"),
            _ => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnknownExternalError,
                "Gemini API連携に失敗しました。")
        };
    }

    private static string? ExtractText(GeminiGenerateContentResponse? payload)
    {
        return payload?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Aggregate(
                seed: null as string,
                func: (current, text) => current is null ? text : current + Environment.NewLine + text);
    }

    private sealed record GeminiGenerateContentRequest(
        [property: JsonPropertyName("system_instruction")]
        GeminiContent? SystemInstruction,
        IReadOnlyList<GeminiContent> Contents,
        GeminiGenerationConfig? GenerationConfig);

    private sealed record GeminiGenerationConfig(double Temperature);

    private sealed record GeminiContent(
        string? Role,
        IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(string Text);

    private sealed record GeminiGenerateContentResponse(
        IReadOnlyList<GeminiCandidate>? Candidates,
        string? ResponseId);

    private sealed record GeminiCandidate(GeminiContentResponse? Content);

    private sealed record GeminiContentResponse(IReadOnlyList<GeminiPartResponse>? Parts);

    private sealed record GeminiPartResponse(string? Text);
}
