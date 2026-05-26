using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using WebWritingTool.Web.Components;
using WebWritingTool.Web.Configuration;
using WebWritingTool.Web.Endpoints;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Application.Security;

DevelopmentEnvironmentFileConfiguration.TryLoadAspNetCoreEnvironmentFromDotEnv();

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("Test"))
{
    StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
}

builder.AddDevelopmentEnvironmentFileConfiguration();
if (builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration, builder.Environment)
    .AddWebAuthorization();

var app = builder.Build();
var securityOptions = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

// Configure the HTTP request pipeline.
if (securityOptions.ShouldUseForwardedHeaders(app.Environment))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (securityOptions.RequireHttps)
    {
        app.UseHsts();
    }
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (securityOptions.RequireHttps)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();
app.MapHealthChecks("/health/deps", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("deps")
}).RequireAuthorization(ApplicationPolicies.RequireAdmin);
app.MapSecurityEndpoints();
app.MapAccountEndpoints();
app.MapArticleEndpoints();
app.MapHeadingEndpoints();
app.MapJobEndpoints();
app.MapWordpressEndpoints();
app.MapNotificationEndpoints();
app.MapAdminEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

try
{
    await SeedIdentityAsync(app);
    await app.RunAsync();
}
catch (Exception exception)
{
    try
    {
        var masker = app.Services.GetService<ISecretMasker>();
        app.Logger.LogCritical(
            "Application terminated unexpectedly. exceptionType={ExceptionType} message={Message}",
            exception.GetType().Name,
            masker?.Mask(exception.Message) ?? "Application startup failed.");
    }
    catch
    {
        Console.Error.WriteLine($"Application terminated unexpectedly: {exception.GetType().Name}");
    }

    Environment.ExitCode = 1;
}

static async Task SeedIdentityAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IIdentityDataSeeder>();
    await seeder.SeedAsync();
}

public partial class Program;
