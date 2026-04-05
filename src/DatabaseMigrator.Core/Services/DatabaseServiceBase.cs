using System;
using System.IO;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// Shared base for database services: retry logic, connection creation, identifier escaping,
/// and table/column name formatting.
/// </summary>
public abstract class DatabaseServiceBase
{
    protected readonly int _commandTimeoutSeconds;
    private readonly int _retryCount;
    private readonly int _retryInitialDelayMilliseconds;
    private readonly bool _enableTransientRetries;

    protected static void Log(string message) => LoggerService.Log(message);

    protected DatabaseServiceBase()
    {
        var opts = RuntimeOptionsProvider.Current.Database;
        _commandTimeoutSeconds = opts.CommandTimeoutSeconds;
        _retryCount = opts.RetryCount;
        _retryInitialDelayMilliseconds = opts.RetryInitialDelayMilliseconds;
        _enableTransientRetries = opts.EnableTransientRetries;
    }

    protected async Task ExecuteWithRetryAsync(Func<Task> operation, string operationName)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, operationName);
    }

    protected async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (_enableTransientRetries && attempt < _retryCount && IsTransient(ex))
            {
                attempt++;
                int delayMs = (int)Math.Min(_retryInitialDelayMilliseconds * Math.Pow(2, attempt - 1), 10_000);
                Log($"[{operationName}] Transient error (attempt {attempt}/{_retryCount}), retrying in {delayMs}ms: {ex.Message}");
                await Task.Delay(delayMs);
            }
        }
    }

    protected static bool IsTransient(Exception ex)
    {
        if (ex is IOException or TimeoutException)
            return true;

        if (ex is SqlException sqlEx)
            return sqlEx.Number is -2 or 53 or 1205 or 4060 or 10928 or 10929 or 10053 or 10054 or 10060;

        if (ex is NpgsqlException npgsqlEx && npgsqlEx.IsTransient)
            return true;

        if (ex is OracleException oracleEx)
            return oracleEx.Number is 1013 or 1033 or 1034 or 1089 or 1090 or 1092 or 12514 or 12537 or 12541;

        if (ex.InnerException != null)
            return IsTransient(ex.InnerException);

        string msg = ex.Message.ToLowerInvariant();
        return msg.Contains("timeout") || msg.Contains("deadlock") ||
               msg.Contains("network") || msg.Contains("transport-level error") ||
               msg.Contains("connection is broken");
    }

    protected static DbConnection CreateConnection(ConnectionInfo connectionInfo) => connectionInfo.DatabaseType switch
    {
        DatabaseType.SqlServer => new SqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.PostgreSQL => new NpgsqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.Oracle => new OracleConnection(connectionInfo.GetConnectionString()),
        _ => throw new NotSupportedException($"Database type {connectionInfo.DatabaseType} not supported")
    };

    protected static string FormatTableName(DatabaseType dbType, string schema, string tableName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer =>
                $"[{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(tableName)}]",
            DatabaseType.PostgreSQL =>
                $"\"{EscapePostgresIdentifier(schema.ToLowerInvariant())}\".\"{EscapePostgresIdentifier(tableName.ToLowerInvariant())}\"",
            DatabaseType.Oracle =>
                $"{schema.ToUpperInvariant()}.{tableName.ToUpperInvariant()}",
            _ => throw new NotSupportedException($"Database type {dbType} not supported")
        };
    }

    protected static string FormatColumnName(DatabaseType dbType, string columnName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(columnName)}]",
            DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(columnName.ToLowerInvariant())}\"",
            DatabaseType.Oracle => columnName.ToUpperInvariant(),
            _ => columnName
        };
    }

    protected static string EscapeSqlServerIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        return identifier.Replace("]", "]]");
    }

    protected static string EscapePostgresIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        return identifier.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Escapes an Oracle identifier for use within double-quoted identifier syntax
    /// (e.g. constraint names, column names in DDL). Doubles any embedded double-quote characters.
    /// For Oracle user/schema creation use <see cref="DatabaseService.ValidateOracleUserIdentifier"/> instead.
    /// </summary>
    protected static string EscapeOracleIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        return identifier.Replace("\"", "\"\"");
    }
}
