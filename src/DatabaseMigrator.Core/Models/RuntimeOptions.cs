using System;
using System.IO;
using System.Text.Json;

namespace DatabaseMigrator.Core.Models;

public sealed class RuntimeOptions
{
    public DatabaseRuntimeOptions Database { get; set; } = new();
    public LoggingRuntimeOptions Logging { get; set; } = new();
    public SecurityRuntimeOptions Security { get; set; } = new();
}

public sealed class DatabaseRuntimeOptions
{
    public int BatchSize { get; set; } = 1000;
    public int CommandTimeoutSeconds { get; set; } = 300;
    public int RowCountMaxConcurrency { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int RetryInitialDelayMilliseconds { get; set; } = 500;
    public bool EnableTransientRetries { get; set; } = true;
}

public sealed class LoggingRuntimeOptions
{
    public int MaxFileSizeMb { get; set; } = 10;
    public int RetentionDays { get; set; } = 14;
    public int MaxArchivedFiles { get; set; } = 20;
}

public sealed class SecurityRuntimeOptions
{
    public bool TrustServerCertificateByDefault { get; set; } = false;
    public bool AllowPlaintextConfigFallback { get; set; } = false;
}

public static class RuntimeOptionsProvider
{
    private static readonly Lazy<RuntimeOptions> LazyCurrent = new(LoadInternal);

    public static RuntimeOptions Current => LazyCurrent.Value;

    private static RuntimeOptions LoadInternal()
    {
        var current = new RuntimeOptions();

        foreach (var path in GetSettingsPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<RuntimeOptions>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    Merge(current, parsed);
                }
            }
            catch
            {
                // Keep defaults if config parsing fails.
            }
        }

        ApplyEnvironmentOverrides(current);
        Normalize(current);
        return current;
    }

    private static string[] GetSettingsPaths()
    {
        var appBasePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var localAppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseMigrator",
            "appsettings.json");

        return new[] { appBasePath, localAppDataPath };
    }

    private static void Merge(RuntimeOptions target, RuntimeOptions source)
    {
        target.Database = source.Database ?? target.Database;
        target.Logging = source.Logging ?? target.Logging;
        target.Security = source.Security ?? target.Security;
    }

    private static void ApplyEnvironmentOverrides(RuntimeOptions options)
    {
        options.Database.BatchSize = GetIntEnv("DBMIGRATOR_BATCH_SIZE", options.Database.BatchSize);
        options.Database.CommandTimeoutSeconds = GetIntEnv("DBMIGRATOR_COMMAND_TIMEOUT_SECONDS", options.Database.CommandTimeoutSeconds);
        options.Database.RowCountMaxConcurrency = GetIntEnv("DBMIGRATOR_ROWCOUNT_MAX_CONCURRENCY", options.Database.RowCountMaxConcurrency);
        options.Database.RetryCount = GetIntEnv("DBMIGRATOR_RETRY_COUNT", options.Database.RetryCount);
        options.Database.RetryInitialDelayMilliseconds = GetIntEnv("DBMIGRATOR_RETRY_INITIAL_DELAY_MS", options.Database.RetryInitialDelayMilliseconds);
        options.Database.EnableTransientRetries = GetBoolEnv("DBMIGRATOR_ENABLE_RETRIES", options.Database.EnableTransientRetries);

        options.Logging.MaxFileSizeMb = GetIntEnv("DBMIGRATOR_LOG_MAX_FILE_MB", options.Logging.MaxFileSizeMb);
        options.Logging.RetentionDays = GetIntEnv("DBMIGRATOR_LOG_RETENTION_DAYS", options.Logging.RetentionDays);
        options.Logging.MaxArchivedFiles = GetIntEnv("DBMIGRATOR_LOG_MAX_ARCHIVED_FILES", options.Logging.MaxArchivedFiles);

        options.Security.TrustServerCertificateByDefault = GetBoolEnv("DBMIGRATOR_TRUST_SERVER_CERTIFICATE", options.Security.TrustServerCertificateByDefault);
        options.Security.AllowPlaintextConfigFallback = GetBoolEnv("DBMIGRATOR_ALLOW_PLAINTEXT_CONFIG_FALLBACK", options.Security.AllowPlaintextConfigFallback);
    }

    private static int GetIntEnv(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static bool GetBoolEnv(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static void Normalize(RuntimeOptions options)
    {
        options.Database.BatchSize = Math.Max(1, options.Database.BatchSize);
        options.Database.CommandTimeoutSeconds = Math.Max(30, options.Database.CommandTimeoutSeconds);
        options.Database.RowCountMaxConcurrency = Math.Clamp(options.Database.RowCountMaxConcurrency, 1, 64);
        options.Database.RetryCount = Math.Clamp(options.Database.RetryCount, 0, 10);
        options.Database.RetryInitialDelayMilliseconds = Math.Clamp(options.Database.RetryInitialDelayMilliseconds, 50, 10_000);

        options.Logging.MaxFileSizeMb = Math.Clamp(options.Logging.MaxFileSizeMb, 1, 500);
        options.Logging.RetentionDays = Math.Clamp(options.Logging.RetentionDays, 1, 365);
        options.Logging.MaxArchivedFiles = Math.Clamp(options.Logging.MaxArchivedFiles, 1, 200);
    }
}
