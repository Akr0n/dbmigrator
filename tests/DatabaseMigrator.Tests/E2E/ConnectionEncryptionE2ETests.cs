using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// PostgreSQL and Oracle connections used to be opened in whatever mode the driver defaults to: TLS when the server offers
/// it, plaintext when it does not, and no check of the server certificate either way. With "require encryption" they must
/// either be encrypted (and verified) or fail: a silent plaintext fallback is the case that matters.
/// </summary>
public class ConnectionEncryptionE2ETests
{
    // Same port override as a local PostgreSQL service on 5432 needs; CI leaves it unset.
    private static readonly int PgPort =
        int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_PORT"), out var port) ? port : 5432;

    // A PostgreSQL with ssl=on and a self-signed certificate. Not part of the CI containers: the tests that need it run only
    // when this is set (scripts: see PODMAN_E2E_TESTING.md).
    private static readonly int? PgTlsPort =
        int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_TLS_PORT"), out var tlsPort) ? tlsPort : null;

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static ConnectionInfo Postgres(int port, bool requireEncryption = false, bool trust = false) => new()
    {
        DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Port = port, Database = "testdb",
        Username = "pguser", Password = "pgpass123", RequireEncryption = requireEncryption, TrustServerCertificate = trust
    };

    private static ConnectionInfo Oracle(bool requireEncryption = false) => new()
    {
        DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
        Username = "migration_test", Password = "oraclepass123", RequireEncryption = requireEncryption
    };

    private static Task<bool> CanConnectAsync(ConnectionInfo connection) => new DatabaseService().TestConnectionAsync(connection);

    [Trait("Category", "E2E")]
    [Fact]
    public async Task APostgresServerWithoutTls_RefusesAConnectionThatRequiresEncryption()
    {
        if (!ShouldRunE2E()) return;

        Assert.True(await CanConnectAsync(Postgres(PgPort)));                                           // the default still works
        Assert.False(await CanConnectAsync(Postgres(PgPort, requireEncryption: true)));                 // no silent plaintext
        Assert.False(await CanConnectAsync(Postgres(PgPort, requireEncryption: true, trust: true)));    // not even when trusting
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AnOracleListenerWithoutTls_RefusesAConnectionThatRequiresEncryption()
    {
        if (!ShouldRunE2E()) return;

        Assert.True(await CanConnectAsync(Oracle()));
        Assert.False(await CanConnectAsync(Oracle(requireEncryption: true)));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task APostgresServerWithASelfSignedCertificate_IsAcceptedOnlyWhenTheCertificateIsTrusted()
    {
        if (!ShouldRunE2E() || PgTlsPort is not { } tls) return;

        Assert.True(await CanConnectAsync(Postgres(tls)));                                       // default: encrypted, nothing verified
        Assert.False(await CanConnectAsync(Postgres(tls, requireEncryption: true)));             // verified: a self-signed one is refused
        Assert.True(await CanConnectAsync(Postgres(tls, requireEncryption: true, trust: true))); // encrypted, certificate accepted
    }
}
