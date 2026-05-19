namespace WebWritingTool.Domain;

public static class DomainAssemblyReference
{
    public static string AssemblyName => typeof(DomainAssemblyReference).Assembly.GetName().Name!;
}
