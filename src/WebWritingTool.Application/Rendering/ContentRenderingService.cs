using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace WebWritingTool.Application.Rendering;

public sealed partial class ContentRenderingService : IContentRenderingService
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "h2",
        "h3",
        "p",
        "br",
        "ul",
        "ol",
        "li",
        "strong",
        "em",
        "blockquote",
        "a",
        "code",
        "pre",
        "cite",
        "time"
    };

    public string BuildArticleMarkdown(IReadOnlyList<ArticleHeadingContent> headings)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            headings
                .OrderBy(heading => heading.DisplayOrder)
                .Select(heading =>
                {
                    var prefix = heading.Level == 3 ? "###" : "##";
                    var body = string.IsNullOrWhiteSpace(heading.Body)
                        ? string.Empty
                        : Environment.NewLine + Environment.NewLine + heading.Body.Trim();
                    return $"{prefix} {heading.Title.Trim()}{body}";
                }));
    }

    public string ConvertMarkdownToHtml(string markdown, bool insertLineBreakAfterPeriod)
    {
        var html = ConvertTrustedMarkdown(markdown, insertLineBreakAfterPeriod);
        return SanitizeHtml(html);
    }

    public string SanitizeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sanitized = CommentPattern().Replace(html, string.Empty);
        sanitized = ForbiddenBlockPattern().Replace(sanitized, string.Empty);
        sanitized = TagPattern().Replace(sanitized, SanitizeTag);
        sanitized = ControlCharacterPattern().Replace(sanitized, string.Empty);

        return sanitized.Trim();
    }

    private static string ConvertTrustedMarkdown(string markdown, bool insertLineBreakAfterPeriod)
    {
        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var html = new StringBuilder();
        var paragraph = new List<string>();
        string? openListTag = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                CloseList(html, ref openListTag);
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                CloseList(html, ref openListTag);
                html.Append("<h3>")
                    .Append(RenderInline(trimmed[4..].Trim(), insertLineBreakAfterPeriod: false))
                    .AppendLine("</h3>");
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                CloseList(html, ref openListTag);
                html.Append("<h2>")
                    .Append(RenderInline(trimmed[3..].Trim(), insertLineBreakAfterPeriod: false))
                    .AppendLine("</h2>");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                OpenList(html, ref openListTag, "ul");
                html.Append("<li>")
                    .Append(RenderInline(trimmed[2..].Trim(), insertLineBreakAfterPeriod))
                    .AppendLine("</li>");
                continue;
            }

            if (TryReadOrderedListItem(trimmed, out var orderedText))
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                OpenList(html, ref openListTag, "ol");
                html.Append("<li>")
                    .Append(RenderInline(orderedText, insertLineBreakAfterPeriod))
                    .AppendLine("</li>");
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
                CloseList(html, ref openListTag);
                html.Append("<blockquote><p>")
                    .Append(RenderInline(trimmed[2..].Trim(), insertLineBreakAfterPeriod))
                    .AppendLine("</p></blockquote>");
                continue;
            }

            CloseList(html, ref openListTag);
            paragraph.Add(trimmed);
        }

        FlushParagraph(html, paragraph, insertLineBreakAfterPeriod);
        CloseList(html, ref openListTag);

        return html.ToString();
    }

    private static void FlushParagraph(
        StringBuilder html,
        List<string> paragraph,
        bool insertLineBreakAfterPeriod)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, paragraph);
        html.Append("<p>")
            .Append(RenderInline(text, insertLineBreakAfterPeriod))
            .AppendLine("</p>");
        paragraph.Clear();
    }

    private static void OpenList(StringBuilder html, ref string? openListTag, string listTag)
    {
        if (string.Equals(openListTag, listTag, StringComparison.Ordinal))
        {
            return;
        }

        CloseList(html, ref openListTag);
        html.Append('<').Append(listTag).AppendLine(">");
        openListTag = listTag;
    }

    private static void CloseList(StringBuilder html, ref string? openListTag)
    {
        if (openListTag is null)
        {
            return;
        }

        html.Append("</").Append(openListTag).AppendLine(">");
        openListTag = null;
    }

    private static string RenderInline(string text, bool insertLineBreakAfterPeriod)
    {
        var normalized = insertLineBreakAfterPeriod
            ? InsertLineBreaksAfterJapanesePeriod(text)
            : text;
        var encoded = HtmlEncoder.Default.Encode(normalized);

        encoded = LinkPattern().Replace(encoded, match =>
        {
            var label = match.Groups["label"].Value;
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            return IsAllowedHref(href)
                ? BuildAnchor(label, href)
                : label;
        });

        encoded = StrongPattern().Replace(encoded, "<strong>${text}</strong>");
        encoded = EmphasisPattern().Replace(encoded, "<em>${text}</em>");
        encoded = encoded
            .Replace("&#xA;", "<br>", StringComparison.OrdinalIgnoreCase)
            .Replace("&#xD;", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return encoded;
    }

    private static string InsertLineBreaksAfterJapanesePeriod(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            builder.Append(current);
            if (current != '。')
            {
                continue;
            }

            var isLast = index == value.Length - 1;
            if (!isLast && value[index + 1] != '\n')
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static bool TryReadOrderedListItem(string line, out string text)
    {
        var index = 0;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            index++;
        }

        if (index == 0
            || index + 1 >= line.Length
            || line[index] != '.'
            || line[index + 1] != ' ')
        {
            text = string.Empty;
            return false;
        }

        text = line[(index + 2)..].Trim();
        return true;
    }

    private static string SanitizeTag(Match match)
    {
        var isEndTag = match.Groups["end"].Success;
        var tag = match.Groups["tag"].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(tag))
        {
            return string.Empty;
        }

        if (isEndTag)
        {
            return tag == "br" ? string.Empty : $"</{tag}>";
        }

        if (tag == "br")
        {
            return "<br>";
        }

        var attributes = match.Groups["attributes"].Value;
        return tag switch
        {
            "a" => BuildSanitizedAnchorTag(attributes),
            "blockquote" => BuildSanitizedBlockquoteTag(attributes),
            "time" => BuildSanitizedTimeTag(attributes),
            _ => $"<{tag}>"
        };
    }

    private static string BuildSanitizedAnchorTag(string attributes)
    {
        var href = ReadAttribute(attributes, "href");
        if (!IsAllowedHref(href))
        {
            return "<a>";
        }

        return BuildAnchorStartTag(href!);
    }

    private static string BuildSanitizedBlockquoteTag(string attributes)
    {
        var cite = ReadAttribute(attributes, "cite");
        return IsAllowedHref(cite)
            ? $"<blockquote cite=\"{HtmlEncoder.Default.Encode(cite!)}\">"
            : "<blockquote>";
    }

    private static string BuildSanitizedTimeTag(string attributes)
    {
        var datetime = ReadAttribute(attributes, "datetime");
        return DateTimeOffset.TryParse(datetime, out _)
            ? $"<time datetime=\"{HtmlEncoder.Default.Encode(datetime!)}\">"
            : "<time>";
    }

    private static string BuildAnchor(string label, string href)
    {
        return $"{BuildAnchorStartTag(href)}{label}</a>";
    }

    private static string BuildAnchorStartTag(string href)
    {
        var encodedHref = HtmlEncoder.Default.Encode(href);
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return $"<a href=\"{encodedHref}\" target=\"_blank\" rel=\"noopener noreferrer\">";
        }

        return $"<a href=\"{encodedHref}\">";
    }

    private static string? ReadAttribute(string attributes, string name)
    {
        var match = AttributePattern().Matches(attributes)
            .FirstOrDefault(item => string.Equals(item.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase));

        return match?.Groups["value"].Value;
    }

    private static bool IsAllowedHref(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(char.IsControl)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('/'))
        {
            return true;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(
        @"<\s*(script|style|iframe|object|embed|form|input|button|textarea|select|img|video|audio|svg|math|link|meta)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ForbiddenBlockPattern();

    [GeneratedRegex(@"<\s*(?<end>/)?\s*(?<tag>[a-zA-Z0-9]+)\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex ControlCharacterPattern();

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\((?<href>[^)]+)\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\*\*(?<text>[^*]+)\*\*")]
    private static partial Regex StrongPattern();

    [GeneratedRegex(@"(?<!\*)\*(?<text>[^*]+)\*(?!\*)")]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AttributePattern();
}
