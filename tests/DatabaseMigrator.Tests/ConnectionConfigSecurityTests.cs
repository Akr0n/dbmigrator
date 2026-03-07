using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Tests;

public class ConnectionConfigSecurityTests
{
    [Fact]
    public void DatabaseConnectionData_RoundTripsPasswordAndSecurityFlags()
    {
        if (!OperatingSystem.IsWindows() && !RuntimeOptionsProvider.Current.Security.AllowPlaintextConfigFallback)
        {
            // On non-Windows, DPAPI is unavailable and plaintext fallback may be intentionally disabled.
            return;
        }

        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = "localhost",
            Port = 1433,
            Database = "TestDB",
            Username = "sa",
            Password = "Sup3rSecure!",
            TrustServerCertificate = true
        };

        var serialized = DatabaseConnectionData.FromConnectionInfo(source);
        var roundTrip = serialized.ToConnectionInfo();

        Assert.Equal(source.DatabaseType, roundTrip.DatabaseType);
        Assert.Equal(source.Server, roundTrip.Server);
        Assert.Equal(source.Port, roundTrip.Port);
        Assert.Equal(source.Database, roundTrip.Database);
        Assert.Equal(source.Username, roundTrip.Username);
        Assert.Equal(source.Password, roundTrip.Password);
        Assert.Equal(source.TrustServerCertificate, roundTrip.TrustServerCertificate);

        if (OperatingSystem.IsWindows())
        {
            Assert.True(serialized.PasswordProtected);
            Assert.NotEqual(source.Password, serialized.Password);
        }
    }
}
