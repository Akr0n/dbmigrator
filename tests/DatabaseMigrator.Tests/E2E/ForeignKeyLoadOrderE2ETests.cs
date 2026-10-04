using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// A real migration into a target that already has its FOREIGN KEYs (tables created by the application, not by
/// this tool) failed with "INSERT conflicted with FOREIGN KEY constraint": the data phase walked the tables
/// alphabetically, so a child was loaded before its parent.
/// </summary>
public class ForeignKeyLoadOrderE2ETests
{
    private const int SelfReferencingRows = 2500; // > DBMIGRATOR_BATCH_SIZE default (1000): parents land in later batches

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    // ── Scenario: SQL Server → SQL Server, target pre-created with FOREIGN KEYs ─────────────────────────

    [Trait("Category", "E2E")]
    [Fact]
    public async Task NaiveAlphabeticalLoad_ChildBeforeParent_IsRejectedByTheTargetForeignKey()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var ex = await Record.ExceptionAsync(() =>
            new DatabaseService().MigrateTableAsync(db.Source, db.Target, db.Table("a_item"), new Progress<int>()));

        // Guards the other tests against being vacuous: the target really does enforce the FOREIGN KEY.
        Assert.Equal(547, FindSqlException(ex)?.Number);
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task NaiveLoad_SelfReferencingRowsSpanningBatches_IsRejectedByTheTargetForeignKey()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var ex = await Record.ExceptionAsync(() =>
            new DatabaseService().MigrateTableAsync(db.Source, db.Target, db.Table("m_node"), new Progress<int>()));

        Assert.Equal(547, FindSqlException(ex)?.Number);
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_LoadsEveryTable_AndLeavesForeignKeysEnabledAndTrusted()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var warnings = await LoadAllAsync(db);

