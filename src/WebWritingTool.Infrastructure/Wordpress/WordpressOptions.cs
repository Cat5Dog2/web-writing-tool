namespace WebWritingTool.Infrastructure.Wordpress;

public sealed class WordpressOptions
{
    public const string SectionName = "Wordpress";

    public int TimeoutSeconds { get; init; } = 60;

    public int RetryCount { get; init; } = 0;

    public string[] AllowedSchemes { get; init; } = ["https"];
}
