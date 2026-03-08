using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests.E2E;

public class CrossDatabaseDataMigrationMatrixTests
{
    // Use a table without inbound FKs so each pair can be validated independently.
    private const string SampleTableName = "audit_log";

    public static IEnumerable<object[]> CrossDatabasePairs()
    {
        yield return new object[] { DatabaseType.SqlServer, DatabaseType.PostgreSQL };
        yield return new object[] { DatabaseType.SqlServer, DatabaseType.Oracle };
        yield return new object[] { DatabaseType.PostgreSQL, DatabaseType.SqlServer };
        yield return new object[] { DatabaseType.PostgreSQL, DatabaseType.Oracle };
        yield return new object[] { DatabaseType.Oracle, DatabaseType.SqlServer };
        yield return new object[] { DatabaseType.Oracle, DatabaseType.PostgreSQL };
    }

    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(CrossDatabasePairs))]
    public async Task CrossDatabaseMigration_SampleTable_RoundTripsRowCount(DatabaseType sourceType, DatabaseType targetType)
    {
        if (!ShouldRunE2E())
        {
            return;
        }

        var service = new DatabaseService();
        var source = BuildConnectionInfo(sourceType);
        var target = BuildConnectionInfo(targetType);

        Assert.True(await service.TestConnectionAsync(source), $"Source connection failed for {sourceType}");
        Assert.True(await service.TestConnectionAsync(target), $"Target connection failed for {targetType}");

        var sourceTable = await FindSampleTableAsync(service, source);
        Assert.NotNull(sourceTable);

        await service.MigrateTableAsync(source, target, sourceTable!, new Progress<int>());

        long sourceRowCount = await GetSampleTableRowCountAsync(service, source);
        long targetRowCount = await GetSampleTableRowCountAsync(service, target);

        Assert.Equal(sourceRowCount, targetRowCount);
    }

    private static bool ShouldRunE2E()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static ConnectionInfo BuildConnectionInfo(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.SqlServer => new ConnectionInfo
            {
                DatabaseType = DatabaseType.SqlServer,
                Server = "localhost",
                Port = 1433,
                Database = "TestDB",
                Username = "sa",
                Password = "SqlServer@123",
                TrustServerCertificate = true
            },
            DatabaseType.PostgreSQL => new ConnectionInfo
            {
                DatabaseType = DatabaseType.PostgreSQL,
                Server = "localhost",
                Port = 5432,
                Database = "testdb",
                Username = "pguser",
                Password = "pgpass123"
            },
            DatabaseType.Oracle => new ConnectionInfo
            {
                DatabaseType = DatabaseType.Oracle,
                Server = "localhost",
                Port = 1521,
                Database = "FREEPDB1",
                Username = "migration_test",
                Password = "oraclepass123"
            },
            _ => throw new NotSupportedException($"Unsupported database type {databaseType}")
        };
    }

    private static async Task<TableInfo?> FindSampleTableAsync(DatabaseService service, ConnectionInfo connectionInfo)
    {
        var tables = await service.GetTablesAsync(connectionInfo);
        string expectedSchema = connectionInfo.DatabaseType == DatabaseType.Oracle ? "MIGRATION_TEST" : "migration_test";

        return tables.FirstOrDefault(table =>
            string.Equals(table.TableName, SampleTableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(table.Schema, expectedSchema, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<long> GetSampleTableRowCountAsync(DatabaseService service, ConnectionInfo connectionInfo)
    {
        var sampleTable = await FindSampleTableAsync(service, connectionInfo);
        Assert.NotNull(sampleTable);
        return sampleTable!.RowCount;
    }
}
