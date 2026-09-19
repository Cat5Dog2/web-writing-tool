namespace WebWritingTool.Application.Generation;

public sealed class ExternalApiDeferredException(DateTimeOffset nextRunAt)
    : Exception("Gemini APIの利用枠が空くまで待機しています。")
{
    public DateTimeOffset NextRunAt { get; } = nextRunAt;
}
