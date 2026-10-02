using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Npgsql;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// PostgreSQL refuses to TRUNCATE a table that another table references, so the data load used TRUNCATE ... CASCADE,
/// which also empties every table that references it, directly or not: including tables the user never selected.
/// </summary>
public class PostgresTruncateCascadeE2ETests
{
    // Same port override as a local PostgreSQL service on 5432 needs; CI leaves it unset.
    private static readonly int PgPort =
        int.TryParse(Environment.GetEnvironmentVariable("DBMIGRATOR_PG_PORT"), out var port) ? port : 5432;

    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableTheUserDidNotSelect_KeepsItsRows()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, @"
            INSERT INTO head VALUES (1, 'old'), (2, 'old');
            INSERT INTO child VALUES (1, 1, 'old'), (2, 2, 'old');");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // Only head is selected; child references it and holds rows. The user is asked (and declines), nothing is wiped.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Single(asked);
        Assert.Contains("child", asked[0]);
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM child"));
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableReachedOnlyThroughASelectedOne_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, @"
            INSERT INTO head VALUES (1, 'old');
            INSERT INTO child VALUES (1, 1, 'old');
            INSERT INTO leaf VALUES (1, 1, 'old');");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // head and child are selected, leaf is not: it references child, which references head.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(),
                tablesLoadedLater: [db.Table("child")]));

        Assert.Contains("leaf", asked.Single());
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM leaf"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ReferencingTablesThatAreEmpty_DoNotBlockTheLoad()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, "INSERT INTO head VALUES (1, 'old'), (2, 'old');"); // child and leaf exist, empty
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        await service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name <> 'old'"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ReferencingTablesLoadedLaterInTheSameRun_AreReplacedAsBefore()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, @"
            INSERT INTO head VALUES (1, 'old'), (2, 'old');
            INSERT INTO child VALUES (1, 1, 'old'), (2, 2, 'old');");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };
        var head = db.Table("head");
        var child = db.Table("child");

        // A re-run over a populated target, with both tables selected and head loaded first.
        await service.MigrateTableAsync(db.Source, db.Target, head, new Progress<int>(), tablesLoadedLater: [child]);
        await service.MigrateTableAsync(db.Source, db.Target, child, new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head"));
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM child"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM child WHERE name = 'old'"));
    }

    // ── tables that PostgreSQL empties together with another one without any foreign key pointing at it ──

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableReferencingAPartitionOfTheTable_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraSource: "CREATE TABLE proot (id INT, name TEXT);",
            extraTarget: @"
                CREATE TABLE proot (id INT NOT NULL, name TEXT NOT NULL, PRIMARY KEY (id)) PARTITION BY RANGE (id);
                CREATE TABLE proot_1 PARTITION OF proot FOR VALUES FROM (0) TO (1000);
                CREATE TABLE refs_part (id INT PRIMARY KEY, p_id INT NOT NULL REFERENCES proot_1(id));
                INSERT INTO proot VALUES (1, 'old');
                INSERT INTO refs_part VALUES (1, 1);");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // Emptying proot empties its partition, and with it everything that references the partition.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("proot"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains("refs_part", asked.Single());
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM refs_part"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableReferencingAnInheritanceChildOfTheTable_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraSource: "CREATE TABLE iroot (id INT, name TEXT);",
            extraTarget: @"
                CREATE TABLE iroot (id INT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE ichild () INHERITS (iroot);
                ALTER TABLE ichild ADD PRIMARY KEY (id);
                CREATE TABLE refs_ichild (id INT PRIMARY KEY, c_id INT NOT NULL REFERENCES ichild(id));
                INSERT INTO ichild VALUES (1, 'old');
                INSERT INTO refs_ichild VALUES (1, 1);");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("iroot"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains("refs_ichild", asked.Single());
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM refs_ichild"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task PartitionsOfASelectedReferencingTable_AreNotTreatedAsSeparateTables()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraSource: @"
                CREATE TABLE pref (id INT, head_id INT, name TEXT);
                INSERT INTO pref VALUES (1, 1, 'new'), (2, 2, 'new');",
            extraTarget: @"
                CREATE TABLE pref (id INT NOT NULL, head_id INT NOT NULL REFERENCES head(id), name TEXT NOT NULL, PRIMARY KEY (id))
                    PARTITION BY RANGE (id);
                CREATE TABLE pref_1 PARTITION OF pref FOR VALUES FROM (0) TO (1000);
                INSERT INTO head VALUES (1, 'old');
                INSERT INTO pref VALUES (1, 1, 'old');");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        // head and the partitioned pref are selected; its partition pref_1 holds rows but goes with pref.
        await service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: [db.Table("pref")]);
        await service.MigrateTableAsync(db.Source, db.Target, db.Table("pref"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM pref WHERE name = 'new'"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM pref WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AnEmptyTargetTable_IsNotTruncated_SoATableLoadedEarlierInAForeignKeyCycleSurvives()
    {
        if (!ShouldRunE2E()) return;
        const string columns = "CREATE TABLE ca (id INT PRIMARY KEY, cb_id INT); CREATE TABLE cb (id INT PRIMARY KEY, ca_id INT);";
        await using var db = await Scratch.CreateAsync(
            extraSource: columns + @"
                INSERT INTO ca VALUES (1, NULL), (2, NULL);
                INSERT INTO cb VALUES (1, 1), (2, 2);",
            extraTarget: columns + @"
                ALTER TABLE ca ADD FOREIGN KEY (cb_id) REFERENCES cb(id);
                ALTER TABLE cb ADD FOREIGN KEY (ca_id) REFERENCES ca(id);");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        // ca then cb, as the load order breaks the cycle. Truncating the empty cb ... CASCADE would empty the ca just loaded.
        await service.MigrateTableAsync(db.Source, db.Target, db.Table("ca"), new Progress<int>(), tablesLoadedLater: [db.Table("cb")]);
        await service.MigrateTableAsync(db.Source, db.Target, db.Table("cb"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM ca"));
        Assert.Equal(2, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM cb"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AnOrdinaryInheritanceChildOfTheTable_IsNotTheSameTableAsItsParent()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraSource: "CREATE TABLE iroot (id INT, name TEXT);",
            extraTarget: @"
                CREATE TABLE iroot (id INT PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE ichild () INHERITS (iroot);
                INSERT INTO ichild VALUES (1, 'old');");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // Unlike a partition, ichild is a table of its own that the user may or may not have selected; TRUNCATE iroot empties it.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("iroot"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains("ichild", asked.Single());
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM ichild"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task InheritanceChildrenOfAReferencingTable_AreNotCountedAsEmptiedByTheCascade()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraTarget: @"
                CREATE TABLE ref (id INT PRIMARY KEY, head_id INT NOT NULL REFERENCES head(id));
                CREATE TABLE refchild () INHERITS (ref);
                INSERT INTO head VALUES (1, 'old');
                INSERT INTO refchild VALUES (1, 1);");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        // TRUNCATE head CASCADE empties ref only, not the children ref has through INHERITS.
        await service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM refchild"));
        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'new'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task NamesInTheCaseOfAnotherDialect_AreMatchedAgainstTheLowerCasedTarget()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, @"
            INSERT INTO head VALUES (1, 'old');
            INSERT INTO child VALUES (1, 1, 'old');");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // A SQL Server or Oracle source lists PUBLIC.HEAD; on PostgreSQL the table is public.head.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, new TableInfo { Schema = "PUBLIC", TableName = "HEAD" },
                new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains("child", asked.Single());
        Assert.Equal(1, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM child"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ARoleWithoutSelectPrivilege_StillReplacesAPopulatedTable()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        await db.ExecAsync(db.Target, "INSERT INTO head VALUES (1, 'old'), (2, 'old');");
        var limited = await db.CreateTargetUserAsync("INSERT, TRUNCATE");

        // The role cannot see whether head holds rows, so it must not be taken for empty: head is still truncated.
        await new DatabaseService().MigrateTableAsync(db.Source, limited, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'new'"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ARoleWithoutUsageOnTheSchemaOfAReferencingTable_StillMigrates()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraTarget: @"
                CREATE SCHEMA other;
                CREATE TABLE other.refs (id INT PRIMARY KEY, head_id INT NOT NULL REFERENCES public.head(id));
                INSERT INTO head VALUES (1, 'old');
                INSERT INTO other.refs VALUES (1, 1);");
        var limited = await db.CreateTargetUserAsync("SELECT, INSERT, TRUNCATE", "GRANT SELECT, TRUNCATE ON other.refs TO {role};");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        // Naming other.refs would fail with "permission denied for schema other" and abort the transaction;
        // TRUNCATE ... CASCADE reaches it by OID and needs no USAGE on its schema.
        await service.MigrateTableAsync(db.Source, limited, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'new'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task RowLevelSecurityHidingTheRows_DoesNotMakeATableLookEmpty()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(
            extraTarget: @"
                INSERT INTO head VALUES (1, 'old'), (2, 'old');
                ALTER TABLE head ENABLE ROW LEVEL SECURITY;
                CREATE POLICY see_nothing ON head FOR SELECT USING (false);
                CREATE POLICY add_any ON head FOR INSERT WITH CHECK (true);");
        var limited = await db.CreateTargetUserAsync("SELECT, INSERT, TRUNCATE");

        // The role sees no rows through the policy, but TRUNCATE ignores it: head must still be truncated, not skipped.
        await new DatabaseService().MigrateTableAsync(db.Source, limited, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'new'"));
        Assert.Equal(0, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ARoleWithoutSelectPrivilege_StillMigrates()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync();
        var limited = await db.CreateTargetUserAsync("INSERT, TRUNCATE");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        // TRUNCATE ... CASCADE needs no SELECT on head, child or leaf, so checking them must not turn into a failure.
        await service.MigrateTableAsync(db.Source, limited, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync(db.Target, "SELECT COUNT(*) FROM head"));
    }

    private static ConnectionInfo Connection(string database) => new()
    {
        DatabaseType = DatabaseType.PostgreSQL, Server = "127.0.0.1", Port = PgPort, Database = database,
        Username = "pguser", Password = "pgpass123"
    };

    /// <summary>Two throw-away PostgreSQL databases: a source with rows and a target that has the FOREIGN KEYs.</summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private readonly ConnectionInfo _admin = Connection("testdb");

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; }

        private Scratch(ConnectionInfo source, ConnectionInfo target)
        {
            Source = source;
            Target = target;
        }

        public TableInfo Table(string name) => new() { Schema = "public", TableName = name };

        private string? _role;

        /// <summary>
        /// A non-owner role on the target with <paramref name="headPrivileges"/> on head, TRUNCATE (and nothing else) on
        /// child and leaf, plus <paramref name="extraGrants"/> in which "{role}" stands for the role name.
        /// </summary>
        public async Task<ConnectionInfo> CreateTargetUserAsync(string headPrivileges, string extraGrants = "")
        {
            _role = $"trunc_user_{Target.Database[^8..]}";
            await ExecAsync(_admin, $"CREATE ROLE {_role} LOGIN PASSWORD 'rolepass'");
            await ExecAsync(Target, $@"
                GRANT USAGE ON SCHEMA public TO {_role};
                GRANT {headPrivileges} ON head TO {_role};
                GRANT TRUNCATE ON child, leaf TO {_role};
                {extraGrants.Replace("{role}", _role)}");
            var limited = Connection(Target.Database);
            limited.Username = _role;
            limited.Password = "rolepass";
            return limited;
        }

        public static async Task<Scratch> CreateAsync(string extraSource = "", string extraTarget = "")
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            var scratch = new Scratch(Connection($"trunc_src_{id}"), Connection($"trunc_tgt_{id}"));
            try
            {
                await scratch.ExecAsync(scratch._admin, $"CREATE DATABASE {scratch.Source.Database}");
                await scratch.ExecAsync(scratch._admin, $"CREATE DATABASE {scratch.Target.Database}");

                const string tables = @"
                    CREATE TABLE head (id INT PRIMARY KEY, name TEXT NOT NULL);
                    CREATE TABLE child (id INT PRIMARY KEY, head_id INT NOT NULL, name TEXT NOT NULL);
                    CREATE TABLE leaf (id INT PRIMARY KEY, child_id INT NOT NULL, name TEXT NOT NULL);";
                await scratch.ExecAsync(scratch.Source, tables + @"
                    INSERT INTO head VALUES (1, 'new'), (2, 'new'), (3, 'new');
                    INSERT INTO child VALUES (1, 1, 'new'), (2, 2, 'new');" + extraSource);
                await scratch.ExecAsync(scratch.Target, tables + @"
                    ALTER TABLE child ADD CONSTRAINT fk_child_head FOREIGN KEY (head_id) REFERENCES head(id);
                    ALTER TABLE leaf ADD CONSTRAINT fk_leaf_child FOREIGN KEY (child_id) REFERENCES child(id);" + extraTarget);
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
            await using var connection = new NpgsqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(ConnectionInfo database, string sql)
        {
            await using var connection = new NpgsqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools(); // pooled connections would keep the databases from being dropped
            foreach (var name in new[] { Source.Database, Target.Database })
            {
                try
                {
                    await ExecAsync(_admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
                }
                catch
                {
                    // Best effort: a leftover scratch database must not mask the real test result.
                }
            }

            if (_role != null)
            {
                try
                {
                    await ExecAsync(_admin, $"DROP ROLE IF EXISTS {_role}");
                }
                catch
                {
                    // Same: the role is cluster-wide but harmless once its databases are gone.
                }
            }
        }
    }
}
