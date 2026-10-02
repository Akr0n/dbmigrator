using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// A migration into SQL Server wrote string values as plain '...' literals, which the server reads in its database code page:
/// every character outside it (Japanese, Cyrillic, Greek, many accented letters) arrived as '?', with no error.
/// </summary>
public class SqlServerUnicodeDataE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static ConnectionInfo Connection(string database) => new()
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = database,
        Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
    };

    private static async Task ExecAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(Connection(database).GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> TextAsync(string database, int id)
    {
        await using var connection = new SqlConnection(Connection(database).GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT v FROM dbo.words WHERE id = {id}";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task NonLatinText_ArrivesIntactInANvarcharColumn_OfADatabaseWithALatinCodePage()
    {
        if (!ShouldRunE2E()) return;

        // Hiragana "arigatou", Cyrillic "Zhuk" (a beetle), Greek "Omega", the euro sign, and a plain ASCII value with a quote.
        string japanese = new(new[] { (char)0x3042, (char)0x308A, (char)0x304C, (char)0x3068, (char)0x3046 });
        string cyrillic = new(new[] { (char)0x0416, (char)0x0443, (char)0x043A });
        string greek = new(new[] { (char)0x03A9, (char)0x03BC, (char)0x03AD, (char)0x03B3, (char)0x03B1 });
        string euro = ((char)0x20AC).ToString();
        string mixed = $"{japanese} {cyrillic} {greek} {euro}";

        string id = Guid.NewGuid().ToString("N")[..8];
        string source = $"uni_src_{id}", target = $"uni_tgt_{id}";
        try
        {
            // The default collation of the fixture container is SQL_Latin1_General_CP1_CI_AS: code page 1252.
            await ExecAsync("master", $"CREATE DATABASE [{source}] COLLATE SQL_Latin1_General_CP1_CI_AS");
            await ExecAsync("master", $"CREATE DATABASE [{target}] COLLATE SQL_Latin1_General_CP1_CI_AS");
            const string table = "CREATE TABLE dbo.words (id INT NOT NULL PRIMARY KEY, v NVARCHAR(100) NOT NULL);";
            await ExecAsync(source, table);
            await ExecAsync(target, table);
            await using (var connection = new SqlConnection(Connection(source).GetConnectionString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT dbo.words VALUES (1, @mixed), (2, @plain)";
                command.Parameters.AddWithValue("@mixed", mixed);
                command.Parameters.AddWithValue("@plain", "it's plain");
                await command.ExecuteNonQueryAsync();
            }

            await new DatabaseService().MigrateTableAsync(Connection(source), Connection(target),
                new TableInfo { Schema = "dbo", TableName = "words" }, new Progress<int>());

            Assert.Equal(mixed, await TextAsync(target, 1));      // it used to arrive as "????? ??? ..."
            Assert.Equal("it's plain", await TextAsync(target, 2));
        }
        finally
        {
            SqlConnection.ClearAllPools(); // pooled connections would keep the databases from being dropped
            foreach (var database in new[] { source, target })
            {
                try
                {
                    await ExecAsync("master", $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
                }
                catch
                {
                    // Best effort: a leftover scratch database must not mask the real test result.
                }
            }
        }
    }
}
