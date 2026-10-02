using System.Text.RegularExpressions;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// A real migration stopped on a table of 199 columns: the batch of 1000 rows became one INSERT of about 200,000 values, and SQL
/// Server gave up compiling it after 44 seconds ("ran out of internal resources and could not produce a query plan", error 8623),
/// before a single row was loaded. The rows one INSERT holds now depend on the number of columns.
/// </summary>
public class WideTableInsertE2ETests
{
    private const int DataColumns = 199; // plus the identity key: 200 columns, like the table that failed
    private const int Rows = 1000;       // the default batch: all in one statement before the fix

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AWideTable_IsLoadedInStatementsTheServerCanCompile_AndEveryRowArrives()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        var rowsPerStatement = new List<int>();
        void Capture(LogEntry entry)
        {
            var match = Regex.Match(entry.Message, @"Batch INSERT executed, rows affected: (\d+)");
            if (match.Success)
                lock (rowsPerStatement)
                    rowsPerStatement.Add(int.Parse(match.Groups[1].Value));
        }

        LoggerService.MessageLogged += Capture;
        try
        {
            await new DatabaseService().MigrateTableAsync(db.Source, db.Target, db.Table, new Progress<int>());
        }
        finally
        {
            LoggerService.MessageLogged -= Capture;
        }

        Assert.Equal(Rows, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.wide"));
        Assert.Equal(await db.ScalarAsync(db.Source, "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.wide"),
            await db.ScalarAsync(db.Target, "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.wide"));
        // 200 values a row: a statement may hold 150 rows. One of 1000 rows is what the server could not compile.
        lock (rowsPerStatement)
        {
            Assert.Equal(Rows, rowsPerStatement.Sum());
            Assert.True(rowsPerStatement.Max() <= 150, $"the largest INSERT held {rowsPerStatement.Max()} rows");
        }
    }

    private static ConnectionInfo Fixture(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    /// <summary>A source and a target database with the same 200-column table, the source holding <see cref="Rows"/> rows.</summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private readonly ConnectionInfo _master = Fixture("master");

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; }
        public TableInfo Table { get; } = new() { Schema = "dbo", TableName = "wide" };

        private Scratch(ConnectionInfo source, ConnectionInfo target)
        {
            Source = source;
            Target = target;
        }

        public static async Task<Scratch> CreateAsync()
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var scratch = new Scratch(Fixture($"wide_src_{id}"), Fixture($"wide_tgt_{id}"));
            try
            {
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Source.Database}]");
                await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Target.Database}]");

                var names = Enumerable.Range(0, DataColumns).Select(i => $"c{i}").ToList();
                string create = $"CREATE TABLE dbo.wide (id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, {string.Join(", ", names.Select(n => $"{n} NVARCHAR(20) NULL"))})";
                await scratch.ExecAsync(scratch.Source, create);
                await scratch.ExecAsync(scratch.Target, create);

                // Short values, some of them NULL, as in a table with many optional columns.
                var values = names.Select((n, i) => i % 3 == 0 ? "NULL" : $"CONCAT('{n}-', i)");
                await scratch.ExecAsync(scratch.Source, $@"
                    ;WITH n AS (
                        SELECT TOP ({Rows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                        FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                    INSERT dbo.wide ({string.Join(", ", names)}) SELECT {string.Join(", ", values)} FROM n;");
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
            command.CommandTimeout = 300;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(ConnectionInfo database, string sql)
        {
            await using var connection = new SqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300;
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
