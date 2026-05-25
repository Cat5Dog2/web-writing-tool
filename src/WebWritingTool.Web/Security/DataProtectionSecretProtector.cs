using Microsoft.AspNetCore.DataProtection;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Web.Security;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "WebWritingTool.Secrets.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        return _protector.Unprotect(protectedValue);
    }
}
