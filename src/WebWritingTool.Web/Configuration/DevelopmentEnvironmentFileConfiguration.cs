using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace WebWritingTool.Web.Configuration;

internal static class DevelopmentEnvironmentFileConfiguration
{
    private const string EnvironmentFileName = ".env";

    public static void TryLoadAspNetCoreEnvironmentFromDotEnv()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            return;
        }

        var envFilePath = FindEnvironmentFile();
        if (envFilePath is null)
        {
            return;
        }

        var values = ReadEnvironmentFile(envFilePath);
        if (values.TryGetValue("ASPNETCORE_ENVIRONMENT", out var environment)
            && !string.IsNullOrWhiteSpace(environment))
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
        }
    }

    public static WebApplicationBuilder AddDevelopmentEnvironmentFileConfiguration(
        this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var envFilePath = FindEnvironmentFile(builder.Environment.ContentRootPath);
        if (envFilePath is null)
        {
            return builder;
        }

        var values = ReadEnvironmentFile(envFilePath);
        var additions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            var configurationKey = key.Replace("__", ":", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(builder.Configuration[configurationKey]))
            {
                additions[configurationKey] = NormalizeDevelopmentConfigurationValue(configurationKey, value);
            }
        }

        var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var normalizedConnectionString = NormalizeDevelopmentConfigurationValue(
                "ConnectionStrings:DefaultConnection",
                configuredConnectionString);

            if (!string.Equals(configuredConnectionString, normalizedConnectionString, StringComparison.Ordinal))
            {
                additions["ConnectionStrings:DefaultConnection"] = normalizedConnectionString;
            }
        }

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection"))
            && !additions.ContainsKey("ConnectionStrings:DefaultConnection")
            && values.TryGetValue("POSTGRES_PASSWORD", out var password)
            && !string.IsNullOrWhiteSpace(password))
        {
            var database = values.GetValueOrDefault("POSTGRES_DB") ?? "web_writing_tool";
            var username = values.GetValueOrDefault("POSTGRES_USER") ?? "web_writing_tool";
            additions["ConnectionStrings:DefaultConnection"] =
                $"Host=127.0.0.1;Port=5432;Database={database};Username={username};Password={password}";
        }

        if (additions.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(additions);
        }

        return builder;
    }

    private static string NormalizeDevelopmentConfigurationValue(string key, string value)
    {
        if (!key.Equals("ConnectionStrings:DefaultConnection", StringComparison.OrdinalIgnoreCase)
            || IsRunningInContainer())
        {
            return value;
        }

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = value
            };

            NormalizeLocalDatabaseHost(builder, "Host");
            NormalizeLocalDatabaseHost(builder, "Server");

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    private static void NormalizeLocalDatabaseHost(DbConnectionStringBuilder builder, string key)
    {
        if (!builder.TryGetValue(key, out var hostValue)
            || hostValue is not string host
            || (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("postgres", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        builder[key] = "127.0.0.1";
    }

    private static bool IsRunningInContainer()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ReadEnvironmentFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = TrimQuotes(value);
        }

        return values;
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string? FindEnvironmentFile(string? contentRootPath = null)
    {
        var candidates = new[]
        {
            contentRootPath,
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var directory = string.IsNullOrWhiteSpace(candidate)
                ? null
                : new DirectoryInfo(candidate);

            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, EnvironmentFileName);
                if (File.Exists(path))
                {
                    return path;
                }

                if (File.Exists(Path.Combine(directory.FullName, "WebWritingTool.slnx")))
                {
                    return null;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
