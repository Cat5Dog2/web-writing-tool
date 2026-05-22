namespace WebWritingTool.Infrastructure.Generation;

public sealed class GeminiOptions
{
    public const string SectionName = "AiProviders:Gemini";
    public const string Provider = "GoogleGemini";
    public const string DefaultModel = "gemini-3.1-pro-preview";
    public const string DefaultRegion = "Japan";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = DefaultModel;

    public string Region { get; init; } = DefaultRegion;

    public int TimeoutSeconds { get; init; } = 120;

    public int? MaxInputChars { get; init; }

    public Uri EndpointBaseAddress { get; init; } = new("https://generativelanguage.googleapis.com/");
}
