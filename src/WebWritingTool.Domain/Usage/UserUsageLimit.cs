using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Usage;

public sealed class UserUsageLimit : IAuditableEntity, IRowVersionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public int MonthlyLimitChars { get; set; }

    public int RemainingOutlineCount { get; set; }

    public bool DefaultStrictMode { get; set; }

    public string? MaxSearchCachePolicy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
