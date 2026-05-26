using WebWritingTool.Infrastructure.Security;

namespace WebWritingTool.UnitTests.Security;

public class SecretMaskerTests
{
    [Fact]
    public void Mask_WithSecretPatterns_RedactsSensitiveValues()
    {
        var masker = new SecretMasker();
        const string input = """
            Authorization: Bearer x-api-token
            Cookie: WebWritingTool.Auth=auth-cookie
            api_key=gemini-key
            application_password='wp-app-pass'
            webhook_url=https://discord.com/api/webhooks/1234567890/discord-secret-token
            """;

        var masked = masker.Mask(input);

        Assert.DoesNotContain("x-api-token", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-cookie", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini-key", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("wp-app-pass", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("discord-secret-token", masked, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_WithSecretQueryString_RedactsOnlySecretValue()
    {
        var masker = new SecretMasker();

        var masked = masker.Mask("https://example.com/callback?token=secret-token&status=failed");

        Assert.Contains("status=failed", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", masked, StringComparison.Ordinal);
        Assert.Contains("token=[REDACTED]", masked, StringComparison.Ordinal);
    }
}
