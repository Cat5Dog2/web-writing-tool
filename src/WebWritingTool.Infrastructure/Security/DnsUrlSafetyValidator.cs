using System.Net;
using System.Net.Sockets;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Infrastructure.Security;

public sealed class DnsUrlSafetyValidator : IUrlSafetyValidator
{
    private static readonly HashSet<string> ForbiddenHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "metadata.google.internal"
    };

    public async Task<UrlSafetyValidationResult> ValidateHttpsPublicUrlAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return UrlSafetyValidationResult.Failure("HTTPS URLを指定してください。");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return UrlSafetyValidationResult.Failure("HTTPS URLのみ指定できます。");
        }

        if (!uri.IsDefaultPort && uri.Port != 443)
        {
            return UrlSafetyValidationResult.Failure("許可されていないポート番号は指定できません。");
        }

        if (string.IsNullOrWhiteSpace(uri.Host)
            || ForbiddenHosts.Contains(uri.Host)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return UrlSafetyValidationResult.Failure("内部ホストは指定できません。");
        }

        if (IPAddress.TryParse(uri.Host, out var ipAddress))
        {
            return IsForbiddenAddress(ipAddress)
                ? UrlSafetyValidationResult.Failure("内部ネットワークのURLは指定できません。")
                : UrlSafetyValidationResult.Success(Normalize(uri));
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            if (addresses.Length == 0)
            {
                return UrlSafetyValidationResult.Failure("ホスト名を解決できません。");
            }

            if (addresses.Any(IsForbiddenAddress))
            {
                return UrlSafetyValidationResult.Failure("内部ネットワークのURLは指定できません。");
            }
        }
        catch (SocketException)
        {
            return UrlSafetyValidationResult.Failure("ホスト名を解決できません。");
        }

        return UrlSafetyValidationResult.Success(Normalize(uri));
    }

    private static Uri Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var normalized = builder.Uri.ToString().TrimEnd('/');
        return new Uri(normalized, UriKind.Absolute);
    }

    private static bool IsForbiddenAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 => true,
                10 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.GetAddressBytes()[0] is >= 0xfc and <= 0xfd;
        }

        return true;
    }
}
