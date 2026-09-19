namespace WebWritingTool.Infrastructure.Generation;

public sealed class GeminiRateLimitOptions
{
    public string Scope { get; init; } = "default";

    public double SafetyRatio { get; init; } = 0.8;

    public GeminiModelRateLimits Default { get; init; } = new();

    public Dictionary<string, GeminiModelRateLimits> Models { get; init; } = new(StringComparer.Ordinal);

    public bool IsValid() => !string.IsNullOrWhiteSpace(Scope) && Scope.Length <= 100
        && double.IsFinite(SafetyRatio) && SafetyRatio is > 0 and <= 1
        && Default.IsValid()
        && Models.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 100 && pair.Value.IsValid());

    public GeminiModelRateLimits ForModel(string model) => Models.GetValueOrDefault(model, Default);
}

public sealed class GeminiModelRateLimits
{
    public int RequestsPerMinute { get; init; } = 5;

    public int InputTokensPerMinute { get; init; } = 100_000;

    public int? RequestsPerDay { get; init; }

    // 同じ割当枠を使用するモデルエイリアスには同じグループ名を設定する。
    public string? QuotaGroup { get; init; }

    public bool IsValid() => RequestsPerMinute > 0 && InputTokensPerMinute > 0
        && (RequestsPerDay is null or > 0)
        && (QuotaGroup is null || !string.IsNullOrWhiteSpace(QuotaGroup) && QuotaGroup.Length <= 100);
}
