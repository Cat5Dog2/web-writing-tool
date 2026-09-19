using System.Globalization;
using System.Text.Json;

namespace WebWritingTool.Infrastructure.Generation;

internal sealed record GeminiRateLimitResponse(TimeSpan? RetryAfter, bool DailyLimit)
{
    public static async Task<GeminiRateLimitResponse> ReadAsync(HttpResponseMessage response,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var header = response.Headers.RetryAfter;
        var retryAfter = header?.Delta ?? (header?.Date - now);
        if (retryAfter <= TimeSpan.Zero) retryAfter = null;
        var daily = false;
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                return new(retryAfter, daily);

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object || !detail.TryGetProperty("@type", out var type)) continue;
                if (type.ValueKind != JsonValueKind.String) continue;
                if (type.GetString() == "type.googleapis.com/google.rpc.RetryInfo"
                    && detail.TryGetProperty("retryDelay", out var duration) && duration.ValueKind == JsonValueKind.String)
                {
                    var text = duration.GetString()!;
                    if (text.EndsWith('s') && double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var seconds) && seconds is > 0 and <= 31_536_000)
                    {
                        var bodyDelay = TimeSpan.FromSeconds(seconds);
                        if (!retryAfter.HasValue || bodyDelay > retryAfter.Value) retryAfter = bodyDelay;
                    }
                }
                if (type.GetString() == "type.googleapis.com/google.rpc.QuotaFailure"
                    && detail.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                {
                    daily |= violations.EnumerateArray().Any(violation => violation.ValueKind == JsonValueKind.Object
                        && violation.TryGetProperty("quotaId", out var id) && id.ValueKind == JsonValueKind.String
                        && id.GetString()!.Contains("PerDay", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch (JsonException)
        {
            // HTMLや空の429でもHTTPの待機指示を維持する。レスポンス本文はログに出さない。
            return new(retryAfter, daily);
        }

        return new(retryAfter, daily);
    }
}
