using System;

namespace DatabaseMigrator.Core.Models;

public class ConnectionInfo
{
    public DatabaseType DatabaseType { get; set; }
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string GetConnectionString() => DatabaseType switch
    {
        DatabaseType.SqlServer => BuildSqlServerConnectionString(),
        DatabaseType.PostgreSQL => BuildPostgresConnectionString(),
        DatabaseType.Oracle => BuildOracleConnectionString(),
        _ => throw new NotSupportedException($"Database type {DatabaseType} not supported")
    };

    private string BuildSqlServerConnectionString()
    {
        // Se username è vuoto, usa Integrated Security (connessione trusted)
        if (string.IsNullOrWhiteSpace(Username))
        {
            return $"Server={Server},{Port};Database={Database};Integrated Security=true;TrustServerCertificate=True;";
        }
        else
        {
            var cs = $"Server={Server},{Port};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True;";
            System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] SQL Server: {cs}");
            return cs;
        }
    }

    private string BuildPostgresConnectionString()
    {
        var cs = $"Host={Server};Port={Port};Database={Database};Username={Username};Password={Password};";
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] PostgreSQL: {cs}");
        return cs;
    }

    private string BuildOracleConnectionString()
    {
        var cs = $"Data Source={Server}:{Port}/{Database};User Id={Username};Password={Password};";
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] Oracle: {cs}");
        return cs;
    }
}
