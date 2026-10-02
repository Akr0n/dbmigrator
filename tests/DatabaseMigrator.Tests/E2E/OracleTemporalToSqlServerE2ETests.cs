using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// Oracle reports TIMESTAMP(6) and TIMESTAMP(6) WITH TIME ZONE with the precision inside the type name, which the mapping did not
/// recognise: such columns were created on SQL Server as text (varchar) instead of datetime2 / datetimeoffset.
/// </summary>
public class OracleTemporalToSqlServerE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static ConnectionInfo SqlServer(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    private static async Task ExecAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    // Same port override as a local PostgreSQL service on 5432 needs; CI leaves it unset.
    private static readonly int PgPort =
        int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_PORT"), out var port) ? port : 5432;

    [Trait("Category", "E2E")]
    [Fact]
    public async Task OracleIntervals_StillLoadIntoPostgres_AsText()
    {
        if (!ShouldRunE2E()) return;

        // Mapping an interval to a PostgreSQL interval column made the load fail: the driver hands the value over as a month count
        // (YEAR TO MONTH) or as a TimeSpan whose negative form is not valid interval syntax. As text they load, as they always did.
        string id = Guid.NewGuid().ToString("N")[..8];
        string table = $"IVL_{id}".ToUpperInvariant();
        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "migration_test", Password = "oraclepass123"
        };
        var target = new ConnectionInfo
        {
            DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Port = PgPort, Database = "testdb",
            Username = "pguser", Password = "pgpass123"
        };

        await using var oracle = new OracleConnection(source.GetConnectionString());
        await oracle.OpenAsync();
        await using var pg = new Npgsql.NpgsqlConnection(target.GetConnectionString());
        await pg.OpenAsync();
        async Task Pg(string sql)
        {
            await using var command = new Npgsql.NpgsqlCommand(sql, pg);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            foreach (var statement in new[]
            {
                $"CREATE TABLE {table} (id NUMBER(10) PRIMARY KEY, iym INTERVAL YEAR(2) TO MONTH, ids INTERVAL DAY(2) TO SECOND(6))",
                $"INSERT INTO {table} VALUES (1, INTERVAL '1-2' YEAR TO MONTH, INTERVAL '3 04:05:06.5' DAY TO SECOND)",
                $"INSERT INTO {table} VALUES (2, INTERVAL '-2-3' YEAR TO MONTH, INTERVAL '-2 03:04:05' DAY TO SECOND)",
                "COMMIT"
            })
            {
                await using var command = new OracleCommand(statement, oracle);
                await command.ExecuteNonQueryAsync();
            }

            await Pg("CREATE SCHEMA IF NOT EXISTS migration_test");
            var info = new TableInfo { Schema = "MIGRATION_TEST", TableName = table };
            await new SchemaMigrationService().MigrateSchemaAsync(source, target, [info], new List<TableInfo>());
            await new DatabaseService().MigrateTableAsync(source, target, info, new Progress<int>()); // it used to throw 42804 / 22007

            await using var count = new Npgsql.NpgsqlCommand($"SELECT COUNT(*) FROM migration_test.{table.ToLowerInvariant()}", pg);
            Assert.Equal(2L, await count.ExecuteScalarAsync());
            await using var types = new Npgsql.NpgsqlCommand(
                $"SELECT string_agg(data_type, ',' ORDER BY column_name) FROM information_schema.columns " +
                $"WHERE table_schema = 'migration_test' AND table_name = '{table.ToLowerInvariant()}' AND column_name IN ('iym','ids')", pg);
            Assert.Equal("text,text", await types.ExecuteScalarAsync());
        }
        finally
        {
            try { await Pg($"DROP TABLE IF EXISTS migration_test.{table.ToLowerInvariant()}"); } catch { /* best effort */ }
            try
            {
                await using var drop = new OracleCommand($"DROP TABLE {table} PURGE", oracle);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best effort: a leftover scratch table must not mask the real test result.
            }
        }
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task OracleTimestamps_ArriveAsTemporalColumns_WithTheirValues()
    {
        if (!ShouldRunE2E()) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        string table = $"TMP_{id}".ToUpperInvariant();
        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "migration_test", Password = "oraclepass123"
        };
        var master = SqlServer("master");
        var target = SqlServer($"tmpld_{id}");

        await using var oracle = new OracleConnection(source.GetConnectionString());
        await oracle.OpenAsync();
        try
        {
            foreach (var statement in new[]
            {
                $"CREATE TABLE {table} (id NUMBER(10) PRIMARY KEY, ts TIMESTAMP(6), tstz TIMESTAMP(6) WITH TIME ZONE)",
                $"INSERT INTO {table} VALUES (1, TIMESTAMP '2024-05-06 07:08:09.123456', TIMESTAMP '2024-05-06 07:08:09.123456 +00:00')",
                "COMMIT"
            })
            {
                await using var command = new OracleCommand(statement, oracle);
                await command.ExecuteNonQueryAsync();
            }

            await ExecAsync(master, $"CREATE DATABASE [{target.Database}]");
            await ExecAsync(target, "CREATE SCHEMA [MIGRATION_TEST]"); // the migration does not create schemas
            var info = new TableInfo { Schema = "MIGRATION_TEST", TableName = table };
            await new SchemaMigrationService().MigrateSchemaAsync(source, target, [info], new List<TableInfo>());
            await new DatabaseService().MigrateTableAsync(source, target, info, new Progress<int>());

            await using var connection = new SqlConnection(target.GetConnectionString());
            await connection.OpenAsync();
            await using (var types = new SqlCommand(
                $"SELECT LOWER(COLUMN_NAME) + '=' + DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' " +
                "AND LOWER(COLUMN_NAME) IN ('ts','tstz') ORDER BY COLUMN_NAME", connection))
            await using (var reader = await types.ExecuteReaderAsync())
            {
                var actual = new List<string>();
                while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
                Assert.Equal(["ts=datetime2", "tstz=datetimeoffset"], actual); // they used to be varchar
            }

            await using var values = new SqlCommand($"SELECT ts, tstz FROM [MIGRATION_TEST].[{table}]", connection);
            await using var data = await values.ExecuteReaderAsync();
            Assert.True(await data.ReadAsync());
            Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, 123), data.GetDateTime(0)); // the load writes milliseconds
            Assert.Equal(new DateTime(2024, 5, 6), data.GetDateTimeOffset(1).UtcDateTime.Date);
        }
        finally
        {
            try
            {
                await using var drop = new OracleCommand($"DROP TABLE {table} PURGE", oracle);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best effort: a leftover scratch table must not mask the real test result.
            }

            SqlConnection.ClearAllPools(); // pooled connections would keep the database from being dropped
            try
            {
                await ExecAsync(master, $"ALTER DATABASE [{target.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{target.Database}];");
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
