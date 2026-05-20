namespace WebWritingTool.Infrastructure.BackgroundJobs;

public static class JobErrorCodes
{
    public const string ValidationError = "ValidationError";
    public const string UnauthorizedExternalApi = "UnauthorizedExternalApi";
    public const string ForbiddenExternalApi = "ForbiddenExternalApi";
    public const string RateLimited = "RateLimited";
    public const string Timeout = "Timeout";
    public const string ExternalServerError = "ExternalServerError";
    public const string ExternalBadResponse = "ExternalBadResponse";
    public const string NetworkError = "NetworkError";
    public const string UsageLimitExceeded = "UsageLimitExceeded";
    public const string NotFound = "NotFound";
    public const string Conflict = "Conflict";
    public const string UnknownError = "UnknownError";
}
