using WebWritingTool.Application;
using WebWritingTool.Infrastructure;

namespace WebWritingTool.Web;

public static class WebAssemblyReference
{
    public static string AssemblyName => typeof(WebAssemblyReference).Assembly.GetName().Name!;

    public static string ApplicationAssemblyName => ApplicationAssemblyReference.AssemblyName;

    public static string InfrastructureAssemblyName => InfrastructureAssemblyReference.AssemblyName;
}
