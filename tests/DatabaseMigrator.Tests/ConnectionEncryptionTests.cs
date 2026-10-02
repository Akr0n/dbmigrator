using System.Text.Json;
using DatabaseMigrator.Core.Models;
using Npgsql;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Test puri sulle opzioni di cifratura della connessione. Per SQL Server la connessione e' sempre cifrata e
/// "TrustServerCertificate" decide solo se il certificato viene verificato. PostgreSQL e Oracle invece non cifravano
/// nulla in modo obbligatorio e non verificavano il server: <see cref="ConnectionInfo.RequireEncryption"/> li rende
/// cifrati (e, salvo "accetta certificato", verificati) oppure la connessione fallisce.
/// </summary>
public class ConnectionEncryptionTests
{
    private static ConnectionInfo Info(DatabaseType type, bool requireEncryption = false, bool trust = false) => new()
    {
        DatabaseType = type, Server = "db.example.com", Port = 5432, Database = "d", Username = "u", Password = "p",
        RequireEncryption = requireEncryption, TrustServerCertificate = trust
    };

    private static NpgsqlConnectionStringBuilder Pg(ConnectionInfo info) => new(info.GetConnectionString());

    // ── PostgreSQL ───────────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_ByDefault_KeepsTheDriversOpportunisticMode()
        => Assert.Equal(SslMode.Prefer, Pg(Info(DatabaseType.PostgreSQL)).SslMode);

    [Fact]
    public void Postgres_RequireEncryption_RefusesPlaintextAndVerifiesTheServer()
        => Assert.Equal(SslMode.VerifyFull, Pg(Info(DatabaseType.PostgreSQL, requireEncryption: true)).SslMode);

    [Fact]
    public void Postgres_RequireEncryption_WithTrust_EncryptsWithoutVerifyingTheCertificate()
    {
        Assert.Equal(SslMode.Require, Pg(Info(DatabaseType.PostgreSQL, requireEncryption: true, trust: true)).SslMode);
    }

    [Fact]
    public void Postgres_TrustAlone_ChangesNothing()
    {
        // The box says "accept the server certificate": without a request for encryption there is none to accept.
        Assert.Equal(SslMode.Prefer, Pg(Info(DatabaseType.PostgreSQL, trust: true)).SslMode);
    }

    // ── Oracle ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Oracle_ByDefault_UsesPlainTcp()
    {
        string cs = Info(DatabaseType.Oracle).GetConnectionString();

        Assert.Contains("(PROTOCOL=TCP)", cs);
        Assert.DoesNotContain("TCPS", cs);
        Assert.DoesNotContain("SSL_SERVER_DN_MATCH", cs);
    }

    [Fact]
    public void Oracle_RequireEncryption_UsesTcpsAndChecksTheServerName()
    {
        string cs = Info(DatabaseType.Oracle, requireEncryption: true).GetConnectionString();

        Assert.Contains("(PROTOCOL=TCPS)", cs);
        Assert.DoesNotContain("(PROTOCOL=TCP)", cs);
        Assert.Contains("(SECURITY=(SSL_SERVER_DN_MATCH=yes))", cs);
    }

    [Fact]
    public void Oracle_RequireEncryption_WithTrust_SkipsOnlyTheServerNameCheck()
    {
        string cs = Info(DatabaseType.Oracle, requireEncryption: true, trust: true).GetConnectionString();

        Assert.Contains("(PROTOCOL=TCPS)", cs);
        Assert.Contains("(SECURITY=(SSL_SERVER_DN_MATCH=no))", cs);
    }

    // ── SQL Server ───────────────────────────────────────────────────────────────

    [Fact]
    public void SqlServer_IsAlwaysEncrypted_AndTrustDecidesOnlyTheVerification()
    {
        Assert.Contains("Encrypt=True;TrustServerCertificate=False;", Info(DatabaseType.SqlServer).GetConnectionString());
        Assert.Contains("Encrypt=True;TrustServerCertificate=True;", Info(DatabaseType.SqlServer, trust: true).GetConnectionString());
        Assert.Contains("Encrypt=True;TrustServerCertificate=False;", Info(DatabaseType.SqlServer, requireEncryption: true).GetConnectionString());
    }

    // ── a copy of the connection keeps every setting ─────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.Oracle)]
    [InlineData(DatabaseType.SqlServer)]
    public void WithDatabase_ChangesOnlyTheDatabase(DatabaseType type)
    {
        // The migration opens a second connection (master / postgres / the service) to check or create a database: it used to be
        // rebuilt field by field, which dropped any setting added later.
        var original = Info(type, requireEncryption: true, trust: true);

        var copy = original.WithDatabase("postgres");

        Assert.Equal("postgres", copy.Database);
        Assert.Equal("d", original.Database);
        Assert.Equal(original.RequireEncryption, copy.RequireEncryption);
        Assert.Equal(original.TrustServerCertificate, copy.TrustServerCertificate);
        Assert.Equal((original.DatabaseType, original.Server, original.Port, original.Username, original.Password),
            (copy.DatabaseType, copy.Server, copy.Port, copy.Username, copy.Password));
    }

    // ── saved configuration ──────────────────────────────────────────────────────

    [Fact]
    public void ASavedConfiguration_KeepsTheEncryptionRequest()
    {
        var saved = DatabaseConnectionData.FromConnectionInfo(Info(DatabaseType.PostgreSQL, requireEncryption: true));

        Assert.True(saved.ToConnectionInfo().RequireEncryption);
        Assert.Contains("\"requireEncryption\":true", JsonSerializer.Serialize(saved));
    }

    [Fact]
    public void AConfigurationSavedBeforeTheOption_LoadsWithoutIt()
    {
        const string old = "{\"databaseType\":\"PostgreSQL\",\"server\":\"s\",\"port\":5432,\"database\":\"d\",\"username\":\"u\",\"password\":\"\"}";

        var loaded = JsonSerializer.Deserialize<DatabaseConnectionData>(old)!.ToConnectionInfo();

        Assert.False(loaded.RequireEncryption);
    }
}
