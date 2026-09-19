namespace WebWritingTool.Domain.Ai;

public sealed class GeminiQuotaState
{
    public string Scope { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string StateJson { get; set; } = "{}";
}
