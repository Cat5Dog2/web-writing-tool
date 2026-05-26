using WebWritingTool.Infrastructure.Security;

namespace WebWritingTool.UnitTests.Security;

public class DnsUrlSafetyValidatorTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://192.168.1.10")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://example.com:8443")]
    public async Task ValidateHttpsPublicUrlAsync_WithUnsafeUrl_ReturnsFailure(string url)
    {
        var validator = new DnsUrlSafetyValidator();

        var result = await validator.ValidateHttpsPublicUrlAsync(url);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }
}
