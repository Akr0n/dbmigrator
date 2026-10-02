using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// PostgreSQL and Oracle keep text in any script. Migrated into SQL Server as <c>varchar</c>, the text was converted to the
/// database code page and everything outside it (Japanese, Cyrillic, Greek) arrived as '?', with no error. The columns are now
/// created as nvarchar/nchar: this runs the real schema + data migration into a database with a Latin code page.
/// </summary>
public class TextToSqlServerUnicodeE2ETests
{
    // Same port override as a local PostgreSQL service on 5432 needs; CI leaves it unset.
    private static readonly int PgPort =
        int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_PORT"), out var port) ? port : 5432;

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    // Built from code points: the source of this test never has to carry the non-Latin characters themselves.
    private static readonly string Japanese = new([(char)0x3042, (char)0x308A, (char)0x304C, (char)0x3068, (char)0x3046]);
    private static readonly string Cyrillic = new([(char)0x0416, (char)0x0443, (char)0x043A]);
    private static readonly string Greek = new([(char)0x03A9, (char)0x03BC, (char)0x03AD, (char)0x03B3, (char)0x03B1]);
    private static readonly string Mixed = $"{Japanese} {Cyrillic} {Greek} {(char)0x20AC}";
    private static readonly string Long = string.Concat(Enumerable.Repeat(Mixed + " ", 600)).TrimEnd(); // well over 4000 characters

    private static ConnectionInfo SqlServer(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    [Trait("Category", "E2E")]
    [Fact]
    public async Task PostgresText_ArrivesIntact_InADatabaseWithALatinCodePage()
    {
        if (!ShouldRunE2E()) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        string schema = $"uni_{id}";
        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Port = PgPort, Database = "testdb",
            Username = "pguser", Password = "pgpass123"
        };
        await using var pg = new NpgsqlConnection(source.GetConnectionString());
        await pg.OpenAsync();
        try
        {
            await ExecPostgresAsync(pg, $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.words (id integer PRIMARY KEY, v varchar(30), t text, c char(3));");
            await using (var insert = new NpgsqlCommand($"INSERT INTO {schema}.words VALUES (1, @v, @t, @c)", pg))
            {
                insert.Parameters.AddWithValue("v", Mixed);
                insert.Parameters.AddWithValue("t", Long);
                insert.Parameters.AddWithValue("c", Cyrillic);
                await insert.ExecuteNonQueryAsync();
            }

            await MigrateAndVerifyAsync(source, schema, "words", id);
        }
        finally
        {
            await ExecPostgresAsync(pg, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        }
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task OracleText_ArrivesIntact_InADatabaseWithALatinCodePage()
    {
        if (!ShouldRunE2E()) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        string table = $"UNI_{id}".ToUpperInvariant();
        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "migration_test", Password = "oraclepass123"
        };
        await using var oracle = new OracleConnection(source.GetConnectionString());
        await oracle.OpenAsync();
        try
        {
            // CHAR semantics: 3 characters, whatever their byte length in the database character set.
            await ExecOracleAsync(oracle, $"CREATE TABLE {table} (id NUMBER(10) PRIMARY KEY, v VARCHAR2(30 CHAR), t CLOB, c CHAR(3 CHAR))");
            await using (var insert = new OracleCommand($"INSERT INTO {table} VALUES (1, :v, :t, :c)", oracle))
            {
                insert.BindByName = true;
                insert.Parameters.Add("v", OracleDbType.NVarchar2).Value = Mixed;
                insert.Parameters.Add("t", OracleDbType.NClob).Value = Long;
                insert.Parameters.Add("c", OracleDbType.NChar).Value = Cyrillic;
                await insert.ExecuteNonQueryAsync();
            }

            await MigrateAndVerifyAsync(source, "MIGRATION_TEST", table, id);
        }
        finally
        {
            try { await ExecOracleAsync(oracle, $"DROP TABLE {table} PURGE"); } catch { /* best effort */ }
        }
    }

    /// <summary>Runs the schema and data migration into a fresh SQL Server database (code page 1252) and checks both.</summary>
    private static async Task MigrateAndVerifyAsync(ConnectionInfo source, string schema, string table, string id)
    {
        var master = SqlServer("master");
        var target = SqlServer($"unitxt_{id}");
        try
        {
            // SQL_Latin1_General_CP1_CI_AS is code page 1252: Japanese, Cyrillic and Greek are not in it.
            await ExecSqlServerAsync(master, $"CREATE DATABASE [{target.Database}] COLLATE SQL_Latin1_General_CP1_CI_AS");
            await ExecSqlServerAsync(target, $"CREATE SCHEMA [{schema}]"); // the migration does not create schemas
            var info = new TableInfo { Schema = schema, TableName = table };

            var created = new List<TableInfo>();
            await new SchemaMigrationService().MigrateSchemaAsync(source, target, [info], created);
            Assert.Single(created);
            await new DatabaseService().MigrateTableAsync(source, target, info, new Progress<int>());

            // The three text columns are Unicode types; PostgreSQL's lower-case names and Oracle's upper-case ones both land here.
            var types = await QuerySqlServerAsync(target,
                $"SELECT LOWER(COLUMN_NAME) + '=' + DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{schema}' " +
                $"AND TABLE_NAME = '{table}' AND LOWER(COLUMN_NAME) IN ('v','t','c') ORDER BY COLUMN_NAME");
            Assert.Equal(["c=nchar", "t=nvarchar", "v=nvarchar"], types);

            // The data: it used to be "????? ??? ?????? ?". CHAR is blank-padded by definition, and Oracle reports the size of a
            // multi-byte CHAR(n CHAR) in bytes (data_length), so the target column is wider and the value padded: compare it trimmed.
            var row = (await QuerySqlServerAsync(target, $"SELECT v + '|' + t + '|' + RTRIM(c) FROM [{schema}].[{table}]")).Single();
            Assert.Equal($"{Mixed}|{Long}|{Cyrillic}", row);
        }
        finally
        {
            SqlConnection.ClearAllPools(); // pooled connections would keep the database from being dropped
            try
            {
                await ExecSqlServerAsync(master, $"ALTER DATABASE [{target.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{target.Database}];");
            }
            catch
            {
                // Best effort: a leftover scratch database must not mask the real test result.
            }
        }
    }

    private static async Task ExecSqlServerAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> QuerySqlServerAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private static async Task ExecPostgresAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecOracleAsync(OracleConnection connection, string sql)
    {
        await using var command = new OracleCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
