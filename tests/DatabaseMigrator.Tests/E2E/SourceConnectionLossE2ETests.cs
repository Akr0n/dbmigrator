using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// A real migration (SQL Server to SQL Server) lost the connection to the source in the middle of a table of millions of rows,
/// twice (after 8 and after 21 minutes), and every time the whole table was rolled back. Here the source connection is cut with
/// a reset, through a relay, while the table is being read, and the read has to pick up from the row after the last one received.
/// </summary>
public class SourceConnectionLossE2ETests
{
    private const int Rows = 12_000;
    private const long CutAfterBytes = 700_000; // a rows is about 170 bytes on the wire: the first cut falls near row 4,000

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static readonly Func<ResumePolicy> QuickPolicy =
        () => new ResumePolicy(3, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50));

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AConnectionCutInTheMiddleOfATable_IsResumed_AndEveryRowArrivesExactlyOnce()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(sourceHasPrimaryKey: true);
        using var proxy = new TcpDropProxy("127.0.0.1", 1433, CutAfterBytes);

        var log = await MigrateCapturingLogAsync(db, proxy);

        Assert.Equal(1, proxy.Drops); // the cut really happened: without it this test proves nothing
        await AssertSameRowsAsync(db);
        Assert.Contains(log, line => line.Contains("Resumed reading", StringComparison.Ordinal));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AConnectionCutTwice_IsResumedTwice_FromWhereTheSecondRunStopped()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(sourceHasPrimaryKey: true);
        using var proxy = new TcpDropProxy("127.0.0.1", 1433, CutAfterBytes, maxDrops: 2);

        await MigrateCapturingLogAsync(db, proxy);

        Assert.Equal(2, proxy.Drops);
        await AssertSameRowsAsync(db);
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableWithoutAPrimaryKey_CannotBeResumed_SoTheLossFailsTheTableAndNothingIsLeftBehind()
    {
        if (!ShouldRunE2E()) return;
        // Without a key there is no order to come back to: reading again could skip or repeat rows, so the table fails as before.
        await using var db = await Scratch.CreateAsync(sourceHasPrimaryKey: false);
        using var proxy = new TcpDropProxy("127.0.0.1", 1433, CutAfterBytes);

        var ex = await Record.ExceptionAsync(() => MigrateCapturingLogAsync(db, proxy));

        Assert.NotNull(ex);
        Assert.Equal(1, proxy.Drops);
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.big_t")); // rolled back
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AConnectionThatCannotBeOpenedAgain_IsGivenUp_WithTheTableNamedAndNothingLeftBehind()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(sourceHasPrimaryKey: true);
        using var proxy = new TcpDropProxy("127.0.0.1", 1433, CutAfterBytes, refuseAfterDrop: true); // the network stays down

        var ex = await Record.ExceptionAsync(() => MigrateCapturingLogAsync(db, proxy));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("dbo.big_t", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.big_t"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Migrates the table with the source reached through the relay; returns what the migration logged.</summary>
    private static async Task<List<string>> MigrateCapturingLogAsync(Scratch db, TcpDropProxy proxy)
    {
        var log = new List<string>();
        void Capture(LogEntry entry)
        {
            lock (log)
                log.Add(entry.Message);
        }

        LoggerService.MessageLogged += Capture;
        try
        {
            await new DatabaseService { NewResumePolicy = QuickPolicy }
                .MigrateTableAsync(db.SourceThrough(proxy.Port), db.Target, db.Table, new Progress<int>());
        }
        finally
        {
            LoggerService.MessageLogged -= Capture;
        }

        lock (log)
            return log.ToList();
    }

    private static async Task AssertSameRowsAsync(Scratch db)
    {
        Assert.Equal(Rows, await db.ScalarAsync(db.Source, "SELECT COUNT(*) FROM dbo.big_t"));
        Assert.Equal(Rows, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.big_t"));
        // The same ids and the same text in every row: a row read twice would have been rejected by the target's key, and one
        // skipped would change the count and the checksum.
        Assert.Equal(await db.ScalarAsync(db.Source, "SELECT CHECKSUM_AGG(CHECKSUM(id, payload)) FROM dbo.big_t"),
            await db.ScalarAsync(db.Target, "SELECT CHECKSUM_AGG(CHECKSUM(id, payload)) FROM dbo.big_t"));
    }

    private static ConnectionInfo Fixture(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    /// <summary>A source and a target database holding the same table (identity key), the source with <see cref="Rows"/> rows.</summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private readonly ConnectionInfo _master = Fixture("master");

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; }
        public TableInfo Table { get; } = new() { Schema = "dbo", TableName = "big_t" };

        private Scratch(ConnectionInfo source, ConnectionInfo target)
        {
            Source = source;
            Target = target;
        }

        public ConnectionInfo SourceThrough(int port)
        {
            var info = Fixture(Source.Database);
            info.Port = port;
            return info;
        }

        public static async Task<Scratch> CreateAsync(bool sourceHasPrimaryKey)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var scratch = new Scratch(Fixture($"srcloss_src_{id}"), Fixture($"srcloss_tgt_{id}"));
            try
            {
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Source.Database}]");
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Target.Database}]");
                await scratch.ExecAsync(scratch.Source,
                    $"CREATE TABLE dbo.big_t (id INT IDENTITY(1,1) NOT NULL {(sourceHasPrimaryKey ? "PRIMARY KEY" : "")}, payload NVARCHAR(100) NOT NULL)");
                await scratch.ExecAsync(scratch.Target,
                    "CREATE TABLE dbo.big_t (id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, payload NVARCHAR(100) NOT NULL)");
                await scratch.ExecAsync(scratch.Source, $@"
                    ;WITH n AS (
                        SELECT TOP ({Rows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                        FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                    INSERT dbo.big_t (payload) SELECT CONCAT('row-', i, '-', REPLICATE('x', 70)) FROM n;");
                return scratch;
            }
            catch
            {
                await scratch.DisposeAsync(); // `await using` never sees a Scratch that was not returned
                throw;
            }
        }

        public async Task ExecAsync(ConnectionInfo database, string sql)
        {
            await using var connection = new SqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(ConnectionInfo database, string sql)
        {
            await using var connection = new SqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools(); // pooled connections would keep the databases from being dropped
            foreach (var name in new[] { Source.Database, Target.Database })
            {
                try
                {
                    await ExecAsync(_master,
                        $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
                }
                catch
                {
                    // Best effort: a leftover scratch database must not mask the real test result.
                }
            }
        }
    }
}
