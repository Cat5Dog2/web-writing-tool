using WebWritingTool.Web.Components;
using WebWritingTool.Web.Configuration;
using WebWritingTool.Web.Endpoints;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Application.Security;

DevelopmentEnvironmentFileConfiguration.TryLoadAspNetCoreEnvironmentFromDotEnv();

var builder = WebApplication.CreateBuilder(args);
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
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
