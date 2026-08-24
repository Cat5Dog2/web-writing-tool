namespace WebWritingTool.Web.Configuration;

// docs/security-design.md 18.3 の段階導入に対応する。
// MVPはReportOnlyで運用し、違反が出なくなってからEnforceへ切り替える。
internal enum ContentSecurityPolicyMode
{
    ReportOnly,
    Enforce,
    Disabled
}

internal sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public const string DataProtectionApplicationName = "WebWritingTool";

    public string? DataProtectionKeysPath { get; init; }

    public bool RequireHttps { get; init; } = true;

    public bool? ForwardedHeadersEnabled { get; init; }

    public string[] AllowedForwardedHosts { get; init; } = [];

    public ContentSecurityPolicyMode ContentSecurityPolicyMode { get; init; } = ContentSecurityPolicyMode.ReportOnly;

    public bool ShouldUseForwardedHeaders(IWebHostEnvironment environment)
    {
        return ForwardedHeadersEnabled ?? !environment.IsDevelopment();
    }
}
