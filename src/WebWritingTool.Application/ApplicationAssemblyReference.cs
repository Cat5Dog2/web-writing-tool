using WebWritingTool.Domain;

namespace WebWritingTool.Application;

public static class ApplicationAssemblyReference
{
    public static string AssemblyName => typeof(ApplicationAssemblyReference).Assembly.GetName().Name!;

    public static string DomainAssemblyName => DomainAssemblyReference.AssemblyName;
}
