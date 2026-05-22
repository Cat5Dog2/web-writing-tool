using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebWritingTool.Application.Generation;

public sealed record PromptDocument(
    string SystemInstruction,
    string UserPrompt,
    string PromptHash,
    int PromptChars);

public sealed record ArticlePromptContext(
    Guid ArticleId,
    string Keyword,
    string? Title,
    string? Tone,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? LearningType,
    string? LearningText,
    string? AdditionalPrompt,
    string? OutlineMethod,
    bool SearchMode,
    bool IsDomesticOnly,
    bool StrictMode,
    string? TopicRisk,
    string? WritingProfileSnapshotJson,
    IReadOnlyList<HeadingPromptContext> Headings);

public sealed record HeadingPromptContext(
    Guid Id,
    Guid? ParentId,
    int Level,
    string Title,
    string? Body,
    int DisplayOrder,
    int? TargetLength);

public sealed record WritingProfilePromptContext(
    string? SiteName,
    string? SiteAdminProfile,
    string? WritingCharacter,
    string? ReaderPersona)
{
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(SiteName)
        || !string.IsNullOrWhiteSpace(SiteAdminProfile)
        || !string.IsNullOrWhiteSpace(WritingCharacter)
        || !string.IsNullOrWhiteSpace(ReaderPersona);

    public static WritingProfilePromptContext Empty { get; } = new(null, null, null, null);

    public static WritingProfilePromptContext FromSnapshotJson(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            return new WritingProfilePromptContext(
                ReadString(root, "siteName"),
                ReadString(root, "siteAdminProfile"),
                ReadString(root, "writingCharacter"),
                ReadString(root, "readerPersona"));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? Normalize(property.GetString())
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public static class PromptHashCalculator
{
    public static string Compute(string systemInstruction, string userPrompt)
    {
        var normalized = string.Join(
            "\n---\n",
            Normalize(systemInstruction),
            Normalize(userPrompt));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string value)
    {
        return value.ReplaceLineEndings("\n").Trim();
    }
}
