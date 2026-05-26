namespace WebWritingTool.Application.Security;

public interface ISecretMasker
{
    string Mask(string? value);
}
