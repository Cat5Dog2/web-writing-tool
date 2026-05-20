using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebWritingTool.Infrastructure.Data;

public sealed class ApplicationDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = GetConnectionString()
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. Set it with user-secrets or ConnectionStrings__DefaultConnection.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string? GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:DefaultConnection")
            ?? ReadFromUserSecrets()
            ?? ReadFromAppSettings();
    }

    private static string? ReadFromUserSecrets()
    {
        var userSecretsId = ReadUserSecretsId();
        if (string.IsNullOrWhiteSpace(userSecretsId))
        {
            return null;
        }

        foreach (var secretsPath in GetUserSecretsPaths(userSecretsId))
        {
            var connectionString = ReadConnectionStringFromJson(secretsPath);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }
        }

        return null;
    }

    private static string? ReadUserSecretsId()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var webProjectPath = Path.Combine(
            repoRoot.FullName,
            "src",
            "WebWritingTool.Web",
            "WebWritingTool.Web.csproj");

        if (!File.Exists(webProjectPath))
        {
            return null;
        }

        var document = XDocument.Load(webProjectPath);
        return document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "UserSecretsId")
            ?.Value;
    }

    private static IEnumerable<string> GetUserSecretsPaths(string userSecretsId)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".microsoft", "usersecrets", userSecretsId, "secrets.json");
        }
    }

    private static string? ReadFromAppSettings()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var appSettingsPath = Path.Combine(
            repoRoot.FullName,
            "src",
            "WebWritingTool.Web",
            "appsettings.json");

        return ReadConnectionStringFromJson(appSettingsPath);
    }

    private static string? ReadConnectionStringFromJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (root.TryGetProperty("ConnectionStrings:DefaultConnection", out var flatValue))
        {
            return flatValue.GetString();
        }

        if (root.TryGetProperty("ConnectionStrings", out var connectionStrings)
            && connectionStrings.TryGetProperty("DefaultConnection", out var nestedValue))
        {
            return nestedValue.GetString();
        }

        return null;
    }

    private static DirectoryInfo? FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WebWritingTool.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
