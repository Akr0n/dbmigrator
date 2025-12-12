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
        // Escape password se contiene caratteri speciali
        string escapedPassword = EscapePostgresPassword(Password);
        var cs = $"Host={Server};Port={Port};Database={Database};Username={Username};Password={escapedPassword};";
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] PostgreSQL: {cs}");
        return cs;
    }

    private string BuildOracleConnectionString()
    {
        // Oracle connection string using TNS format
        // For SYS user, we need to add DBA Privilege=SYSDBA
        // For XE: Server=localhost, Port=1521, Database=XE (the SID)
        // Escape password if it contains special characters like ;
        string escapedPassword = EscapeOraclePassword(Password);
        var cs = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={Server})(PORT={Port}))(CONNECT_DATA=(SERVICE_NAME={Database})));User Id={Username};Password={escapedPassword};DBA Privilege=SYSDBA;";
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] Oracle: {cs}");
        return cs;
    }

    private string EscapePostgresPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return password;
        
        // PostgreSQL: escape single quotes and backslashes
        return password.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private string EscapeOraclePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return password;
        
        // Oracle: enclose password in quotes if it contains special characters
        if (password.Contains(";") || password.Contains("=") || password.Contains("(") || password.Contains(")"))
        {
            // Escape internal quotes by doubling them
            return $"\"{password.Replace("\"", "\"\"")}\"";
        }
        return password;
    }
}
