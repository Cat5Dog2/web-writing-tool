namespace WebWritingTool.Application.Generation;

public sealed record TitleGenerationPayload(
    Guid ArticleId,
    string? Keyword,
    string? GenerationModel,
    int? CandidateCount,
    string? TitleMethod,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? AdditionalPrompt);

public sealed record OutlineGenerationPayload(
    Guid ArticleId,
    string? Keyword,
    string? Title,
    int? H2Count,
    int? H3Count,
    string? OutlineMethod,
    string? GenerationModel,
    bool? SearchMode,
    bool? IsDomesticOnly,
    string? Tone,
    string? SuggestedKeywords,
    string? RelatedKeywords,
    string? LearningType,
    string? LearningText,
    string? AdditionalPrompt);

public sealed record BodyGenerationPayload(
    Guid ArticleId,
    Guid? HeadingId,
    string? Scope,
    string? GenerationModel,
    int? TargetLength,
    bool UseWebSearch,
    string? AdditionalPrompt);

public sealed record RewritePayload(
    Guid ArticleId,
    Guid HeadingId,
    string Operation,
    string? GenerationModel,
    string? AdditionalPrompt);
