using Microsoft.AspNetCore.DataProtection;
using WebWritingTool.Web.Security;

namespace WebWritingTool.IntegrationTests.Security;

public class DataProtectionSecretProtectorTests
{
    [Fact]
    public void Protect_WithPlaintextSecret_ReturnsProtectedValueThatCanBeRestored()
    {
        var keyDirectory = Directory.CreateTempSubdirectory("web-writing-tool-dp-keys-");
        try
        {
            var provider = DataProtectionProvider.Create(keyDirectory);
            var protector = new DataProtectionSecretProtector(provider);
            const string plaintext = "wp-app-password-or-discord-webhook-secret";

            var protectedValue = protector.Protect(plaintext);
            var restored = protector.Unprotect(protectedValue);

            Assert.NotEqual(plaintext, protectedValue);
            Assert.DoesNotContain(plaintext, protectedValue, StringComparison.Ordinal);
            Assert.Equal(plaintext, restored);
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }
}
