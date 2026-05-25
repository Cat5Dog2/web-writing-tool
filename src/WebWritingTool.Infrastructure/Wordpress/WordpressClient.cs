using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Wordpress;

namespace WebWritingTool.Infrastructure.Wordpress;

public sealed class WordpressClient(
    HttpClient httpClient,
    ILogger<WordpressClient> logger)
    : IWordpressClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<WordpressConnectionTestResult> TestConnectionAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthorizedRequest(connection, HttpMethod.Get, "wp-json/wp/v2/users/me?context=edit");
        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            var checkedAt = DateTimeOffset.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("WordPress connection test succeeded. baseUrl={BaseUrl}", connection.BaseUrl);
                return new WordpressConnectionTestResult(true, "WordPressに接続できました。", checkedAt);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new WordpressConnectionTestResult(false, "WordPressの認証に失敗しました。", checkedAt);
            }

            return new WordpressConnectionTestResult(false, "WordPress REST APIへ接続できませんでした。", checkedAt);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "WordPress接続テストがタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "WordPressへの接続に失敗しました。",
                ex);
        }
    }

    public async Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
        WordpressSiteConnection connection,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthorizedRequest(
            connection,
            HttpMethod.Get,
            "wp-json/wp/v2/categories?per_page=100&hide_empty=false&orderby=name&order=asc");

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateException(response, "WordPressカテゴリ取得に失敗しました。");
            }

            var payload = await response.Content.ReadFromJsonAsync<WordpressCategoryResponse[]>(
                JsonOptions,
                cancellationToken);

            return (payload ?? [])
                .Where(category => category.Id > 0)
                .Select(category => new WordpressCategoryDto(
                    category.Id,
                    WebUtility.HtmlDecode(category.Name ?? string.Empty).Trim(),
                    category.Slug?.Trim() ?? string.Empty))
                .ToArray();
        }
        catch (ExternalIntegrationException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "WordPressカテゴリ取得がタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "WordPressカテゴリ取得に失敗しました。",
                ex);
        }
        catch (JsonException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalBadResponse,
                "WordPressカテゴリのレスポンス形式が不正です。",
                ex);
        }
    }

    public async Task<WordpressPostResult> CreatePostAsync(
        WordpressPostRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthorizedRequest(
            request.Connection,
            HttpMethod.Post,
            "wp-json/wp/v2/posts");
        message.Content = JsonContent.Create(CreatePostBody(request), options: JsonOptions);

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = MapErrorCode(response.StatusCode);
                return new WordpressPostResult(
                    false,
                    null,
                    null,
                    errorCode,
                    "WordPress投稿に失敗しました。");
            }

            var payload = await response.Content.ReadFromJsonAsync<WordpressPostResponse>(
                JsonOptions,
                cancellationToken);

            if (payload?.Id is null or <= 0)
            {
                throw new ExternalIntegrationException(
                    ExternalIntegrationErrorCodes.ExternalBadResponse,
                    "WordPress投稿のレスポンス形式が不正です。");
            }

            logger.LogInformation(
                "WordPress post succeeded. baseUrl={BaseUrl} postId={PostId}",
                request.Connection.BaseUrl,
                payload.Id);

            return new WordpressPostResult(
                true,
                payload.Id,
                payload.Link,
                null,
                null);
        }
        catch (ExternalIntegrationException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.Timeout,
                "WordPress投稿がタイムアウトしました。",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.NetworkError,
                "WordPress投稿に失敗しました。",
                ex);
        }
        catch (JsonException ex)
        {
            throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalBadResponse,
                "WordPress投稿のレスポンス形式が不正です。",
                ex);
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        WordpressSiteConnection connection,
        HttpMethod method,
        string relativePath)
    {
        var message = new HttpRequestMessage(method, BuildUri(connection.BaseUrl, relativePath));
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{connection.LoginId}:{connection.ApplicationPassword}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        var normalizedBase = baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), relativePath);
    }

    private static WordpressCreatePostRequest CreatePostBody(WordpressPostRequest request)
    {
        return new WordpressCreatePostRequest(
            request.Title,
            request.HtmlBody,
            ToApiStatus(request.Status),
            request.CategoryId.HasValue ? [request.CategoryId.Value] : null);
    }

    private static string ToApiStatus(string status)
    {
        return string.Equals(status, WordpressPostStatuses.Publish, StringComparison.OrdinalIgnoreCase)
            ? "publish"
            : "draft";
    }

    private static ExternalIntegrationException CreateException(
        HttpResponseMessage response,
        string message)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
                message),
            HttpStatusCode.Forbidden => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ForbiddenExternalApi,
                message),
            HttpStatusCode.TooManyRequests => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.RateLimited,
                message,
                retryAfter: retryAfter),
            >= HttpStatusCode.InternalServerError => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ExternalServerError,
                message),
            _ => new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                message)
        };
    }

    private static string MapErrorCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => ExternalIntegrationErrorCodes.ValidationError,
            HttpStatusCode.Unauthorized => ExternalIntegrationErrorCodes.UnauthorizedExternalApi,
            HttpStatusCode.Forbidden => ExternalIntegrationErrorCodes.ForbiddenExternalApi,
            HttpStatusCode.TooManyRequests => ExternalIntegrationErrorCodes.RateLimited,
            >= HttpStatusCode.InternalServerError => ExternalIntegrationErrorCodes.ExternalServerError,
            _ => ExternalIntegrationErrorCodes.UnknownExternalError
        };
    }

    private sealed record WordpressCategoryResponse(int Id, string? Name, string? Slug);

    private sealed record WordpressCreatePostRequest(
        string Title,
        string Content,
        string Status,
        IReadOnlyList<int>? Categories);

    private sealed record WordpressPostResponse(int? Id, string? Link);
}
