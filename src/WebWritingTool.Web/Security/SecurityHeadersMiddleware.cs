namespace WebWritingTool.Web.Security;

using Microsoft.Extensions.Options;
using WebWritingTool.Web.Configuration;

// docs/security-design.md 18.2、18.3 のセキュリティヘッダーを付与する。
// Caddyfileではなくアプリ側で付与するのは、共通Caddy構成（docker-compose.shared-caddy.yml）では
// 同梱Caddyがprofileで停止し、リポジトリのCaddyfileが読まれないため。
// アプリ側なら同梱Caddy構成でも共通Caddy構成でも同じヘッダーが必ず付く。
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    private const string ContentTypeOptionsHeaderName = "X-Content-Type-Options";
    private const string ReferrerPolicyHeaderName = "Referrer-Policy";
    private const string FrameOptionsHeaderName = "X-Frame-Options";
    private const string ContentSecurityPolicyHeaderName = "Content-Security-Policy";
    private const string ContentSecurityPolicyReportOnlyHeaderName = "Content-Security-Policy-Report-Only";

    // 目標ポリシー。Blazorの<ImportMap />が出すインラインscriptとNavMenu.razorのインラインonclickが
    // script-src 'self' に違反するため、既定はReportOnlyのまま運用する。
    // 両方を解消してからSecurity__ContentSecurityPolicyMode=EnforceでEnforceへ移行する。
    private const string ContentSecurityPolicyValue =
        "default-src 'self'; "
        + "base-uri 'self'; "
        + "object-src 'none'; "
        + "frame-ancestors 'none'; "
        + "form-action 'self'; "
        + "img-src 'self' data:; "
        + "style-src 'self'; "
        + "script-src 'self'; "
        + "connect-src 'self'";

    // Blazorは対話コンポーネントの応答へ Content-Security-Policy: frame-ancestors 'self' を付ける。
    // frame-ancestorsはX-Frame-Optionsより優先されるため、そのままだと同一オリジンからのframe埋め込みを許す。
    // 段階導入中もclickjacking対策だけは効かせるよう、ReportOnlyでもこの値で上書きする。
    private const string FrameAncestorsPolicyValue = "frame-ancestors 'none'";

    private readonly ContentSecurityPolicyMode _contentSecurityPolicyMode = options.Value.ContentSecurityPolicyMode;

    public Task InvokeAsync(HttpContext context)
    {
        var response = context.Response;

        // UseExceptionHandlerはエラーページ再実行の前にResponse.Clear()でヘッダーを消す。
        // ここで直接書くと500応答からヘッダーが欠落するため、送信直前のOnStartingで書く。
        response.OnStarting(() =>
        {
            ApplyHeaders(response.Headers);
            return Task.CompletedTask;
        });

        return next(context);
    }

    private void ApplyHeaders(IHeaderDictionary headers)
    {
        headers[ContentTypeOptionsHeaderName] = "nosniff";
        headers[ReferrerPolicyHeaderName] = "strict-origin-when-cross-origin";
        headers[FrameOptionsHeaderName] = "DENY";

        switch (_contentSecurityPolicyMode)
        {
            case ContentSecurityPolicyMode.Enforce:
                headers[ContentSecurityPolicyHeaderName] = ContentSecurityPolicyValue;
                headers.Remove(ContentSecurityPolicyReportOnlyHeaderName);
                break;
            case ContentSecurityPolicyMode.ReportOnly:
                headers[ContentSecurityPolicyHeaderName] = FrameAncestorsPolicyValue;
                headers[ContentSecurityPolicyReportOnlyHeaderName] = ContentSecurityPolicyValue;
                break;
            case ContentSecurityPolicyMode.Disabled:
                break;
        }
    }
}

internal static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
