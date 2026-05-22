using System.Text.Json;

namespace WebWritingTool.Application.Generation;

public sealed record TitleCandidate(string Title, string? Reason);

public sealed record OutlineHeadingItem(
    int Level,
    string Title,
    int? TargetLength,
    IReadOnlyList<OutlineHeadingItem> Children);

public static class TitleCandidateParser
{
    public static IReadOnlyList<TitleCandidate> Parse(string text, int maxCount)
    {
        using var document = JsonDocument.Parse(StripCodeFence(text));
        var root = document.RootElement;
        var source = root.TryGetProperty("candidates", out var candidates)
            ? candidates
            : root.TryGetProperty("titles", out var titles)
                ? titles
                : root;

        if (source.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Title candidate response must be an array.");
        }

        return source.EnumerateArray()
            .Select(ReadCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Title))
            .DistinctBy(candidate => candidate.Title, StringComparer.Ordinal)
            .Take(Math.Clamp(maxCount, 1, 20))
            .ToArray();
    }

    private static TitleCandidate ReadCandidate(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? new TitleCandidate(element.GetString()?.Trim() ?? string.Empty, null)
            : new TitleCandidate(
                ReadString(element, "title") ?? string.Empty,
                ReadString(element, "reason"));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
    }

    internal static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
        return lines.Length >= 2
            ? string.Join('\n', lines.Skip(1).SkipLast(1)).Trim()
            : trimmed;
    }
}

public static class OutlineGenerationParser
{
    public static IReadOnlyList<OutlineHeadingItem> Parse(string text)
    {
        using var document = JsonDocument.Parse(TitleCandidateParser.StripCodeFence(text));
        var root = document.RootElement;
        var source = root.TryGetProperty("headings", out var headings) ? headings : root;

        if (source.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Outline response must contain headings array.");
        }

        return source.EnumerateArray()
            .Select(ReadHeading)
            .Where(heading => !string.IsNullOrWhiteSpace(heading.Title))
            .ToArray();
    }

    private static OutlineHeadingItem ReadHeading(JsonElement element)
    {
        var level = ReadInt(element, "level") ?? 2;
        var title = ReadString(element, "title") ?? string.Empty;
        var targetLength = ReadInt(element, "targetLength");
        var children = element.TryGetProperty("children", out var childrenElement)
            && childrenElement.ValueKind == JsonValueKind.Array
            ? childrenElement.EnumerateArray()
                .Select(ReadHeading)
                .Where(heading => !string.IsNullOrWhiteSpace(heading.Title))
                .ToArray()
            : [];

        return new OutlineHeadingItem(level, title, targetLength, children);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }
}
