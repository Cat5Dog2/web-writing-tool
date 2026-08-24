using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Security;

// docs/security-design.md 18.2、18.3 のセキュリティヘッダーが実際の応答に付くことを検証する。
[Collection(IntegrationTestCollection.Name)]
public class SecurityHeadersTests(IntegrationTestFixture fixture)
{
    private const string ContentTypeOptionsHeaderName = "X-Content-Type-Options";
    private const string ReferrerPolicyHeaderName = "Referrer-Policy";
    private const string FrameOptionsHeaderName = "X-Frame-Options";
    private const string ContentSecurityPolicyHeaderName = "Content-Security-Policy";
    private const string ContentSecurityPolicyReportOnlyHeaderName = "Content-Security-Policy-Report-Only";

    [Fact]
    public async Task PageResponse_HasBaselineSecurityHeaders()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", GetHeaderValue(response.Headers, ContentTypeOptionsHeaderName));
        Assert.Equal(
            "strict-origin-when-cross-origin",
            GetHeaderValue(response.Headers, ReferrerPolicyHeaderName));
        Assert.Equal("DENY", GetHeaderValue(response.Headers, FrameOptionsHeaderName));
    }

    [Fact]
    public async Task HealthResponse_HasBaselineSecurityHeaders()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", GetHeaderValue(response.Headers, ContentTypeOptionsHeaderName));
        Assert.Equal("DENY", GetHeaderValue(response.Headers, FrameOptionsHeaderName));
    }

    // UseStatusCodePagesWithReExecuteはパイプラインを再実行する。
    // ヘッダーが再実行後の応答にも残ることを確認する。
    [Fact]
    public async Task ReExecutedNotFoundResponse_KeepsSecurityHeaders()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/this-route-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", GetHeaderValue(response.Headers, ContentTypeOptionsHeaderName));
        Assert.Equal(
            "strict-origin-when-cross-origin",
            GetHeaderValue(response.Headers, ReferrerPolicyHeaderName));
        Assert.Equal("DENY", GetHeaderValue(response.Headers, FrameOptionsHeaderName));
    }

    [Fact]
    public async Task ContentSecurityPolicy_ByDefault_IsSentAsReportOnly()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/login");

        var policy = GetHeaderValue(response.Headers, ContentSecurityPolicyReportOnlyHeaderName);
        Assert.NotNull(policy);
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("form-action 'self'", policy);

        // Blazorが付ける frame-ancestors 'self' を上書きし、段階導入中もframe埋め込みは拒否する。
        Assert.Equal(
            "frame-ancestors 'none'",
            GetHeaderValue(response.Headers, ContentSecurityPolicyHeaderName));
    }

    [Fact]
    public async Task ContentSecurityPolicy_WhenEnforce_IsSentAsEnforcingHeader()
    {
        using var factory = new TestApplicationFactory(
            fixture.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Security:ContentSecurityPolicyMode"] = "Enforce"
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/login");

        var policy = GetHeaderValue(response.Headers, ContentSecurityPolicyHeaderName);
        Assert.NotNull(policy);
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.False(response.Headers.Contains(ContentSecurityPolicyReportOnlyHeaderName));
    }

    [Fact]
    public async Task ContentSecurityPolicy_WhenDisabled_IsNotSent()
    {
        using var factory = new TestApplicationFactory(
            fixture.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Security:ContentSecurityPolicyMode"] = "Disabled"
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/login");

        Assert.False(
            response.Headers.Contains(ContentSecurityPolicyReportOnlyHeaderName),
            Describe(response.Headers));

        // Disabledではアプリ側のポリシーを一切書かない。Blazorが独自に付ける値は残る。
        var policy = GetHeaderValue(response.Headers, ContentSecurityPolicyHeaderName);
        Assert.DoesNotContain("default-src 'self'", policy ?? string.Empty);
        Assert.NotEqual("frame-ancestors 'none'", policy);

        // CSPを止めても他のヘッダーは残る。
        Assert.Equal("nosniff", GetHeaderValue(response.Headers, ContentTypeOptionsHeaderName));
    }

    // 未定義値でもEnumへはバインドされてしまうため、起動時に弾けることを確認する。
    // ここを通してしまうとMiddlewareのswitchがどのcaseにも入らず、CSPだけが黙って消える。
    [Fact]
    public void ContentSecurityPolicyMode_WithUndefinedValue_FailsStartup()
    {
        using var factory = new TestApplicationFactory(
            fixture.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Security:ContentSecurityPolicyMode"] = "9999"
            });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(
            "Security:ContentSecurityPolicyMode must be ReportOnly, Enforce or Disabled.",
            exception.Failures);
    }

    private static string Describe(HttpResponseHeaders headers)
    {
        return string.Join(" | ", headers.Select(header => $"{header.Key}={string.Join(",", header.Value)}"));
    }

    private static string? GetHeaderValue(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
