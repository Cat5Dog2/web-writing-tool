using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebWritingTool.Application.Search;

public static class SearchQueryNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static NormalizedSearchQuery NormalizeWeb(WebSearchRequest request)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = SearchProviders.Tavily,
            ["query"] = NormalizeText(request.Query),
            ["region"] = NormalizeText(request.Region),
            ["language"] = NormalizeText(request.Language),
            ["maxResults"] = Math.Max(1, request.MaxResults).ToString("D"),
            ["domesticOnly"] = request.DomesticOnly ? "true" : "false",
            ["topic"] = NormalizeText(request.Topic),
            ["searchDepth"] = NormalizeText(string.IsNullOrWhiteSpace(request.SearchDepth) ? "basic" : request.SearchDepth),
            ["startDate"] = request.StartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["endDate"] = request.EndDate?.ToString("yyyy-MM-dd") ?? string.Empty
        };

        return new NormalizedSearchQuery(values, ComputeHash(values));
    }

    public static NormalizedSearchQuery NormalizeX(XFullArchiveSearchRequest request)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = SearchProviders.X,
            ["query"] = NormalizeText(request.Query),
            ["language"] = NormalizeText(request.Language ?? "ja"),
            ["startTime"] = request.StartTime?.UtcDateTime.ToString("O") ?? string.Empty,
            ["endTime"] = request.EndTime?.UtcDateTime.ToString("O") ?? string.Empty,
            ["maxResults"] = Math.Max(1, request.MaxResults).ToString("D"),
            ["largeResearchMode"] = request.LargeResearchMode ? "true" : "false",
            ["excludeRetweets"] = request.ExcludeRetweets ? "true" : "false",
            ["excludeReplies"] = request.ExcludeReplies ? "true" : "false"
        };

        return new NormalizedSearchQuery(values, ComputeHash(values));
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var previousWasWhiteSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            previousWasWhiteSpace = false;
        }

        return builder.ToString();
    }

    private static string ComputeHash(SortedDictionary<string, string> values)
    {
        var json = JsonSerializer.Serialize(values, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record NormalizedSearchQuery(
    IReadOnlyDictionary<string, string> Values,
    string QueryHash);
