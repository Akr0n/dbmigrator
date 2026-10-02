using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// The key a resumed read is ordered by must give every row a place of its own: a key that is missing, disabled or not validated,
/// or one that is not unique across the rows a read returns, would let the read repeat or skip rows after a cut. And it must be
/// found by whoever reads the source, including a role that can only SELECT.
/// </summary>
public class SourceKeyColumnsE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static ConnectionInfo Postgres => new()
    {
        DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Database = "testdb", Username = "pguser", Password = "pgpass123",
        Port = int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_PORT"), out var port) ? port : 5432
    };

    private static ConnectionInfo Oracle => new()
    {
        DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
        Username = "migration_test", Password = "oraclepass123"
    };

    // ── PostgreSQL ───────────────────────────────────────────────────────────────────────────────────────

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Postgres_ACompositeKey_IsReadInKeyOrder_AndAMixedCaseColumnKeepsItsCase()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await PostgresScratch.CreateAsync();
        await db.ExecAsync(@"CREATE TABLE {s}.ck (""Zed"" int, a int, v text, PRIMARY KEY (a, ""Zed""))");

        var columns = await db.KeyColumnsAsync(Postgres, "ck");

        Assert.Equal(["a", "Zed"], columns); // key order, not column order; the catalog's own spelling
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Postgres_ARoleThatCanOnlySelect_StillSeesTheKey()
    {
        if (!ShouldRunE2E()) return;
        // information_schema.table_constraints lists a table to the roles that own it or hold a privilege other than SELECT: a
        // read-only source account got no key at all for any table.
        await using var db = await PostgresScratch.CreateAsync();
        await db.ExecAsync("CREATE TABLE {s}.ro (id int PRIMARY KEY, v text)");
        string role = $"kc_ro_{db.Id}";
        await db.ExecAsync($"CREATE ROLE {role} LOGIN PASSWORD 'ro-{db.Id}'; GRANT USAGE ON SCHEMA {{s}} TO {role}; GRANT SELECT ON {{s}}.ro TO {role}");
        var readOnly = Postgres;
        readOnly.Username = role;
        readOnly.Password = $"ro-{db.Id}";

        var columns = await db.KeyColumnsAsync(readOnly, "ro");

        Assert.Equal(["id"], columns);
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Postgres_AnInheritanceParent_HasNoUsableKey()
    {
        if (!ShouldRunE2E()) return;
        // SELECT * FROM parent also returns the rows of the tables that inherit from it, and the parent's key is not unique across them.
        await using var db = await PostgresScratch.CreateAsync();
        await db.ExecAsync("CREATE TABLE {s}.parent (id int PRIMARY KEY); CREATE TABLE {s}.child () INHERITS ({s}.parent)");

        Assert.Empty(await db.KeyColumnsAsync(Postgres, "parent"));
        Assert.Equal(["id"], await db.KeyColumnsAsync(Postgres, "child_free_probe")); // a table nobody inherits from keeps its key
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Postgres_APartitionedTable_KeepsItsKey()
    {
        if (!ShouldRunE2E()) return;
        // Declarative partitions are recorded in pg_inherits like legacy inheritance, but a primary key on a partitioned table has to
        // contain the partition key, so it IS unique across the partitions: this table must still be resumable.
        await using var db = await PostgresScratch.CreateAsync();
        await db.ExecAsync(@"CREATE TABLE {s}.part (id int, region int, v text, PRIMARY KEY (id, region)) PARTITION BY LIST (region);
                             CREATE TABLE {s}.part_1 PARTITION OF {s}.part FOR VALUES IN (1);
                             CREATE TABLE {s}.part_2 PARTITION OF {s}.part FOR VALUES IN (2)");

        Assert.Equal(["id", "region"], await db.KeyColumnsAsync(Postgres, "part"));
        Assert.Equal(["id", "region"], await db.KeyColumnsAsync(Postgres, "part_1")); // a partition has the key too
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Postgres_ATableWhoseKeyColumnHasMixedCase_IsStillMigrated()
    {
        if (!ShouldRunE2E()) return;
        // The ORDER BY of the source read once used the names as the migration writes them to a target (lower case): for a source
        // column "CustomerID" that is "column customerid does not exist", and a table that migrated fine stopped migrating.
        string id = Guid.NewGuid().ToString("N")[..8];
        var source = Postgres;
        source.Database = $"kc_src_{id}";
        var target = Postgres;
        target.Database = $"kc_tgt_{id}";
        try
        {
            await ExecAsync(Postgres, $"CREATE DATABASE {source.Database}; CREATE DATABASE {target.Database}");
            await ExecAsync(source, @"CREATE TABLE public.mc (""CustomerID"" int PRIMARY KEY, v text);
                                      INSERT INTO public.mc SELECT i, 'v' || i FROM generate_series(1, 50) i");
            await ExecAsync(target, "CREATE TABLE public.mc (customerid int PRIMARY KEY, v text)"); // as the tool creates it: lower case

            await new DatabaseService { ResumeMinRows = 0 }
                .MigrateTableAsync(source, target, new TableInfo { Schema = "public", TableName = "mc" }, new Progress<int>());

            Assert.Equal(50L, await ScalarAsync(target, "SELECT COUNT(*) FROM public.mc"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            foreach (var name in new[] { source.Database, target.Database })
            {
                try { await ExecAsync(Postgres, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)"); } catch { /* best effort */ }
            }
        }
    }

    private static async Task ExecAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(ConnectionInfo database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    // ── Oracle ───────────────────────────────────────────────────────────────────────────────────────────

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Oracle_AnEnabledAndValidatedKey_IsReadInKeyOrder()
    {
        if (!ShouldRunE2E()) return;
        await using var db = new OracleScratch();
        await db.ExecAsync("CREATE TABLE {t} (b NUMBER, a NUMBER, CONSTRAINT {pk} PRIMARY KEY (b, a))");

        Assert.Equal(["B", "A"], await db.KeyColumnsAsync());
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Oracle_ADisabledKey_IsNotUsable()
    {
        if (!ShouldRunE2E()) return;
        // A key declared DISABLE enforces neither uniqueness nor NOT NULL, so rows can tie on it.
        await using var db = new OracleScratch();
        await db.ExecAsync("CREATE TABLE {t} (id NUMBER, CONSTRAINT {pk} PRIMARY KEY (id) DISABLE)");

        Assert.Empty(await db.KeyColumnsAsync());
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task Oracle_AKeyEnabledWithoutValidation_IsNotUsable()
    {
        if (!ShouldRunE2E()) return;
        // ENABLE NOVALIDATE on a non-unique index guards the rows added from now on, not the ones already there (here: two rows with id 1).
        await using var db = new OracleScratch();
        await db.ExecAsync("CREATE TABLE {t} (id NUMBER)");
        await db.ExecAsync("INSERT INTO {t} VALUES (1)");
        await db.ExecAsync("INSERT INTO {t} VALUES (1)");
        await db.ExecAsync("CREATE INDEX {ix} ON {t} (id)");
        await db.ExecAsync("ALTER TABLE {t} ADD CONSTRAINT {pk} PRIMARY KEY (id) ENABLE NOVALIDATE");

        Assert.Empty(await db.KeyColumnsAsync());
    }

    // ── scratch objects ──────────────────────────────────────────────────────────────────────────────────

    private sealed class PostgresScratch : IAsyncDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        private string Schema => $"kc_{Id}";

        public static async Task<PostgresScratch> CreateAsync()
        {
            var scratch = new PostgresScratch();
            await scratch.ExecAsync("CREATE SCHEMA {s}; CREATE TABLE {s}.child_free_probe (id int PRIMARY KEY)");
            return scratch;
        }

        public async Task ExecAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(Postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql.Replace("{s}", Schema);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<string>> KeyColumnsAsync(ConnectionInfo asUser, string table)
        {
            await using var connection = new NpgsqlConnection(asUser.GetConnectionString());
            await connection.OpenAsync();
            return await SourceResume.GetKeyColumnsAsync(connection, DatabaseType.PostgreSQL, Schema, table, 60);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools(); // a pooled session of the read-only role would keep it from being dropped
            try
            {
                await ExecAsync($"DROP SCHEMA IF EXISTS {{s}} CASCADE; DROP OWNED BY kc_ro_{Id}; DROP ROLE IF EXISTS kc_ro_{Id}");
            }
            catch
            {
                // The role was never created (DROP OWNED BY fails then): the schema still has to go.
                try { await ExecAsync("DROP SCHEMA IF EXISTS {s} CASCADE"); } catch { /* best effort */ }
            }
        }
    }

    private sealed class OracleScratch : IAsyncDisposable
    {
        private readonly string _id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        private string Table => $"KC_{_id}";

        public async Task ExecAsync(string sql)
        {
            await using var connection = new OracleConnection(Oracle.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql.Replace("{t}", Table).Replace("{pk}", $"PK_{_id}").Replace("{ix}", $"IX_{_id}");
            await command.ExecuteNonQueryAsync();
            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT";
            await commit.ExecuteNonQueryAsync();
        }

        public async Task<List<string>> KeyColumnsAsync()
        {
            await using var connection = new OracleConnection(Oracle.GetConnectionString());
            await connection.OpenAsync();
            return await SourceResume.GetKeyColumnsAsync(connection, DatabaseType.Oracle, "MIGRATION_TEST", Table, 60);
        }

        public async ValueTask DisposeAsync()
        {
            try { await ExecAsync("DROP TABLE {t} PURGE"); } catch { /* the table was never created */ }
        }
    }
}
