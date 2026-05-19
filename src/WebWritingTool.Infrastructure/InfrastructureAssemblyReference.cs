using WebWritingTool.Application;
using WebWritingTool.Domain;

namespace WebWritingTool.Infrastructure;

public static class InfrastructureAssemblyReference
{
    public static string AssemblyName => typeof(InfrastructureAssemblyReference).Assembly.GetName().Name!;

    public static string ApplicationAssemblyName => ApplicationAssemblyReference.AssemblyName;

    public static string DomainAssemblyName => DomainAssemblyReference.AssemblyName;
}
