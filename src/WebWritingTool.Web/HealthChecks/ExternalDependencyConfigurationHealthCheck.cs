namespace WebWritingTool.Web.HealthChecks;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.Infrastructure.Search;

internal sealed class ExternalDependencyConfigurationHealthCheck(
    IOptions<GeminiOptions> geminiOptions,
    IOptions<SearchProviderOptions> searchProviderOptions)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(geminiOptions.Value.ApiKey))
        {
            missing.Add("Gemini API key");
        }

        if (string.IsNullOrWhiteSpace(searchProviderOptions.Value.Tavily.ApiKey))
        {
            missing.Add("Tavily API key");
        }

        if (string.IsNullOrWhiteSpace(searchProviderOptions.Value.X.BearerToken))
        {
            missing.Add("X API bearer token");
        }

        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy("External dependency configuration is present.")
            : HealthCheckResult.Degraded($"Missing external dependency configuration: {string.Join(", ", missing)}."));
    }
}
