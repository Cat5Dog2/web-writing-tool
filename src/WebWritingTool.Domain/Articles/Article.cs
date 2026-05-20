using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Articles;

public sealed class Article : IAuditableEntity, ISoftDeletableEntity, IRowVersionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public string Keyword { get; set; } = string.Empty;

    public string? Title { get; set; }

    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;

    public string? Tone { get; set; }

    public string[] Tags { get; set; } = [];

    public string? Memo { get; set; }

    public string? SuggestedKeywords { get; set; }

    public string? RelatedKeywords { get; set; }

    public string? LearningType { get; set; }

    public string? LearningText { get; set; }

    public string? AdditionalPrompt { get; set; }

    public string? Body { get; set; }

    public string? HtmlBody { get; set; }

    public string? MetaDescription { get; set; }

    public string? GenerationModel { get; set; }

    public string OutlineMethod { get; set; } = string.Empty;

    public bool SearchMode { get; set; }

    public bool IsDomesticOnly { get; set; } = true;

    public string NotificationMode { get; set; } = "None";

    public bool StrictMode { get; set; }

    public string? TopicRisk { get; set; }

    public bool HumanReviewRequired { get; set; }

    public DateTimeOffset? HumanReviewedAt { get; set; }

    public string? HumanReviewedByUserId { get; set; }

    public Guid? WritingProfileWordpressSiteId { get; set; }

    public string? WritingProfileSnapshotJson { get; set; }

    public bool AutoPostToWordpress { get; set; }

    public Guid? AutoPostWordpressSiteId { get; set; }

    public int? AutoPostWordpressCategoryId { get; set; }

    public DateTimeOffset? AutoPostQueuedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
