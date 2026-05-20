namespace WebWritingTool.Application.Articles;

public static class ArticleInputNormalizer
{
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        return tags
            .Select(tag => tag?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    public static IReadOnlyList<string> SplitTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        return NormalizeTags(tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static BulkArticleLineParseResult ParseBulkLine(string? line, int lineNumber)
    {
        var originalLine = line ?? string.Empty;
        var normalizedLine = originalLine.Trim();

        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            return Reject(lineNumber, originalLine, "行が空です。");
        }

        var parts = normalizedLine.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
        {
            return Reject(lineNumber, originalLine, "入力形式は「キーワード」または「キーワード|タイトル」です。");
        }

        var keyword = parts[0];
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Reject(lineNumber, originalLine, "キーワードが未入力です。");
        }

        var title = parts.Length == 2 ? NormalizeOptionalText(parts[1]) : null;
        return new BulkArticleLineParseResult(new BulkArticleLine(lineNumber, keyword, title), null);
    }

    private static BulkArticleLineParseResult Reject(int lineNumber, string line, string reason)
    {
        return new BulkArticleLineParseResult(null, new BulkArticleRejectedLine(lineNumber, line, reason));
    }
}
