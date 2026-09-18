namespace WebWritingTool.Web.Configuration;

public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApis";

    public bool UseMocks { get; set; }
}
