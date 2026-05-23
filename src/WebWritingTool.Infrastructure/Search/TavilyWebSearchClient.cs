using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;

namespace WebWritingTool.Infrastructure.Search;

public sealed class TavilyWebSearchClient(
    HttpClient httpClient,
    IOptions<SearchProviderOptions> options,
    ILogger<TavilyWebSearchClient> logger)
    : IWebSearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var tavilyOptions = options.Value.Tavily;
        if (string.IsNullOrWhiteSpace(tavilyOptions.ApiKey))
        {
            throw SearchExternalException.MissingCredential(SearchProviders.Tavily);
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "検索クエリが空です。");
        }

        var startedAt = DateTimeOffset.UtcNow;
        using var message = new HttpRequestMessage(HttpMethod.Post, tavilyOptions.Endpoint);
        message.Content = JsonContent.Create(CreateRequest(request, tavilyOptions.ApiKey), options: JsonOptions);

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateException(response);
            }

            var payload = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(
                JsonOptions,
                cancellationToken);

            var results = payload?.Results ?? [];
            var mapped = results
                .Where(result => !string.IsNullOrWhiteSpace(result.Url))
                .Select((result, index) => new WebSearchResult(
                    result.Title?.Trim() ?? string.Empty,
                    result.Url.Trim(),
                    result.Content?.Trim(),
                    index + 1,
                    SearchProviders.Tavily))
                .ToArray();

            logger.LogInformation(
                "Tavily search succeeded. resultCount={ResultCount} elapsedMs={ElapsedMs}",
                mapped.Length,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return mapped;
        }
        catch (ExternalIntegrationException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "Tavily検索がタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "Tavily検索への接続に失敗しました。",
                ex);
        }
        catch (JsonException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalBadResponse,
                "Tavily検索のレスポンス形式が不正です。",
                ex);
        }
    }

    private static TavilySearchRequest CreateRequest(WebSearchRequest request, string apiKey)
    {
        return new TavilySearchRequest(
            apiKey,
            request.Query.Trim(),
            string.IsNullOrWhiteSpace(request.SearchDepth) ? "basic" : request.SearchDepth.Trim(),
            string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim(),
            Math.Clamp(request.MaxResults, 1, 20),
            request.DomesticOnly ? "japan" : null,
            false,
            false,
            false);
    }

    private static ExternalIntegrationException CreateException(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "Tavily検索の入力が不正です。"),
            HttpStatusCode.Unauthorized => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
                "Tavily APIの認証に失敗しました。"),
            HttpStatusCode.Forbidden => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ForbiddenExternalApi,
                "Tavily APIの利用権限がありません。"),
            HttpStatusCode.TooManyRequests => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.RateLimited,
                "Tavily APIのレート制限に達しました。",
                retryAfter: retryAfter),
            >= HttpStatusCode.InternalServerError => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalServerError,
                "Tavily API側でエラーが発生しました。"),
            _ => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnknownExternalError,
                "Tavily検索に失敗しました。")
        };
    }

    private sealed record TavilySearchRequest(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("topic")] string? Topic,
        [property: JsonPropertyName("max_results")] int MaxResults,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer,
        [property: JsonPropertyName("include_raw_content")] bool IncludeRawContent,
        [property: JsonPropertyName("include_images")] bool IncludeImages);

    private sealed record TavilySearchResponse(TavilySearchResult[]? Results);

    private sealed record TavilySearchResult(
        string? Title,
        string Url,
        string? Content,
        decimal? Score,
        [property: JsonPropertyName("raw_content")] string? RawContent);
}
