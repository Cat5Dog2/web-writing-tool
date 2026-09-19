namespace WebWritingTool.Application.Generation;

public static class AiProviders
{
    public const string Gemini = "GoogleGemini";
}

public static class AiOperations
{
    public const string TitleGeneration = "TitleGeneration";
    public const string OutlineGeneration = "OutlineGeneration";
    public const string BodyGeneration = "BodyGeneration";
    public const string Rewrite = "Rewrite";
    public const string Summarize = "Summarize";
    public const string Expand = "Expand";
    public const string Refresh = "Refresh";
}

public static class ExternalIntegrationErrorCodes
{
    public const string ValidationError = "ValidationError";
    public const string UnauthorizedExternalApi = "UnauthorizedExternalApi";
    public const string ForbiddenExternalApi = "ForbiddenExternalApi";
    public const string RateLimited = "RateLimited";
    public const string Timeout = "Timeout";
    public const string ExternalServerError = "ExternalServerError";
    public const string ExternalBadResponse = "ExternalBadResponse";
    public const string NetworkError = "NetworkError";
    public const string UsageLimitExceeded = "UsageLimitExceeded";
    public const string UnknownExternalError = "UnknownExternalError";
}

public interface IAiTextGenerationClient
{
    Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AiTextGenerationRequest(
    string Provider,
    string Model,
    string Operation,
    string SystemInstruction,
    string UserPrompt,
    int? MaxOutputChars,
    double? Temperature,
    IReadOnlyList<AiReferenceSource> References)
{
    public int PromptChars => ReferencePromptFormatter.SystemInstruction(SystemInstruction, References).Length
        + ReferencePromptFormatter.UserPrompt(UserPrompt, References).Length;
}

public sealed record AiReferenceSource(
    string SourceId,
    string? Title,
    string? Url,
    string? Summary);

public sealed record AiTextGenerationResult(
    string Text,
    string Provider,
    string Model,
    int PromptChars,
    int OutputChars,
    string? RawResponseId)
{
    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }
}

public sealed class ExternalIntegrationException : Exception
{
    public ExternalIntegrationException(
        string errorCode,
        string userMessage,
        Exception? innerException = null,
        TimeSpan? retryAfter = null)
        : base(userMessage, innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        RetryAfter = retryAfter;
    }

    public string ErrorCode { get; }

    public string UserMessage { get; }

    public TimeSpan? RetryAfter { get; }
}
