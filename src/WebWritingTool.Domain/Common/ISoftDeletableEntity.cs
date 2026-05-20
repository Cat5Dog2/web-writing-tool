namespace WebWritingTool.Domain.Common;

public interface ISoftDeletableEntity
{
    DateTimeOffset? DeletedAt { get; set; }
}
