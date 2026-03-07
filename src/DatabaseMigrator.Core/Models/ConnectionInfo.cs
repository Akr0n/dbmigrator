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
    public bool TrustServerCertificate { get; set; } = RuntimeOptionsProvider.Current.Security.TrustServerCertificateByDefault;

    public string GetConnectionString() => DatabaseType switch
    {
        DatabaseType.SqlServer => BuildSqlServerConnectionString(),
        DatabaseType.PostgreSQL => BuildPostgresConnectionString(),
        DatabaseType.Oracle => BuildOracleConnectionString(),
        _ => throw new NotSupportedException($"Database type {DatabaseType} not supported")
    };

    private string BuildSqlServerConnectionString()
    {
        string trustOption = TrustServerCertificate ? "True" : "False";

        // Se username è vuoto, usa Integrated Security (connessione trusted)
        if (string.IsNullOrWhiteSpace(Username))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ConnectionInfo] SQL Server target {Server}:{Port}/{Database} (IntegratedSecurity=True, TrustServerCertificate={trustOption})");
            return $"Server={Server},{Port};Database={Database};Integrated Security=true;Encrypt=True;TrustServerCertificate={trustOption};";
        }

        System.Diagnostics.Debug.WriteLine(
            $"[ConnectionInfo] SQL Server target {Server}:{Port}/{Database} (SqlAuth user={Username}, TrustServerCertificate={trustOption})");
        return $"Server={Server},{Port};Database={Database};User Id={Username};Password={Password};Encrypt=True;TrustServerCertificate={trustOption};";
    }

    private string BuildPostgresConnectionString()
    {
        // Escape password se contiene caratteri speciali
        string escapedPassword = EscapePostgresPassword(Password);
        var cs = $"Host={Server};Port={Port};Database={Database};Username={Username};Password={escapedPassword};";
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] PostgreSQL target {Server}:{Port}/{Database} (user={Username})");
        return cs;
    }

    private string BuildOracleConnectionString()
    {
        // Oracle connection string using TNS format
        // For oracle-free: Server=localhost, Port=1521, Database=FREEPDB1 (the PDB service)
        // Escape password if it contains special characters like ;
        //
        // IMPORTANT: Database/user creation operations (e.g. CREATE USER, GRANT privileges)
        // require Oracle SYSTEM privileges. If connecting with a non-SYS administrative user,
        // ensure that account has at least:
        // - CREATE SESSION system privilege (to be able to connect)
        // - CREATE USER system privilege for creating new schema users
        // - GRANT ANY PRIVILEGE system privilege for assigning privileges to new users
        // These are powerful SYSTEM privileges and must only be granted to DBA/administrative
        // accounts, never to regular application users. Without appropriate privileges, user /
        // schema creation operations will fail with ORA-01031 (insufficient privileges).
        //
        // WARNING: Non-SYS users CANNOT use SYSDBA privilege. Only the SYS user can connect with
        // SYSDBA privilege. If using a non-SYS administrative account (e.g., SYSTEM or custom DBA),
        // that account MUST have the system privileges listed above explicitly granted to it.
        // Attempting to use SYSDBA with non-SYS users or lacking required privileges will result
        // in ORA-01031 (insufficient privileges) errors during database/user creation operations.
        string escapedPassword = EscapeOraclePassword(Password);
        var cs = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={Server})(PORT={Port}))(CONNECT_DATA=(SERVICE_NAME={Database})));User Id={Username};Password={escapedPassword};";
        
        // Only add SYSDBA privilege if connecting as SYS user
        // SYSDBA provides full database control and should not be used for regular operations
        // Note: Oracle usernames are case-insensitive, so both 'SYS' and 'sys' should be treated as SYS user
        if (string.Equals(Username, "SYS", StringComparison.OrdinalIgnoreCase))
        {
            cs += "DBA Privilege=SYSDBA;";
        }
        
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] Oracle target {Server}:{Port}/{Database} (user={Username})");
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
