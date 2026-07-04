using System.Net;
using System.Text.RegularExpressions;

namespace WebWritingTool.Application.Rendering;

public static partial class XPostCitationParser
{
    public static IReadOnlyList<string> ExtractPostIds(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var postIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in BlockquoteCiteRegex().Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (TryExtractPostId(url, out var postId) && seen.Add(postId))
            {
                postIds.Add(postId);
            }
        }

        return postIds;
    }

    private static bool TryExtractPostId(string url, out string postId)
    {
        postId = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !IsXHost(uri.Host))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "status", StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].All(char.IsAsciiDigit))
            {
                postId = segments[index + 1];
                return postId.Length > 0;
            }
        }

        return false;
    }

    private static bool IsXHost(string host)
    {
        return string.Equals(host, "x.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.x.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "twitter.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.twitter.com", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        """<blockquote\b[^>]*\bcite\s*=\s*"(?<url>[^"]*)"[^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockquoteCiteRegex();
}
