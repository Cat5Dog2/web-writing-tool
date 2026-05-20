using WebWritingTool.Domain.Common;

namespace WebWritingTool.Domain.Ai;

public sealed class AiModelSetting : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Region { get; set; } = "Japan";

    public bool Enabled { get; set; } = true;

    public int? MaxInputChars { get; set; }

    public int? MaxOutputChars { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
