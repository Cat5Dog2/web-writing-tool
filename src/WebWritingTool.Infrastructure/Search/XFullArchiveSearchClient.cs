using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;

namespace WebWritingTool.Infrastructure.Search;

public sealed class XFullArchiveSearchClient(
    HttpClient httpClient,
    IOptions<SearchProviderOptions> options,
    ILogger<XFullArchiveSearchClient> logger)
    : IXFullArchiveSearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<XSearchPostResult>> SearchAsync(
        XFullArchiveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var xOptions = options.Value.X;
        EnsureConfigured(xOptions);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "X検索クエリが空です。");
        }

        var maxResults = ResolveMaxResults(request, xOptions);
        var query = BuildQuery(request);
        var endpoint = AppendQuery(
            xOptions.Endpoint,
            new Dictionary<string, string?>
            {
                ["query"] = query,
                ["max_results"] = maxResults.ToString("D"),
                ["tweet.fields"] = "author_id,created_at,lang",
                ["start_time"] = request.StartTime?.UtcDateTime.ToString("O"),
                ["end_time"] = request.EndTime?.UtcDateTime.ToString("O")
            });

        var startedAt = DateTimeOffset.UtcNow;
        using var response = await SendAsync(endpoint, xOptions.BearerToken, cancellationToken);
        var results = await ReadPostsAsync(response, cancellationToken);

        logger.LogInformation(
            "X full-archive search succeeded. resultCount={ResultCount} elapsedMs={ElapsedMs}",
            results.Count,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

        return results;
    }

    public async Task<IReadOnlyList<XSearchPostResult>> RehydrateAsync(
        XPostRehydrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var xOptions = options.Value.X;
        EnsureConfigured(xOptions);

        var ids = request.PostIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var endpoint = AppendQuery(
            ResolveTweetLookupEndpoint(xOptions.Endpoint),
            new Dictionary<string, string?>
            {
                ["ids"] = string.Join(",", ids),
                ["tweet.fields"] = "author_id,created_at,lang"
            });

        using var response = await SendAsync(endpoint, xOptions.BearerToken, cancellationToken);
        return await ReadPostsAsync(response, cancellationToken);
    }

    private static void EnsureConfigured(XProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BearerToken))
        {
            throw SearchExternalException.MissingCredential(SearchProviders.X);
        }
    }

    private static int ResolveMaxResults(
        XFullArchiveSearchRequest request,
        XProviderOptions options)
    {
        var maxAllowed = request.LargeResearchMode
            ? Math.Clamp(options.BulkMaxResults, 100, 500)
            : Math.Clamp(options.DefaultMaxResults, 10, 100);
        var requested = request.MaxResults <= 0 ? maxAllowed : request.MaxResults;

        if (requested > options.MonthlySafetyLimitPosts)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UsageLimitExceeded,
                "X検索の月間安全上限を超える可能性があるため実行できません。");
        }

        return Math.Clamp(requested, 10, maxAllowed);
    }

    private static string BuildQuery(XFullArchiveSearchRequest request)
    {
        var query = new StringBuilder(request.Query.Trim());
        var language = string.IsNullOrWhiteSpace(request.Language) ? "ja" : request.Language.Trim();
        if (!query.ToString().Contains("lang:", StringComparison.OrdinalIgnoreCase))
        {
            query.Append(" lang:").Append(language);
        }

        if (request.ExcludeRetweets
            && !query.ToString().Contains("-is:retweet", StringComparison.OrdinalIgnoreCase))
        {
            query.Append(" -is:retweet");
        }

        if (request.ExcludeReplies
            && !query.ToString().Contains("-is:reply", StringComparison.OrdinalIgnoreCase))
        {
            query.Append(" -is:reply");
        }

        return query.ToString();
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri endpoint,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        try
        {
            var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateException(response);
            }

            return response;
        }
        catch (ExternalIntegrationException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "X API検索がタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "X APIへの接続に失敗しました。",
                ex);
        }
    }

    private static async Task<IReadOnlyList<XSearchPostResult>> ReadPostsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<XSearchResponse>(
                JsonOptions,
                cancellationToken);

            return (payload?.Data ?? [])
                .Where(post => !string.IsNullOrWhiteSpace(post.Id))
                .Select(ToResult)
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalBadResponse,
                "X APIのレスポンス形式が不正です。",
                ex);
        }
    }

    private static XSearchPostResult ToResult(XPostDto post)
    {
        return new XSearchPostResult(
            post.Id.Trim(),
            string.IsNullOrWhiteSpace(post.AuthorId) ? null : post.AuthorId.Trim(),
            post.Text?.Trim() ?? string.Empty,
            $"https://x.com/i/web/status/{post.Id.Trim()}",
            string.IsNullOrWhiteSpace(post.Lang) ? null : post.Lang.Trim(),
            DateTimeOffset.TryParse(post.CreatedAt, out var postedAt) ? postedAt : null);
    }

    private static ExternalIntegrationException CreateException(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "X API検索条件が不正です。"),
            HttpStatusCode.Unauthorized => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
                "X APIの認証に失敗しました。"),
            HttpStatusCode.Forbidden => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ForbiddenExternalApi,
                "X APIの利用権限がありません。"),
            HttpStatusCode.TooManyRequests => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.RateLimited,
                "X APIのレート制限に達しました。",
                retryAfter: retryAfter),
            >= HttpStatusCode.InternalServerError => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalServerError,
                "X API側でエラーが発生しました。"),
            _ => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnknownExternalError,
                "X API連携に失敗しました。")
        };
    }

    private static Uri ResolveTweetLookupEndpoint(Uri searchEndpoint)
    {
        var builder = new UriBuilder(searchEndpoint);
        if (builder.Path.EndsWith("/2/tweets/search/all", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = builder.Path[..^"/search/all".Length];
            builder.Query = string.Empty;
            return builder.Uri;
        }

        return new Uri("https://api.x.com/2/tweets");
    }

    private static Uri AppendQuery(Uri endpoint, IReadOnlyDictionary<string, string?> parameters)
    {
        var builder = new UriBuilder(endpoint);
        var query = new StringBuilder();

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (query.Length > 0)
            {
                query.Append('&');
            }

            query
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        builder.Query = query.ToString();
        return builder.Uri;
    }

    private sealed record XSearchResponse(XPostDto[]? Data);

    private sealed record XPostDto(
        string Id,
        string? Text,
        [property: JsonPropertyName("author_id")] string? AuthorId,
        [property: JsonPropertyName("created_at")] string? CreatedAt,
        string? Lang);
}
