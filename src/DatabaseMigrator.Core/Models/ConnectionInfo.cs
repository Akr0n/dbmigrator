using System;
using Npgsql;

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

    /// <summary>
    /// PostgreSQL and Oracle only (SQL Server is always encrypted). Off: the driver's own default, which uses TLS when the
    /// server offers it and otherwise falls back to plaintext without verifying anything. On: the connection is encrypted and
    /// fails if the server cannot do that; the server certificate is verified unless <see cref="TrustServerCertificate"/> is set.
    /// </summary>
    public bool RequireEncryption { get; set; }

    /// <summary>The same connection to another database of the same server (a copy: every setting is kept).</summary>
    public ConnectionInfo WithDatabase(string database)
    {
        var copy = (ConnectionInfo)MemberwiseClone();
        copy.Database = database;
        return copy;
    }

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
        // Use NpgsqlConnectionStringBuilder so special characters in any field
        // (especially the password) are correctly handled without manual escaping.
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Server,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password
        };
        if (RequireEncryption)
        {
            // Require encrypts but verifies nothing (that is what "accept the certificate" asks for); VerifyFull also checks
            // the certificate chain and that it was issued for this host.
            builder.SslMode = TrustServerCertificate ? SslMode.Require : SslMode.VerifyFull;
        }
        System.Diagnostics.Debug.WriteLine($"[ConnectionInfo] PostgreSQL target {Server}:{Port}/{Database} (user={Username})");
        return builder.ConnectionString;
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
        // TCPS is TCP over TLS. The certificate chain is checked against the Windows trust store / the client wallet; "accept the
        // certificate" only skips the check that its name is the server's.
        string protocol = RequireEncryption ? "TCPS" : "TCP";
        string security = RequireEncryption ? $"(SECURITY=(SSL_SERVER_DN_MATCH={(TrustServerCertificate ? "no" : "yes")}))" : "";
        var cs = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL={protocol})(HOST={Server})(PORT={Port}))(CONNECT_DATA=(SERVICE_NAME={Database})){security});User Id={Username};Password={escapedPassword};";
        
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

    private string EscapeOraclePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return password;

        // Always wrap in double quotes to safely handle any special characters
        // (@, !, #, $, ;, =, spaces, etc.) that could break Oracle connection string parsing.
        // Escape any internal double-quote characters by doubling them.
        return $"\"{password.Replace("\"", "\"\"")}\"";
    }
}
