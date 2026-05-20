using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Articles;

public sealed class ArticleHeading : IAuditableEntity, ISoftDeletableEntity, IRowVersionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ArticleId { get; set; }

    public Guid? ParentId { get; set; }

    public int Level { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public int DisplayOrder { get; set; }

    public int? TargetLength { get; set; }

    public int? ActualLength { get; set; }

    public HeadingStatus Status { get; set; } = HeadingStatus.Pending;

    public bool UseWebSearch { get; set; }

    public string? SearchQuery { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
