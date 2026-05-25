namespace WebWritingTool.Application.Security;

public interface IUrlSafetyValidator
{
    Task<UrlSafetyValidationResult> ValidateHttpsPublicUrlAsync(
        string? value,
        CancellationToken cancellationToken = default);
}

public sealed record UrlSafetyValidationResult(
    bool Succeeded,
    string? ErrorMessage,
    Uri? Uri)
{
    public static UrlSafetyValidationResult Success(Uri uri)
    {
        return new UrlSafetyValidationResult(true, null, uri);
    }

    public static UrlSafetyValidationResult Failure(string message)
    {
        return new UrlSafetyValidationResult(false, message, null);
    }
}
