using WebWritingTool.Application;
using WebWritingTool.Domain;
using WebWritingTool.Infrastructure;

namespace WebWritingTool.UnitTests.Architecture;

public class LayerDependencyTests
{
    [Fact]
    public void LayerAssemblyReferencesExposeExpectedAssemblyNames()
    {
        Assert.Equal("WebWritingTool.Domain", DomainAssemblyReference.AssemblyName);
        Assert.Equal("WebWritingTool.Application", ApplicationAssemblyReference.AssemblyName);
        Assert.Equal("WebWritingTool.Infrastructure", InfrastructureAssemblyReference.AssemblyName);
    }

    [Fact]
    public void ApplicationReferencesDomainOnlyAmongLocalLayers()
    {
        var referencedAssemblies = typeof(ApplicationAssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.Contains("WebWritingTool.Domain", referencedAssemblies);
        Assert.DoesNotContain("WebWritingTool.Infrastructure", referencedAssemblies);
    }

    [Fact]
    public void InfrastructureReferencesApplicationAndDomain()
    {
        var referencedAssemblies = typeof(InfrastructureAssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.Contains("WebWritingTool.Application", referencedAssemblies);
        Assert.Contains("WebWritingTool.Domain", referencedAssemblies);
    }
}
