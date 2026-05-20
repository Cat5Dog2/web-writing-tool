using Microsoft.AspNetCore.Identity;

namespace WebWritingTool.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
