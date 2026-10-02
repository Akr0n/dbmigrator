using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// Oracle has no TRUNCATE the migration can use, so it empties a target table with DELETE. An enabled key with
/// ON DELETE CASCADE then deletes rows in the tables that reference it, and one with ON DELETE SET NULL updates them,
/// including tables the user never selected.
/// </summary>
public class OracleDeleteCascadeE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableWithOnDeleteCascadeTheUserDidNotSelect_KeepsItsRows()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')", "INSERT INTO {head} VALUES (2, 'old')",
            "INSERT INTO {child} VALUES (1, 1, 'old')", "INSERT INTO {child} VALUES (2, 2, 'old')");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // Only head is selected: DELETE FROM head would silently delete child's rows too. The user is asked (and declines).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains(db.Name("child"), asked.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await db.ScalarAsync("SELECT COUNT(*) FROM {child}"));
        Assert.Equal(2, await db.ScalarAsync("SELECT COUNT(*) FROM {head}"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableWithOnDeleteSetNullTheUserDidNotSelect_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE SET NULL", childHeadIdNullable: true);
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')", "INSERT INTO {child} VALUES (1, 1, 'old')");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains(db.Name("child"), asked.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM {child} WHERE head_id IS NOT NULL")); // not set to NULL
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableReachedOnlyThroughACascadeChild_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE", withLeaf: true);
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')", "INSERT INTO {child} VALUES (1, 1, 'old')",
            "INSERT INTO {leaf} VALUES (1, 1, 'old')");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // head and child are selected; leaf is not, and loses its rows when child's rows are deleted by the cascade.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(),
                tablesLoadedLater: [db.Table("child")]));

        Assert.Contains(db.Name("leaf"), asked.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM {leaf}"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ReferencingTablesLoadedLaterInTheSameRun_AreReplacedAsBefore()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')", "INSERT INTO {head} VALUES (2, 'old')",
            "INSERT INTO {child} VALUES (1, 1, 'old')", "INSERT INTO {child} VALUES (2, 2, 'old')");
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };
        var head = db.Table("head");
        var child = db.Table("child");

        // A re-run over a populated target, with both tables selected and head loaded first.
        await service.MigrateTableAsync(db.Source, db.Target, head, new Progress<int>(), tablesLoadedLater: [child]);
        await service.MigrateTableAsync(db.Source, db.Target, child, new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync("SELECT COUNT(*) FROM {head}"));
        Assert.Equal(2, await db.ScalarAsync("SELECT COUNT(*) FROM {child}"));
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM {child} WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ReferencingTablesThatAreEmpty_DoNotBlockTheLoad()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')"); // child exists, empty
        var asked = 0;
        var service = new DatabaseService { TruncateFailedHandlerAsync = _ => { asked++; return Task.FromResult(false); } };

        await service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []);

        Assert.Equal(0, asked);
        Assert.Equal(3, await db.ScalarAsync("SELECT COUNT(*) FROM {head} WHERE name = 'new'"));
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM {head} WHERE name = 'old'"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task AKeyWithoutADeleteRule_IsLeftToOracleToRefuse()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')", "INSERT INTO {child} VALUES (1, 1, 'old')");
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // NO ACTION: Oracle itself refuses the DELETE (ORA-02292) and nothing is lost, as before this check existed.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains("ORA-02292", asked.Single());
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM {child}"));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ATableWithAQuotedMixedCaseNameTheUserDidNotSelect_IsProtectedToo()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        string quoted = db.Name("mix"); // lower case on purpose: Oracle finds it only by its exact, quoted name
        string key = $"fk_mx_{quoted[^6..]}";
        try
        {
            await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')",
                $"CREATE TABLE \"{quoted}\" (id NUMBER(10) PRIMARY KEY, head_id NUMBER(10) NOT NULL, " +
                $"CONSTRAINT {key} FOREIGN KEY (head_id) REFERENCES {{head}}(id) ON DELETE CASCADE)",
                $"INSERT INTO \"{quoted}\" VALUES (1, 1)");
            var asked = new List<string>();
            var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

            Assert.Contains(quoted, asked.Single(), StringComparison.Ordinal);
            Assert.Equal(1, await db.ScalarAsync($"SELECT COUNT(*) FROM \"{quoted}\""));
        }
        finally
        {
            await db.OracleAsync($"DROP TABLE \"{quoted}\" PURGE");
        }
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task ACascadeChildInAnotherSchemaTheUserCannotSee_IsRefusedInsteadOfBeingWipedUnseen()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')");
        string otherSchema = await db.CreateChildInAnotherSchemaAsync();
        var asked = new List<string>();
        var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

        // The migration user has REFERENCES-granted tables pointing at head from a schema it cannot read: ALL_CONSTRAINTS does
        // not list that key, but Oracle would still cascade the DELETE into it.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

        Assert.Contains(otherSchema, asked.Single());
        Assert.Contains("SELECT_CATALOG_ROLE", asked.Single()); // says how to let the check see the keys
        Assert.Equal(1, await db.ScalarAsync($"SELECT COUNT(*) FROM {otherSchema}.child_other", asSystem: true));
    }

    [Trait("Category", "E2E")]
    [Fact]
    public async Task WithAccessToTheDataDictionary_TheChildInAnotherSchemaIsNamedAndRefused()
    {
        if (!ShouldRunE2E()) return;
        await using var db = await Scratch.CreateAsync(childRule: "ON DELETE CASCADE");
        await db.OracleAsync("INSERT INTO {head} VALUES (1, 'old')");
        string otherSchema = await db.CreateChildInAnotherSchemaAsync();
        await db.SystemAsync("GRANT SELECT_CATALOG_ROLE TO migration_test");
        Oracle.ManagedDataAccess.Client.OracleConnection.ClearAllPools(); // a role is active only in sessions opened after the grant
        try
        {
            var asked = new List<string>();
            var service = new DatabaseService { TruncateFailedHandlerAsync = ctx => { asked.Add(ctx.ErrorMessage ?? ""); return Task.FromResult(false); } };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.MigrateTableAsync(db.Source, db.Target, db.Table("head"), new Progress<int>(), tablesLoadedLater: []));

            Assert.Contains($"{otherSchema}.CHILD_OTHER", asked.Single());
            Assert.DoesNotContain("SELECT_CATALOG_ROLE", asked.Single());
            Assert.Equal(1, await db.ScalarAsync($"SELECT COUNT(*) FROM {otherSchema}.child_other", asSystem: true));
        }
        finally
        {
            await db.SystemAsync("REVOKE SELECT_CATALOG_ROLE FROM migration_test");
            Oracle.ManagedDataAccess.Client.OracleConnection.ClearAllPools();
        }
    }

    /// <summary>A SQL Server source database with the rows to load, and the matching tables in the Oracle fixture schema.</summary>
    private sealed class Scratch : IAsyncDisposable
    {
        private const string Schema = "migration_test"; // the Oracle fixture user, and a schema created in the source database
        private readonly string _id = Guid.NewGuid().ToString("N")[..6];
        private readonly List<string> _oracleTables = [];

        private static readonly ConnectionInfo Master = new()
        {
            DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = "master",
            Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
        };

        public ConnectionInfo Source { get; }
        public ConnectionInfo Target { get; } = new()
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = Schema, Password = "oraclepass123"
        };

        private Scratch()
        {
            Source = new ConnectionInfo
            {
                DatabaseType = DatabaseType.SqlServer, Server = "127.0.0.1", Port = 1433, Database = $"ocas_src_{_id}",
                Username = "sa", Password = "SqlServer@123", TrustServerCertificate = true
            };
        }

        public string Name(string logical) => $"ocas_{logical}_{_id}";

        public TableInfo Table(string logical) => new() { Schema = Schema, TableName = Name(logical) };

        private string Expand(string sql) =>
            sql.Replace("{head}", Name("head")).Replace("{child}", Name("child")).Replace("{leaf}", Name("leaf"));

        public static async Task<Scratch> CreateAsync(string childRule, bool childHeadIdNullable = false, bool withLeaf = false)
        {
            var scratch = new Scratch();
            try
            {
                await scratch.SqlServerAsync(Master, $"CREATE DATABASE [{scratch.Source.Database}]");
                await scratch.SqlServerAsync(scratch.Source, $"CREATE SCHEMA [{Schema}]");
                await scratch.SqlServerAsync(scratch.Source, scratch.Expand($@"
                    CREATE TABLE [{Schema}].[{{head}}] (id INT, name VARCHAR(50));
                    CREATE TABLE [{Schema}].[{{child}}] (id INT, head_id INT, name VARCHAR(50));
                    CREATE TABLE [{Schema}].[{{leaf}}] (id INT, child_id INT, name VARCHAR(50));
                    INSERT [{Schema}].[{{head}}] VALUES (1, 'new'), (2, 'new'), (3, 'new');
                    INSERT [{Schema}].[{{child}}] VALUES (1, 1, 'new'), (2, 2, 'new');"));

                string notNull = childHeadIdNullable ? "" : " NOT NULL";
                scratch._oracleTables.Add(scratch.Name("head"));
                await scratch.OracleAsync("CREATE TABLE {head} (id NUMBER(10) PRIMARY KEY, name VARCHAR2(50) NOT NULL)");
                scratch._oracleTables.Add(scratch.Name("child"));
                await scratch.OracleAsync($@"CREATE TABLE {{child}} (id NUMBER(10) PRIMARY KEY, head_id NUMBER(10){notNull},
                    name VARCHAR2(50) NOT NULL, CONSTRAINT fk_c_{scratch._id} FOREIGN KEY (head_id) REFERENCES {{head}}(id) {childRule})");
                if (withLeaf)
                {
                    scratch._oracleTables.Add(scratch.Name("leaf"));
                    await scratch.OracleAsync($@"CREATE TABLE {{leaf}} (id NUMBER(10) PRIMARY KEY, child_id NUMBER(10) NOT NULL,
                        name VARCHAR2(50) NOT NULL, CONSTRAINT fk_l_{scratch._id} FOREIGN KEY (child_id) REFERENCES {{child}}(id) ON DELETE CASCADE)");
                }
                return scratch;
            }
            catch
            {
                await scratch.DisposeAsync(); // `await using` never sees a Scratch that was not returned
                throw;
            }
        }

        public async Task OracleAsync(params string[] statements)
        {
            await using var connection = new OracleConnection(Target.GetConnectionString());
            await connection.OpenAsync();
            foreach (var statement in statements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = Expand(statement);
                await command.ExecuteNonQueryAsync();
            }

            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT";
            await commit.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(string sql, bool asSystem = false)
        {
            await using var connection = new OracleConnection((asSystem ? Administrator : Target).GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = Expand(sql);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private static readonly ConnectionInfo Administrator = new()
        {
            DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
            Username = "SYSTEM", Password = "oraclepass123"
        };

        private readonly List<string> _foreignUsers = [];

        /// <summary>Runs statements as SYSTEM (the fixture container's administrator) to set up users and grants.</summary>
        public async Task SystemAsync(params string[] statements)
        {
            await using var connection = new OracleConnection(Administrator.GetConnectionString());
            await connection.OpenAsync();
            foreach (var statement in statements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = Expand(statement);
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Creates another Oracle user that was granted REFERENCES on head and owns child_other with an ON DELETE CASCADE key
        /// to it and one row. The migration user cannot read that schema. Returns the other schema's name.
        /// </summary>
        public async Task<string> CreateChildInAnotherSchemaAsync()
        {
            string user = $"OCAS_O_{_id}".ToUpperInvariant();
            string password = $"Pw_{_id}_x1";
            _foreignUsers.Add(user);
            await SystemAsync($"CREATE USER {user} IDENTIFIED BY \"{password}\" QUOTA UNLIMITED ON USERS",
                $"GRANT CREATE SESSION, CREATE TABLE TO {user}",
                $"GRANT REFERENCES ON {Schema}.{Name("head")} TO {user}");

            var owner = new ConnectionInfo
            {
                DatabaseType = DatabaseType.Oracle, Server = "127.0.0.1", Port = 1521, Database = "FREEPDB1",
                Username = user, Password = password
            };
            await using var connection = new OracleConnection(owner.GetConnectionString());
            await connection.OpenAsync();
            foreach (var statement in new[]
            {
                $"CREATE TABLE child_other (id NUMBER(10) PRIMARY KEY, head_id NUMBER(10) NOT NULL, " +
                    $"CONSTRAINT fk_o_{_id} FOREIGN KEY (head_id) REFERENCES {Schema}.{Name("head")}(id) ON DELETE CASCADE)",
                "INSERT INTO child_other VALUES (1, 1)",
                "COMMIT"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync();
            }

            return user;
        }

        private async Task SqlServerAsync(ConnectionInfo database, string sql)
        {
            await using var connection = new SqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            OracleConnection.ClearAllPools(); // pooled sessions of a user would keep it from being dropped
            foreach (var user in _foreignUsers)
            {
                try
                {
                    await SystemAsync($"DROP USER {user} CASCADE");
                }
                catch
                {
                    // Best effort: a leftover scratch user must not mask the real test result.
                }
            }

            foreach (var table in Enumerable.Reverse(_oracleTables))
            {
                try
                {
                    await using var connection = new OracleConnection(Target.GetConnectionString());
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"DROP TABLE {table} CASCADE CONSTRAINTS PURGE";
                    await command.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Best effort: a leftover scratch table must not mask the real test result.
                }
            }

            SqlConnection.ClearAllPools(); // pooled connections would keep the database from being dropped
            try
            {
                await SqlServerAsync(Master, $"ALTER DATABASE [{Source.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Source.Database}];");
            }
            catch
            {
                // Same: a leftover scratch database is harmless on the throwaway fixture containers.
            }
        }
    }
}
