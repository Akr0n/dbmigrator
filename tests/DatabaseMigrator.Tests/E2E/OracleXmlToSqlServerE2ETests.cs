using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// Oracle hands an XMLTYPE over as text that starts with its XML declaration. Written as an N'...' literal into a SQL Server xml
/// column, a declaration that names an encoding (UTF-8, the usual case for XML loaded from files) made the server refuse the
/// whole load: "XML parsing: unable to switch the encoding".
/// </summary>
public class OracleXmlToSqlServerE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static ConnectionInfo SqlServer(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    private static async Task<string?> ScalarAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task ExecAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new SqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AnXmlTypeWithAnEncodingDeclaration_IsLoadedIntoAnXmlColumn()
    {
        if (!ShouldRunE2E()) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        string table = $"XML_{id}".ToUpperInvariant();
        // Cyrillic "Zhuk" built from code points, so that the declaration is not the only non-ASCII-sensitive part of the test.
        string beetle = new([(char)0x0416, (char)0x0443, (char)0x043A]);
        var source = new ConnectionInfo
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "migration_test", Password = "oraclepass123"
        };
        var master = SqlServer("master");
        var target = SqlServer($"xmlld_{id}");

        await using var oracle = new OracleConnection(source.GetConnectionString());
        await oracle.OpenAsync();
        try
        {
            await using (var create = new OracleCommand($"CREATE TABLE {table} (id NUMBER(10) PRIMARY KEY, doc XMLTYPE)", oracle))
                await create.ExecuteNonQueryAsync();
            var documents = new Dictionary<int, string>
            {
                [1] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a>2</a>",
                [2] = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><a>{beetle}</a>",
                [3] = "<a>no declaration</a>",
            };
            foreach (var (key, document) in documents)
            {
                await using var insert = new OracleCommand($"INSERT INTO {table} VALUES ({key}, XMLTYPE(:x))", oracle) { BindByName = true };
                insert.Parameters.Add("x", OracleDbType.Varchar2).Value = document; // not NVarchar2: Oracle too rejects a UTF-8 declaration on UTF-16 text
                await insert.ExecuteNonQueryAsync();
            }

            await ExecAsync(master, $"CREATE DATABASE [{target.Database}] COLLATE SQL_Latin1_General_CP1_CI_AS");
            await ExecAsync(target, "CREATE SCHEMA [MIGRATION_TEST]"); // the migration does not create schemas
            var info = new TableInfo { Schema = "MIGRATION_TEST", TableName = table };
            await new SchemaMigrationService().MigrateSchemaAsync(source, target, [info], new List<TableInfo>());
            Assert.Equal("xml", await ScalarAsync(target,
                $"SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND LOWER(COLUMN_NAME) = 'doc'"));

            await new DatabaseService().MigrateTableAsync(source, target, info, new Progress<int>()); // it used to throw "unable to switch the encoding"

            Assert.Equal("3", await ScalarAsync(target, $"SELECT COUNT(*) FROM [MIGRATION_TEST].[{table}]"));
            Assert.Equal("2", await ScalarAsync(target, $"SELECT doc.value('(/a)[1]', 'nvarchar(50)') FROM [MIGRATION_TEST].[{table}] WHERE id = 1"));
            Assert.Equal(beetle, await ScalarAsync(target, $"SELECT doc.value('(/a)[1]', 'nvarchar(50)') FROM [MIGRATION_TEST].[{table}] WHERE id = 2"));
            Assert.Equal("no declaration", await ScalarAsync(target, $"SELECT doc.value('(/a)[1]', 'nvarchar(50)') FROM [MIGRATION_TEST].[{table}] WHERE id = 3"));
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