        Assert.Empty(warnings);
        await AssertRowCountsMatchAsync(db);
        Assert.Equal(0, await db.ScalarAsync(db.Target,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_ParentComesBeforeChild_AndForeignKeysAreSwitchedOffForTheLoad()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var plan = await new ForeignKeyService().PrepareDataLoadAsync(db.Target, db.AlphabeticalTables);
        try
        {
            var order = plan.OrderedTables.Select(t => t.TableName).ToList();
            Assert.True(order.IndexOf("z_head") < order.IndexOf("a_item"));
            Assert.Equal(2, plan.DisabledForeignKeyCount);
            Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1"));
        }
        finally
        {
            await plan.CompleteAsync(false);
        }
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_CanBeRerunOverAnAlreadyPopulatedTarget()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        await LoadAllAsync(db);
        var warnings = await LoadAllAsync(db); // TRUNCATE is refused on referenced tables: needs the DELETE fallback

        Assert.Empty(warnings);
        await AssertRowCountsMatchAsync(db);
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_WhenTheLoadFails_StillSwitchesForeignKeysBackOn()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        var plan = await new ForeignKeyService().PrepareDataLoadAsync(db.Target, db.AlphabeticalTables);
        await plan.CompleteAsync(false);

        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DeleteFallback_DoesNotCascadeIntoATableTheUserDidNotSelect()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();

        // Only z_head is selected. An unselected table keeps an ENABLED key to it with ON DELETE CASCADE, and holds rows.
        await db.ExecAsync(db.Target, @"
            INSERT dbo.z_head (id, name) VALUES (1, 'old1'), (2, 'old2');
            CREATE TABLE dbo.child_x (id INT NOT NULL PRIMARY KEY, head_id INT NOT NULL,
                CONSTRAINT FK_child_x_z_head FOREIGN KEY (head_id) REFERENCES dbo.z_head(id) ON DELETE CASCADE);
            INSERT dbo.child_x (id, head_id) VALUES (1, 1), (2, 2);");

        var handlerCalls = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { handlerCalls++; return Task.FromResult(false); } };
        var selected = new[] { db.Table("z_head") };

        var plan = await new ForeignKeyService().PrepareDataLoadAsync(db.Target, selected);
        try
        {
            // TRUNCATE is refused; a DELETE would silently wipe child_x. The user must be asked (and declines here).
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.MigrateTableAsync(db.Source, db.Target, selected[0], new Progress<int>()));
        }
        finally
        {
            await plan.CompleteAsync(false);
        }

        Assert.Equal(1, handlerCalls);
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.child_x"));
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.z_head"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_LeavesAForeignKeyThatWasAlreadyDisabledDisabled()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, "ALTER TABLE dbo.a_item NOCHECK CONSTRAINT FK_a_item_z_head");

        var warnings = await LoadAllAsync(db);

        Assert.Empty(warnings);
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1"));
        Assert.Equal(1, await db.ScalarAsync(db.Target,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_a_item_z_head' AND is_disabled = 1"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task DataLoadPlan_WhenTheSourceHoldsOrphanRows_WarnsAndStillSwitchesTheKeyBackOn()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Source, "INSERT dbo.a_item (id, head_id, name) VALUES (6, 99, 'orphan')"); // no z_head 99

        var warnings = await LoadAllAsync(db);

        Assert.Contains(warnings, w => w.Contains("FK_a_item_z_head"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1"));
        Assert.Equal(6, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM dbo.a_item")); // data is copied faithfully
    }

    // ── Catalog reading on every dialect, against the shared fixtures ───────────────────────────────────

    [Trait("Category", "E2E")]
    [Theory]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.Oracle)]
    public async Task GetForeignKeys_ReadsTheFixtureForeignKeys(DatabaseType type)
    {
        if (!ShouldRunE2E()) return;

        var connection = FixtureConnection(type);
        var selected = (await new DatabaseService().GetTablesAsync(connection))
            .Where(t => string.Equals(t.Schema, "migration_test", StringComparison.OrdinalIgnoreCase))
            .Where(t => new[] { "fk_grandparent", "fk_parent", "fk_child", "self_ref", "orders", "users", "products" }
                .Contains(t.TableName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(7, selected.Count);

        var edges = (await new ForeignKeyService().GetForeignKeysAsync(connection, selected))
            .Select(fk => $"{fk.ChildTable}>{fk.ParentTable}".ToLowerInvariant())
            .ToHashSet();

        Assert.Contains("fk_parent>fk_grandparent", edges);
        Assert.Contains("fk_child>fk_parent", edges);
        Assert.Contains("orders>users", edges);
        Assert.Contains("orders>products", edges);
        Assert.Contains("self_ref>self_ref", edges);
    }

    [Trait("Category", "E2E")]
    [Theory]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.Oracle)]
    public async Task FixtureTables_AreOrderedParentsFirst_OnEveryDialect(DatabaseType type)
    {
        if (!ShouldRunE2E()) return;

        var connection = FixtureConnection(type);
        var alphabetical = (await new DatabaseService().GetTablesAsync(connection))
            .Where(t => string.Equals(t.Schema, "migration_test", StringComparison.OrdinalIgnoreCase))
            .Where(t => new[] { "fk_grandparent", "fk_parent", "fk_child" }
                .Contains(t.TableName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var foreignKeys = await new ForeignKeyService().GetForeignKeysAsync(connection, alphabetical);

        var order = TableDependencyOrderer.Order(alphabetical, foreignKeys).Tables
            .Select(t => t.TableName.ToLowerInvariant()).ToList();

        Assert.Equal(["fk_grandparent", "fk_parent", "fk_child"], order);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The same steps the migration performs: plan, load in plan order, then always complete the plan.</summary>
    private static async Task<IReadOnlyList<string>> LoadAllAsync(Scratch db)
    {
        var service = new DatabaseService();
        var plan = await new ForeignKeyService().PrepareDataLoadAsync(db.Target, db.AlphabeticalTables);
        bool succeeded = false;
        IReadOnlyList<string> warnings;
        try
        {
            foreach (var table in plan.OrderedTables)
                await service.MigrateTableAsync(db.Source, db.Target, table, new Progress<int>());
            succeeded = true;
        }
        finally
        {
            warnings = await plan.CompleteAsync(succeeded);
        }
        return warnings;
    }

    private static async Task AssertRowCountsMatchAsync(Scratch db)
    {
        foreach (var name in new[] { "z_head", "a_item", "m_node" })
        {
            long source = await db.ScalarAsync(db.Source, $"SELECT COUNT(*) FROM dbo.{name}");
            long target = await db.ScalarAsync(db.Target, $"SELECT COUNT(*) FROM dbo.{name}");
            Assert.Equal(source, target);
            Assert.True(source > 0, $"{name} must have rows to make the comparison meaningful");
        }
    }

    private static SqlException? FindSqlException(Exception? ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
            if (current is SqlException sql)
                return sql;
        return null;
    }

    private static ConnectionInfo FixtureConnection(DatabaseType type) => type switch
    {
        // 127.0.0.1, not "localhost": see CrossDatabaseDataMigrationMatrixTests.BuildConnectionInfo.
        DatabaseType.SqlServer => new ConnectionInfo
        {
            DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = "TestDB",
            Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
        },
        DatabaseType.PostgreSQL => new ConnectionInfo
        {
            DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Port = 5432, Database = "testdb",
            Username = "pguser", Password = "pgpass123"
        },
        DatabaseType.Oracle => new ConnectionInfo
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "migration_test", Password = "oraclepass123"
        },
        _ => throw new NotSupportedException()
    };

    private static ConnectionInfo FixtureConnection(DatabaseType type, string database)
    {
        var info = FixtureConnection(type);
        info.Database = database;
        return info;
    }

    /// <summary>Two throw-away SQL Server databases: a source without FOREIGN KEYs and a target that has them.</summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private readonly ConnectionInfo _master = FixtureConnection(DatabaseType.SqlServer, "master");

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; }

        /// <summary>Alphabetical, as the UI lists them: the child a_item sorts before its parent z_head.</summary>
        public IReadOnlyList<TableInfo> AlphabeticalTables { get; } =
            new[] { "a_item", "m_node", "z_head" }.Select(n => new TableInfo { Schema = "dbo", TableName = n }).ToList();

        private Scratch(ConnectionInfo source, ConnectionInfo target)
        {
            Source = source;
            Target = target;
        }

        public TableInfo Table(string name) => new() { Schema = "dbo", TableName = name };

        public static async Task<Scratch> CreateAsync()
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var scratch = new Scratch(
                FixtureConnection(DatabaseType.SqlServer, $"fkorder_src_{id}"),
                FixtureConnection(DatabaseType.SqlServer, $"fkorder_tgt_{id}"));

            await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Source.Database}]");
            await scratch.ExecAsync(scratch._master, $"CREATE DATABASE [{scratch.Target.Database}]");

            const string columns = @"
                CREATE TABLE dbo.z_head (id INT NOT NULL PRIMARY KEY, name VARCHAR(50) NOT NULL);
                CREATE TABLE dbo.a_item (id INT NOT NULL PRIMARY KEY, head_id INT NOT NULL, name VARCHAR(50) NOT NULL);
                CREATE TABLE dbo.m_node (id INT NOT NULL PRIMARY KEY, parent_id INT NULL, name VARCHAR(50) NOT NULL);";
            await scratch.ExecAsync(scratch.Source, columns);
            await scratch.ExecAsync(scratch.Target, columns + @"
                ALTER TABLE dbo.a_item ADD CONSTRAINT FK_a_item_z_head FOREIGN KEY (head_id) REFERENCES dbo.z_head(id);
                ALTER TABLE dbo.m_node ADD CONSTRAINT FK_m_node_parent FOREIGN KEY (parent_id) REFERENCES dbo.m_node(id);");

            await scratch.ExecAsync(scratch.Source, $@"
                INSERT dbo.z_head (id, name) VALUES (1, 'h1'), (2, 'h2'), (3, 'h3');
                INSERT dbo.a_item (id, head_id, name) VALUES (1, 1, 'i1'), (2, 1, 'i2'), (3, 2, 'i3'), (4, 3, 'i4'), (5, 3, 'i5');
                ;WITH n AS (
                    SELECT TOP ({SelfReferencingRows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                    FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                INSERT dbo.m_node (id, parent_id, name)
                SELECT i, CASE WHEN i <= 1500 THEN i + 1000 END, CONCAT('n', i) FROM n;");
            return scratch;
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
