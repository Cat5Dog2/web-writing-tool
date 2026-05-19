using WebWritingTool.Web;

namespace WebWritingTool.IntegrationTests.Architecture;

public class WebCompositionTests
{
    [Fact]
    public void WebProjectReferencesApplicationAndInfrastructure()
    {
        var referencedAssemblies = typeof(WebAssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.Equal("WebWritingTool.Web", WebAssemblyReference.AssemblyName);
        Assert.Contains("WebWritingTool.Application", referencedAssemblies);
        Assert.Contains("WebWritingTool.Infrastructure", referencedAssemblies);
    }
}
