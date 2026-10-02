using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// An unnamed UNIQUE gets a generated name (UQ__table__&lt;hash&gt;) that differs in every database. Matching the source's
/// name against a pre-existing target table therefore never finds the equivalent constraint, and a duplicate UNIQUE
/// (a second index on the same columns) was added. PostgreSQL and Oracle already match by column list.
/// </summary>
public class SqlServerExistingUniqueConstraintE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private const string Columns = "id INT NOT NULL PRIMARY KEY, code VARCHAR(20) NOT NULL, kind VARCHAR(20) NOT NULL";

    [Trait("Category", "E2E")]
    [Fact]
    public async Task MigrateSchema_TargetAlreadyHasUniqueOnTheSameColumnsUnderAnotherName_AddsNoDuplicate()
    {
        if (!ShouldRunE2E()) return;

        // (kind, code) is deliberately NOT alphabetical: the source catalog lists columns by name, the target keeps key order.
        await WithScratchDatabasesAsync(
            sourceDdl: $"CREATE TABLE dbo.t ({Columns}, UNIQUE (kind, code));",
            // A leading dummy table shifts the object ids, so the generated UNIQUE name differs from the source's.
            targetDdl: $"CREATE TABLE dbo.padding (id INT); CREATE TABLE dbo.t ({Columns}, UNIQUE (kind, code));",
            async (source, target) =>
            {
                const string uniqueName = "SELECT name FROM sys.key_constraints WHERE type = 'UQ'";
                Assert.NotEqual(await StringAsync(source, uniqueName), await StringAsync(target, uniqueName));

                var result = await new SchemaMigrationService().MigrateSchemaAsync(
                    source, target, [new TableInfo { Schema = "dbo", TableName = "t" }], new List<TableInfo>());

                Assert.Empty(result.ConstraintsAdded);
                Assert.Equal("1", await StringAsync(target, "SELECT COUNT(*) FROM sys.key_constraints WHERE type = 'UQ'"));
            });
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task MigrateSchema_TargetUniqueOnDifferentColumns_StillAddsTheSourceUnique()
    {
        if (!ShouldRunE2E()) return;

        // Guards the de-duplication against matching too much: UNIQUE(code) does not enforce what UNIQUE(code, kind) does.
        await WithScratchDatabasesAsync(
            sourceDdl: $"CREATE TABLE dbo.t ({Columns}, UNIQUE (code, kind));",
            targetDdl: $"CREATE TABLE dbo.t ({Columns}, UNIQUE (code));",
            async (source, target) =>
            {
                var result = await new SchemaMigrationService().MigrateSchemaAsync(
                    source, target, [new TableInfo { Schema = "dbo", TableName = "t" }], new List<TableInfo>());

                Assert.Single(result.ConstraintsAdded);
                Assert.Equal("UNIQUE", result.ConstraintsAdded[0].ConstraintType);
                Assert.Equal("2", await StringAsync(target, "SELECT COUNT(*) FROM sys.key_constraints WHERE type = 'UQ'"));
            });
    }

    private static async Task WithScratchDatabasesAsync(string sourceDdl, string targetDdl,
        Func<ConnectionInfo, ConnectionInfo, Task> body)
    {
        string id = Guid.NewGuid().ToString("N")[..8];
        var master = Connection("master");
        var source = Connection($"uqchk_src_{id}");
        var target = Connection($"uqchk_tgt_{id}");
        try
        {
            await ExecAsync(master, $"CREATE DATABASE [{source.Database}]; CREATE DATABASE [{target.Database}];");
            await ExecAsync(source, sourceDdl);
            await ExecAsync(target, targetDdl);
            await body(source, target);
        }
        finally
        {
            SqlConnection.ClearAllPools(); // pooled connections would keep the databases from being dropped
            foreach (var database in new[] { source.Database, target.Database })
            {
                try
                {
                    await ExecAsync(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
                }
                catch
                {
                    // Best effort: a leftover scratch database must not mask the real test result.
                }
            }
        }
    }

    private static ConnectionInfo Connection(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    private static async Task ExecAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> StringAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
