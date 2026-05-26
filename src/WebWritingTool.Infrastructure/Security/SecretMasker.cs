using System.Text.RegularExpressions;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Infrastructure.Security;

public sealed partial class SecretMasker : ISecretMasker
{
    private const string Redacted = "[REDACTED]";

    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var masked = DiscordWebhookUrlPattern().Replace(
            value,
            "https://discord.com/api/webhooks/.../...");

        masked = AuthorizationHeaderPattern().Replace(
            masked,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{Redacted}");

        masked = CookieHeaderPattern().Replace(
            masked,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{Redacted}");

        masked = BearerTokenPattern().Replace(masked, $"Bearer {Redacted}");
        masked = BasicTokenPattern().Replace(masked, $"Basic {Redacted}");

        masked = SecretNameValuePattern().Replace(
            masked,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{Redacted}");

        masked = SecretQueryStringPattern().Replace(
            masked,
            match => $"{match.Groups["prefix"].Value}{Redacted}");

        return masked;
    }

    [GeneratedRegex(
        @"https://(?:discord(?:app)?\.com|canary\.discord\.com|ptb\.discord\.com)/api/webhooks/[^\s/""'<>]+/[^\s/""'<>]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex DiscordWebhookUrlPattern();

    [GeneratedRegex(
        @"(?<name>\bAuthorization\b)(?<separator>\s*[:=]\s*)(?<value>[^\r\n]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(
        @"(?<name>\b(?:Cookie|Set-Cookie)\b)(?<separator>\s*[:=]\s*)(?<value>[^\r\n]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CookieHeaderPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\bBasic\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BasicTokenPattern();

    [GeneratedRegex(
        @"(?<name>\b(?:api[_-]?key|access[_-]?token|bearer|password|application[_-]?password|app[-_]?pass|webhook(?:[_-]?url)?|token|secret)\b)(?<separator>\s*[:=]\s*)(?<value>""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretNameValuePattern();

    [GeneratedRegex(
        @"(?<prefix>[?&](?:api[_-]?key|access[_-]?token|token|password|application[_-]?password|app[-_]?pass|webhook(?:[_-]?url)?)=)[^&#\s]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretQueryStringPattern();
}
