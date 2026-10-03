using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// On SQL Server a TRUNCATE of a table that a FOREIGN KEY references fails with error 4712, and that error ends the open transaction
/// (@@TRANCOUNT goes from 1 to 0). The migration then emptied the table with DELETE, but the driver quietly ignores a transaction that
/// has ended, so the DELETE and every INSERT ran one by one in autocommit: a table that failed half way kept what had been loaded and
/// lost what it held before, and the final COMMIT could only report that the transaction "has completed".
/// </summary>
public class ReferencedTableRollbackE2ETests
{
    private const int Rows = 2500; // the default batch is 1000: two batches go in before the one that fails

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AReferencedTable_ThatFailsHalfWay_IsRolledBackLikeAnyOther()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var error = await Record.ExceptionAsync(() =>
            new DatabaseService().MigrateTableAsync(db.Source, db.Target, db.Table, new Progress<int>()));

        // Row 2400 has a NULL the target does not accept: the third batch fails (error 515).
        Assert.NotNull(error);
        Assert.Equal(5, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.parent"));        // what it held before, untouched
        Assert.Equal(5, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.parent WHERE v = N'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AReferencedTable_IsEmptiedAndLoadedInsideOneTransaction_AndCommitted()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(failingRow: false);

        await new DatabaseService().MigrateTableAsync(db.Source, db.Target, db.Table, new Progress<int>());

        Assert.Equal(Rows, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.parent"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.parent WHERE v = N'old'"));
    }

    private static ConnectionInfo Fixture(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    /// <summary>
    /// A source with a table of 2500 rows and a target with the same table already holding 5 rows, referenced by an (empty) child table.
    /// </summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private readonly ConnectionInfo _master = Fixture("master");

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; }
        public TableInfo Table { get; } = new() { Schema = "dbo", TableName = "parent" };

        private Scratch(ConnectionInfo source, ConnectionInfo target)
        {
            Source = source;
            Target = target;
        }

        public static async Task<Scratch> CreateAsync(bool failingRow = true)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var scratch = new Scratch(Fixture($"refrb_src_{id}"), Fixture($"refrb_tgt_{id}"));
            try
            {
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Source.Database}]");
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Target.Database}]");
                await scratch.ExecAsync(scratch.Source, "CREATE TABLE dbo.parent (id INT NOT NULL PRIMARY KEY, v NVARCHAR(20) NULL)");
                await scratch.ExecAsync(scratch.Source, $@"
                    ;WITH n AS (
                        SELECT TOP ({Rows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                        FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                    INSERT dbo.parent (id, v) SELECT i, CASE WHEN i = 2400 AND {(failingRow ? 1 : 0)} = 1 THEN NULL ELSE CONCAT('new', i) END FROM n;");
                await scratch.ExecAsync(scratch.Target, @"
                    CREATE TABLE dbo.parent (id INT NOT NULL PRIMARY KEY, v NVARCHAR(20) NOT NULL);
                    CREATE TABLE dbo.child (id INT NOT NULL PRIMARY KEY, parent_id INT NOT NULL REFERENCES dbo.parent(id));
                    INSERT dbo.parent (id, v) VALUES (100001, N'old'), (100002, N'old'), (100003, N'old'), (100004, N'old'), (100005, N'old');");
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
