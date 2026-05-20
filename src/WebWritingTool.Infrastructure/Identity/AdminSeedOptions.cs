namespace WebWritingTool.Infrastructure.Identity;

public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? DisplayName { get; set; }
}
