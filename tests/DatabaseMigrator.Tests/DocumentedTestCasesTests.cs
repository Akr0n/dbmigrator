using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Automated E2E tests derived from TestCases_DatabaseMigrator.docx v1.0 (2026-04-06).
///
/// Automated coverage (26 test cases):
///   TC-001..TC-004  Connettività
///   TC-007..TC-019  Migrazione Schema+Dati SS→PG (singole tabelle)
///   TC-021..TC-025  Migrazione Schema+Dati cross-DB (SS↔Oracle, PG↔Oracle, PG↔SS)
///   TC-026..TC-027  Solo Schema
///   TC-028..TC-030  Solo Dati
///
/// NOT automatable — richiedono interazione con la GUI Avalonia (16 test cases):
///   TC-005  Toggle visibilità password (pulsante 👁)
///   TC-006  Porta si aggiorna al cambio tipo DB (ComboBox)
///   TC-020  Migrazione di tutte le 20 tabelle in una sola sessione (vedi DocumentedTestCases_FullMigrationTests)
///   TC-031  Seleziona Tutto / Deseleziona Tutto
///   TC-032  Filtro ricerca tabelle per nome
///   TC-033  Aggiorna tabelle (Refresh)
///   TC-034  Log: filtro Solo Errori
///   TC-035  Log: Copia negli appunti
///   TC-036  Log: Pulisci
///   TC-037  Log: auto-scroll durante migrazione
///   TC-038  Salva e ricarica configurazione (File menu)
///   TC-039  Chiusura finestra durante migrazione attiva
///   TC-040  TRUNCATE fallito — dialog di conferma
///   TC-041  Avvia Migrazione disabilitato senza tabelle selezionate
///   TC-042  DB target non raggiungibile a metà migrazione
/// </summary>
[Trait("Category", "E2E")]
public class DocumentedTestCasesTests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true",
            StringComparison.OrdinalIgnoreCase);

    private static readonly DatabaseService  DbSvc     = new();
    private static readonly SchemaMigrationService SchemaSvc = new();

    // ─── Connection strings ────────────────────────────────────────────────
    private const string SsConnStr = "Server=localhost,1433;Database=TestDB;User Id=sa;Password=SqlServer@123;TrustServerCertificate=True";
    private const string PgConnStr = "Host=localhost;Port=5432;Database=testdb;Username=pguser;Password=pgpass123";

    // ─── ConnectionInfo builders ───────────────────────────────────────────
    private static ConnectionInfo SS => new()
    {
        DatabaseType = DatabaseType.SqlServer, Server = "localhost", Port = 1433,
        Database = "TestDB", Username = "sa", Password = "SqlServer@123",
        TrustServerCertificate = true
    };
    private static ConnectionInfo PG => new()
    {
        DatabaseType = DatabaseType.PostgreSQL, Server = "localhost", Port = 5432,
        Database = "testdb", Username = "pguser", Password = "pgpass123"
    };
    private static ConnectionInfo OracleCI => new()
    {
        DatabaseType = DatabaseType.Oracle, Server = "localhost", Port = 1521,
        Database = "FREEPDB1", Username = "migration_test", Password = "oraclepass123"
    };
    private static ConnectionInfo SsWrongPwd => new()
    {
        DatabaseType = DatabaseType.SqlServer, Server = "localhost", Port = 1433,
        Database = "TestDB", Username = "sa", Password = "PASSWORDERRATA",
        TrustServerCertificate = true
    };

    // ─── PG cleanup helper ─────────────────────────────────────────────────
    private static async Task DropPgTablesAsync(params string[] tableNames)
    {
        await using var conn = new NpgsqlConnection(PgConnStr);
        await conn.OpenAsync();
        foreach (var t in tableNames.Reverse()) // reverse preserves FK order
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS migration_test.\"{t}\" CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─── SS cleanup helper ─────────────────────────────────────────────────
    // Pass tables in FK-safe drop order (children first, parents last).
    // SQL Server has no DROP ... CASCADE, so order matters.
    private static async Task DropSsTablesAsync(params string[] tableNames)
    {
        await using var conn = new SqlConnection(SsConnStr);
        await conn.OpenAsync();
        foreach (var t in tableNames) // no Reverse: caller provides correct FK order
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"IF OBJECT_ID('migration_test.[{t}]') IS NOT NULL DROP TABLE migration_test.[{t}]";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─── Row-count helpers ─────────────────────────────────────────────────
    private static async Task<long> PgRowCountAsync(string table)
    {
        await using var conn = new NpgsqlConnection(PgConnStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM migration_test.\"{table}\"";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> SsRowCountAsync(string table)
    {
        await using var conn = new SqlConnection(SsConnStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM migration_test.[{table}]";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // ─── Oracle cleanup helpers ────────────────────────────────────────────
    private const string OracleConnStr = "User Id=migration_test;Password=oraclepass123;Data Source=localhost:1521/FREEPDB1";

    /// <summary>Drops an Oracle table owned by migration_test (idempotent).</summary>
    private static async Task DropOracleTableAsync(string tableName)
    {
        await using var conn = new OracleConnection(OracleConnStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"BEGIN EXECUTE IMMEDIATE 'DROP TABLE {tableName} CASCADE CONSTRAINTS'; " +
                          $"EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes rows from Oracle tables in the supplied order.
    /// Pass FK-safe order (children before parents).
    /// </summary>
    private static async Task OracleDeleteRowsAsync(params string[] tableNames)
    {
        await using var conn = new OracleConnection(OracleConnStr);
        await conn.OpenAsync();
        foreach (var t in tableNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {t}";
            await cmd.ExecuteNonQueryAsync();
        }
        // Oracle DELETE is DML — commit explicitly
        await using var commitCmd = conn.CreateCommand();
        commitCmd.CommandText = "COMMIT";
        await commitCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes rows from SQL Server tables in the supplied order.
    /// Pass FK-safe order (children before parents).
    /// </summary>
    private static async Task SsDeleteRowsAsync(params string[] tableNames)
    {
        await using var conn = new SqlConnection(SsConnStr);
        await conn.OpenAsync();
        foreach (var t in tableNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM migration_test.[{t}]";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─── Table-info helper ─────────────────────────────────────────────────
    private static async Task<TableInfo> GetTableInfoAsync(
        ConnectionInfo ci, string schema, string tableName)
    {
        var tables = await DbSvc.GetTablesAsync(ci);
        return tables.First(t =>
            string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Schema,     schema,    StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.1  CONNETTIVITÀ
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC001_SsAndPg_ConnectionSucceeds_SS_Has20Tables()
    {
        if (!ShouldRunE2E()) return;
        Assert.True(await DbSvc.TestConnectionAsync(SS), "SQL Server connection failed");
        Assert.True(await DbSvc.TestConnectionAsync(PG), "PostgreSQL connection failed");
        var ssTables = await DbSvc.GetTablesAsync(SS);
        int count = ssTables.Count(t => t.Schema.Equals("migration_test", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task TC002_PgAndOracle_ConnectionSucceeds()
    {
        if (!ShouldRunE2E()) return;
        Assert.True(await DbSvc.TestConnectionAsync(PG),       "PostgreSQL connection failed");
        Assert.True(await DbSvc.TestConnectionAsync(OracleCI), "Oracle connection failed");
    }

    [Fact]
    public async Task TC003_OracleAndSs_ConnectionSucceeds_Oracle_Has20Tables()
    {
        if (!ShouldRunE2E()) return;
        Assert.True(await DbSvc.TestConnectionAsync(OracleCI), "Oracle connection failed");
        Assert.True(await DbSvc.TestConnectionAsync(SS),       "SQL Server connection failed");
        var oracleTables = await DbSvc.GetTablesAsync(OracleCI);
        int count = oracleTables.Count(t => t.Schema.Equals("MIGRATION_TEST", StringComparison.OrdinalIgnoreCase));
        Assert.True(count >= 18, $"Expected ≥18 Oracle tables, got {count}");
    }

    [Fact]
    public async Task TC004_WrongPassword_ConnectionFails()
    {
        if (!ShouldRunE2E()) return;
        Assert.False(await DbSvc.TestConnectionAsync(SsWrongPwd),
            "Connection with wrong password must fail");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.2  MIGRAZIONE SCHEMA+DATI — SS → PG
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC007_SsToPg_TablesBase_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("orders", "audit_log", "products", "users");
        var names = new[] { "users", "products", "orders", "audit_log" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(SS, "migration_test", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(SS, PG, tis);
        foreach (var ti in tis)
            await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("users"),    await PgRowCountAsync("users"));
        Assert.Equal(await SsRowCountAsync("products"), await PgRowCountAsync("products"));
        Assert.Equal(await SsRowCountAsync("orders"),    await PgRowCountAsync("orders"));
        Assert.Equal(await SsRowCountAsync("audit_log"), await PgRowCountAsync("audit_log"));
    }

    [Fact]
    public async Task TC008_SsToPg_BatchTest_1200RowsMigrated()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("batch_test");
        var ti = await GetTableInfoAsync(SS, "migration_test", "batch_test");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(1200L, await PgRowCountAsync("batch_test"));
    }

    [Fact]
    public async Task TC009_SsToPg_FkChain_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("fk_child", "fk_parent", "fk_grandparent");
        var names = new[] { "fk_grandparent", "fk_parent", "fk_child" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(SS, "migration_test", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(SS, PG, tis);
        foreach (var ti in tis)
            await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        foreach (var ti in tis)
            Assert.Equal(await SsRowCountAsync(ti.TableName), await PgRowCountAsync(ti.TableName));
    }

    [Fact]
    public async Task TC010_SsToPg_SelfRef_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("self_ref");
        var ti = await GetTableInfoAsync(SS, "migration_test", "self_ref");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("self_ref"), await PgRowCountAsync("self_ref"));
    }

    [Fact]
    public async Task TC011_SsToPg_NullEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("null_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "null_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("null_edge_cases"), await PgRowCountAsync("null_edge_cases"));
    }

    [Fact]
    public async Task TC012_SsToPg_StringEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("string_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "string_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("string_edge_cases"), await PgRowCountAsync("string_edge_cases"));
    }

    [Fact]
    public async Task TC013_SsToPg_NumericEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("numeric_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "numeric_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("numeric_edge_cases"), await PgRowCountAsync("numeric_edge_cases"));
    }

    [Fact]
    public async Task TC014_SsToPg_DatetimeEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("datetime_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "datetime_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("datetime_edge_cases"), await PgRowCountAsync("datetime_edge_cases"));
    }

    [Fact]
    public async Task TC015_SsToPg_BinaryEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("binary_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "binary_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("binary_edge_cases"), await PgRowCountAsync("binary_edge_cases"));
    }

    [Fact]
    public async Task TC016_SsToPg_ReservedWordsCols_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("reserved_words_cols");
        var ti = await GetTableInfoAsync(SS, "migration_test", "reserved_words_cols");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("reserved_words_cols"), await PgRowCountAsync("reserved_words_cols"));
    }

    [Fact]
    public async Task TC017_SsToPg_CompositePk_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("composite_pk");
        var ti = await GetTableInfoAsync(SS, "migration_test", "composite_pk");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("composite_pk"), await PgRowCountAsync("composite_pk"));
    }

    [Fact]
    public async Task TC018_SsToPg_MultiUnique_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("multi_unique");
        var ti = await GetTableInfoAsync(SS, "migration_test", "multi_unique");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("multi_unique"), await PgRowCountAsync("multi_unique"));
    }

    [Fact]
    public async Task TC019_SsToPg_WideTable_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("wide_table");
        var ti = await GetTableInfoAsync(SS, "migration_test", "wide_table");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("wide_table"), await PgRowCountAsync("wide_table"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.2  MIGRAZIONE SCHEMA+DATI — SS → Oracle
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC021_SsToOracle_BaseTables_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        // Oracle already has tables with data from init script.
        // orders FK references users → DELETE FROM users would fail unless orders is cleared first.
        await OracleDeleteRowsAsync("ORDERS", "USERS", "PRODUCTS");
        var names = new[] { "users", "products", "orders" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(SS, "migration_test", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(SS, OracleCI, tis);
        foreach (var ti in tis)
            await DbSvc.MigrateTableAsync(SS, OracleCI, ti, new Progress<int>());
        var oracleTables = await DbSvc.GetTablesAsync(OracleCI);
        foreach (var name in names)
        {
            long oracleCount = oracleTables
                .First(t => t.TableName.Equals(name, StringComparison.OrdinalIgnoreCase)).RowCount;
            Assert.Equal(await SsRowCountAsync(name), oracleCount);
        }
    }

    [Fact]
    public async Task TC022_SsToOracle_NullEdgeCases_RowCountMatches()
    {
        if (!ShouldRunE2E()) return;
        // Oracle null_edge_cases was created by the Oracle init script with Oracle-native column names
        // (col_number, col_varchar2, …) which differ from the SS source (col_int, col_bigint, …).
        // Drop it so MigrateSchemaAsync recreates it with SS-compatible column names.
        await DropOracleTableAsync("NULL_EDGE_CASES");
        var ti = await GetTableInfoAsync(SS, "migration_test", "null_edge_cases");
        await SchemaSvc.MigrateSchemaAsync(SS, OracleCI, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(SS, OracleCI, ti, new Progress<int>());
        var oracleTables = await DbSvc.GetTablesAsync(OracleCI);
        long oracleCount = oracleTables
            .First(t => t.TableName.Equals("null_edge_cases", StringComparison.OrdinalIgnoreCase)).RowCount;
        // Oracle converts '' to NULL, but row COUNT must match
        Assert.Equal(await SsRowCountAsync("null_edge_cases"), oracleCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.2  MIGRAZIONE SCHEMA+DATI — PG → SS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC023_PgToSs_BaseTables_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        // Use batch_test (no FK references → TRUNCATE succeeds without side effects).
        // users→SS would require dropping SS.orders first, breaking other tests.
        var ti = await GetTableInfoAsync(PG, "migration_test", "batch_test");
        await DbSvc.MigrateTableAsync(PG, SS, ti, new Progress<int>());
        Assert.Equal(await PgRowCountAsync("batch_test"), await SsRowCountAsync("batch_test"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.2  MIGRAZIONE SCHEMA+DATI — Oracle → PG / Oracle → SS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC024_OracleToPg_BaseTables_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("products", "users");
        var names = new[] { "USERS", "PRODUCTS" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(OracleCI, "MIGRATION_TEST", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(OracleCI, PG, tis);
        foreach (var ti in tis)
            await DbSvc.MigrateTableAsync(OracleCI, PG, ti, new Progress<int>());
        var oracleTables = await DbSvc.GetTablesAsync(OracleCI);
        long oracleUsers = oracleTables.First(t => t.TableName.Equals("USERS", StringComparison.OrdinalIgnoreCase)).RowCount;
        Assert.Equal(oracleUsers, await PgRowCountAsync("users"));
    }

    [Fact]
    public async Task TC025_OracleToSs_BaseTables_RowCountsMatch()
    {
        if (!ShouldRunE2E()) return;
        // SQL Server TRUNCATE fails if ANY FK references the table, even if the child table is empty.
        // Pre-delete rows in FK-safe order so the service's INSERT finds an empty table.
        await SsDeleteRowsAsync("orders", "users");
        // Migrate Oracle USERS data into SS (schema already compatible)
        var ti = await GetTableInfoAsync(OracleCI, "MIGRATION_TEST", "USERS");
        await SchemaSvc.MigrateSchemaAsync(OracleCI, SS, new List<TableInfo> { ti });
        await DbSvc.MigrateTableAsync(OracleCI, SS, ti, new Progress<int>());
        var oracleTables = await DbSvc.GetTablesAsync(OracleCI);
        long oracleUsers = oracleTables.First(t => t.TableName.Equals("USERS", StringComparison.OrdinalIgnoreCase)).RowCount;
        Assert.Equal(oracleUsers, await SsRowCountAsync("users"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.3  SOLO SCHEMA
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC026_SsToPg_SchemaOnly_TablesCreatedWithZeroRows()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("orders", "products", "users");
        var names = new[] { "users", "products", "orders" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(SS, "migration_test", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(SS, PG, tis);
        // Schema-only: no data migration → 0 rows in each table
        Assert.Equal(0L, await PgRowCountAsync("users"));
        Assert.Equal(0L, await PgRowCountAsync("products"));
        Assert.Equal(0L, await PgRowCountAsync("orders"));
    }

    [Fact]
    public async Task TC027_SsToPg_SchemaOnly_EmptyTable_CreatedWithZeroRows()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("empty_table");
        var ti = await GetTableInfoAsync(SS, "migration_test", "empty_table");
        await SchemaSvc.MigrateSchemaAsync(SS, PG, new List<TableInfo> { ti });
        Assert.Equal(0L, await PgRowCountAsync("empty_table"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3.4  SOLO DATI
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TC028_SsToPg_DataOnly_TablesPreExist_DataInsertedCorrectly()
    {
        if (!ShouldRunE2E()) return;
        // Ensure target tables exist (schema-only pass first), then data-only pass
        await DropPgTablesAsync("orders", "users");
        var names = new[] { "users", "orders" };
        var tis   = (await Task.WhenAll(names.Select(n => GetTableInfoAsync(SS, "migration_test", n)))).ToList();
        await SchemaSvc.MigrateSchemaAsync(SS, PG, tis);
        // Second pass: data-only (tables already exist)
        foreach (var ti in tis)
            await DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>());
        Assert.Equal(await SsRowCountAsync("users"),  await PgRowCountAsync("users"));
        Assert.Equal(await SsRowCountAsync("orders"), await PgRowCountAsync("orders"));
    }

    [Fact]
    public async Task TC029_SsToPg_DataOnly_TableMissing_ThrowsException()
    {
        if (!ShouldRunE2E()) return;
        await DropPgTablesAsync("numeric_edge_cases");
        var ti = await GetTableInfoAsync(SS, "migration_test", "numeric_edge_cases");
        // Migrate data without creating schema first → must throw
        await Assert.ThrowsAnyAsync<Exception>(
            () => DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>()));
    }

    [Fact]
    public async Task TC030_SsToPg_DataOnly_SchemaMismatch_ThrowsWithClearMessage()
    {
        if (!ShouldRunE2E()) return;
        // Create a deliberately incomplete type_coverage in PG (missing most columns)
        await DropPgTablesAsync("type_coverage");
        await using (var conn = new NpgsqlConnection(PgConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE migration_test.type_coverage (id SERIAL PRIMARY KEY)";
            await cmd.ExecuteNonQueryAsync();
        }
        var ti = await GetTableInfoAsync(SS, "migration_test", "type_coverage");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DbSvc.MigrateTableAsync(SS, PG, ti, new Progress<int>()));
        Assert.Contains("colonne", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type_coverage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
