using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Wordpress;

public sealed class WordpressSite : IAuditableEntity, ISoftDeletableEntity, IRowVersionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public string SiteName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string LoginId { get; set; } = string.Empty;

    public string EncryptedApplicationPassword { get; set; } = string.Empty;

    public int? DefaultCategoryId { get; set; }

    public string? DefaultCategoryName { get; set; }

    public string? SiteAdminProfile { get; set; }

    public string? WritingCharacter { get; set; }

    public string? ReaderPersona { get; set; }

    public DateTimeOffset? LastConnectedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
