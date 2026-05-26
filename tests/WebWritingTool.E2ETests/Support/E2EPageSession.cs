using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebWritingTool.E2ETests.Support;

public sealed partial class E2EPageSession(
    IBrowserContext context,
    IPage page,
    string tracePath,
    string screenshotDirectory)
    : IAsyncDisposable
{
    public IPage Page => page;

    public async Task CaptureFailureScreenshotAsync()
    {
        Directory.CreateDirectory(screenshotDirectory);
        var path = Path.Combine(screenshotDirectory, $"{CreateSafeName(await page.TitleAsync())}-failure.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Path = path
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = tracePath
            });
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static string CreateSafeName(string value)
    {
        var safeName = SafeNamePattern().Replace(value, "_").Trim('_');
        return string.IsNullOrWhiteSpace(safeName) ? "page" : safeName;
    }

    [GeneratedRegex("[^a-zA-Z0-9_.-]+")]
    private static partial Regex SafeNamePattern();
}
