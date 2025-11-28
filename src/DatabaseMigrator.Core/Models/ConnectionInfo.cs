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
        DatabaseType.SqlServer => $"Server={Server},{Port};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True;",
        DatabaseType.PostgreSQL => $"Host={Server};Port={Port};Database={Database};Username={Username};Password={Password};",
        DatabaseType.Oracle => $"Data Source={Server}:{Port}/{Database};User Id={Username};Password={Password};",
        _ => throw new NotSupportedException($"Database type {DatabaseType} not supported")
    };
}
