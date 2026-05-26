namespace WebWritingTool.Web.Configuration;

internal sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public const string DataProtectionApplicationName = "WebWritingTool";

    public string? DataProtectionKeysPath { get; init; }

    public bool RequireHttps { get; init; } = true;

    public bool? ForwardedHeadersEnabled { get; init; }

    public string[] AllowedForwardedHosts { get; init; } = [];

    public bool ShouldUseForwardedHeaders(IWebHostEnvironment environment)
    {
        return ForwardedHeadersEnabled ?? !environment.IsDevelopment();
    }
}
