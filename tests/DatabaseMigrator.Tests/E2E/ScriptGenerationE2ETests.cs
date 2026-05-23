using System.Diagnostics;
using System.Text;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests.E2E;

/// <summary>
/// Test end-to-end della funzione "Genera Script" (<see cref="ScriptGenerationService"/>).
///
/// Strategia di verifica:
///  - <b>Round-trip nello stesso dialetto</b>: si genera lo script per un sottoinsieme di
///    oggetti del database sorgente, lo si <i>esegue realmente</i> tramite il client SQL
///    nativo (psql / sqlcmd / sqlplus, invocato con <c>docker exec</c> sui container E2E)
///    e si verifica che schema e dati vengano ricreati fedelmente. È la prova che lo
///    script prodotto è valido e ri-eseguibile.
///  - <b>Cross-dialetto</b>: si verifica che la generazione traduca il DDL nel dialetto
///    di destinazione richiesto (non si esegue: la traduzione cross-dialetto di alcuni
///    costrutti ha limiti documentati).
///  - <b>Opzioni</b>: si verifica il rispetto dei flag IncludeSchema / IncludeData.
///
/// I test girano solo quando la variabile d'ambiente <c>DBMIGRATOR_RUN_E2E=true</c> è
/// impostata (vedi <c>scripts/run-e2e-matrix.ps1</c>), che presuppone i container Docker
/// di docker-compose.yml attivi e sani. I container nativi contengono i client SQL usati
/// per eseguire gli script generati.
///
/// La classe crea da sé i propri oggetti fixture (vista, indice, sequenza) in modo
/// idempotente: non modifica gli script di init condivisi.
/// </summary>
public class ScriptGenerationE2ETests
{
    // Tabelle presenti in tutti e tre i database di test, con dipendenze FK chiuse
    // (orders -> users, orders -> products) e senza colonne con nomi riservati.
    private static readonly string[] CoreTables = { "users", "products", "orders" };

    // Oggetti fixture creati dai test (idempotenti).
    private const string FixtureView = "vw_user_orders";
    private const string FixtureIndex = "idx_e2e_orders_user";
    private const string FixtureSequence = "seq_e2e_demo";

    public static IEnumerable<object[]> AllDialects()
    {
        yield return new object[] { DatabaseType.SqlServer };
        yield return new object[] { DatabaseType.PostgreSQL };
        yield return new object[] { DatabaseType.Oracle };
    }

    public static IEnumerable<object[]> CrossDialectPairs()
    {
        yield return new object[] { DatabaseType.SqlServer, DatabaseType.PostgreSQL };
        yield return new object[] { DatabaseType.PostgreSQL, DatabaseType.Oracle };
        yield return new object[] { DatabaseType.Oracle, DatabaseType.SqlServer };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 — round-trip nello stesso dialetto: lo script generato è ri-eseguibile
    // ─────────────────────────────────────────────────────────────────────────

    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(AllDialects))]
    public async Task GenerateScript_SameDialect_RoundTripsSchemaAndData(DatabaseType dialect)
    {
        if (!ShouldRunE2E())
        {
            return;
        }

        var source = BuildConnectionInfo(dialect);
        await EnsureFixtureObjectsAsync(dialect);

        var service = new ScriptGenerationService();

        // 1. Scoperta oggetti e selezione del sottoinsieme da esportare.
        var before = await service.GetDatabaseObjectsAsync(source);
        var selected = SelectRoundTripObjects(before);

        Assert.Equal(3, selected.Count(o => o.ObjectType == DatabaseObjectType.Table));
        Assert.Contains(selected, o => o.ObjectType == DatabaseObjectType.View);
        Assert.Contains(selected, o => o.ObjectType == DatabaseObjectType.Index);
        Assert.Contains(selected, o => o.ObjectType == DatabaseObjectType.Sequence);

        var originalRowCounts = selected
            .Where(o => o.ObjectType == DatabaseObjectType.Table)
            .ToDictionary(o => o.Name.ToLowerInvariant(), o => o.RowCount);

        // 2. Generazione dello script nello stesso dialetto (schema + dati + DROP).
        var options = new ScriptGenerationOptions
        {
            TargetDialect = dialect,
            IncludeSchema = true,
            IncludeData = true,
            IncludeDropStatements = true
        };
        var writer = new StringWriter();
        await service.GenerateScriptAsync(source, selected, options, writer);
        string script = writer.ToString();

        Assert.Contains("CREATE TABLE", script);
        Assert.Contains("INSERT INTO", script);
        Assert.Contains("CREATE INDEX", script);
        Assert.Contains("CREATE SEQUENCE", script);
        Assert.Contains("VIEW", script.ToUpperInvariant());

        // 3. Esecuzione reale dello script tramite il client SQL nativo.
        var (exitCode, output) = await RunViaClientAsync(dialect, script);
        Assert.True(exitCode == 0,
            $"Lo script generato per {dialect} non è stato eseguito correttamente " +
            $"(exit code {exitCode}).\n--- Output del client SQL ---\n{output}");

        // 4. Ri-scoperta e verifica che schema e dati siano stati ricreati fedelmente.
        var after = await service.GetDatabaseObjectsAsync(source);
        foreach (var (table, expectedRows) in originalRowCounts)
        {
            var recreated = after.FirstOrDefault(o => o.ObjectType == DatabaseObjectType.Table &&
                string.Equals(o.Name, table, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(recreated);
            Assert.Equal(expectedRows, recreated!.RowCount);
        }

        Assert.Contains(after, o => o.ObjectType == DatabaseObjectType.View &&
            string.Equals(o.Name, FixtureView, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(after, o => o.ObjectType == DatabaseObjectType.Index &&
            string.Equals(o.Name, FixtureIndex, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(after, o => o.ObjectType == DatabaseObjectType.Sequence &&
            string.Equals(o.Name, FixtureSequence, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 — cross-dialetto: la generazione traduce nel dialetto di destinazione
    // ─────────────────────────────────────────────────────────────────────────

    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(CrossDialectPairs))]
    public async Task GenerateScript_CrossDialect_ProducesTargetDialectScript(
        DatabaseType sourceType, DatabaseType targetType)
    {
        if (!ShouldRunE2E())
        {
            return;
        }

        var source = BuildConnectionInfo(sourceType);
        var service = new ScriptGenerationService();

        var objects = await service.GetDatabaseObjectsAsync(source);
        var tables = objects
            .Where(o => o.ObjectType == DatabaseObjectType.Table &&
                        CoreTables.Contains(o.Name.ToLowerInvariant()))
            .ToList();
        Assert.Equal(3, tables.Count);
        foreach (var t in tables)
        {
            t.IsSelected = true;
        }

        var options = new ScriptGenerationOptions
        {
            TargetDialect = targetType,
            IncludeSchema = true,
            IncludeData = true
        };
        var writer = new StringWriter();
        await service.GenerateScriptAsync(source, tables, options, writer);
        string script = writer.ToString();

        Assert.Contains("CREATE TABLE", script);
        Assert.Contains("INSERT INTO", script);
        // Marcatore inequivocabile del dialetto di destinazione richiesto. Il confronto è
        // case-insensitive: la generazione conserva il case degli identificatori della
        // sorgente (es. Oracle li mantiene in maiuscolo).
        Assert.Contains(ExpectedDialectMarker(targetType), script, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 — i flag IncludeSchema / IncludeData sono rispettati
    // ─────────────────────────────────────────────────────────────────────────

    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(AllDialects))]
    public async Task GenerateScript_HonoursIncludeFlags(DatabaseType dialect)
    {
        if (!ShouldRunE2E())
        {
            return;
        }

        var source = BuildConnectionInfo(dialect);
        var service = new ScriptGenerationService();

        var objects = await service.GetDatabaseObjectsAsync(source);
        var usersTable = objects.First(o => o.ObjectType == DatabaseObjectType.Table &&
            string.Equals(o.Name, "users", StringComparison.OrdinalIgnoreCase));
        usersTable.IsSelected = true;
        var selected = new[] { usersTable };

        // Solo schema: DDL presente, nessun INSERT.
        var schemaOnly = new StringWriter();
        await service.GenerateScriptAsync(source, selected,
            new ScriptGenerationOptions { TargetDialect = dialect, IncludeSchema = true, IncludeData = false },
            schemaOnly);
        string schemaOnlyText = schemaOnly.ToString();
        Assert.Contains("CREATE TABLE", schemaOnlyText);
        Assert.DoesNotContain("INSERT INTO", schemaOnlyText);

        // Solo dati: INSERT presenti, nessun CREATE TABLE.
        var dataOnly = new StringWriter();
        await service.GenerateScriptAsync(source, selected,
            new ScriptGenerationOptions { TargetDialect = dialect, IncludeSchema = false, IncludeData = true },
            dataOnly);
        string dataOnlyText = dataOnly.ToString();
        Assert.Contains("INSERT INTO", dataOnlyText);
        Assert.DoesNotContain("CREATE TABLE", dataOnlyText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

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
            _ => throw new NotSupportedException($"Database type {databaseType} non supportato")
        };
    }

    /// <summary>
    /// Seleziona gli oggetti per il round-trip: le 3 tabelle core, la vista, l'indice e la
    /// sequenza fixture. Per PostgreSQL include anche le sequenze implicite delle colonne
    /// SERIAL (<c>&lt;tabella&gt;_id_seq</c>), da cui dipende il DEFAULT delle tabelle.
    /// </summary>
    private static List<DatabaseObject> SelectRoundTripObjects(IEnumerable<DatabaseObject> all)
    {
        var pool = all.ToList();
        var selected = new List<DatabaseObject>();

        void Pick(DatabaseObjectType type, string name)
        {
            var obj = pool.FirstOrDefault(o => o.ObjectType == type &&
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (obj != null)
            {
                obj.IsSelected = true;
                selected.Add(obj);
            }
        }

        foreach (var table in CoreTables)
        {
            Pick(DatabaseObjectType.Table, table);
        }
        Pick(DatabaseObjectType.View, FixtureView);
        Pick(DatabaseObjectType.Index, FixtureIndex);
        Pick(DatabaseObjectType.Sequence, FixtureSequence);

        // PostgreSQL: le colonne SERIAL dipendono dalle sequenze implicite.
        foreach (var table in CoreTables)
        {
            Pick(DatabaseObjectType.Sequence, table + "_id_seq");
        }

        return selected;
    }

    private static string ExpectedDialectMarker(DatabaseType targetDialect) => targetDialect switch
    {
        // Riferimento tabella tra parentesi quadre (T-SQL).
        DatabaseType.SqlServer => "[users]",
        // Identificatori tra virgolette doppie e in minuscolo (PostgreSQL).
        DatabaseType.PostgreSQL => "\"users\"",
        // Identificatori in maiuscolo senza virgolette (Oracle).
        DatabaseType.Oracle => "MIGRATION_TEST.USERS",
        _ => throw new NotSupportedException()
    };

    /// <summary>
    /// Crea, in modo idempotente, gli oggetti fixture (vista, indice, sequenza) nello schema
    /// di test del database indicato, usando il client SQL nativo.
    /// </summary>
    private static async Task EnsureFixtureObjectsAsync(DatabaseType dialect)
    {
        switch (dialect)
        {
            case DatabaseType.PostgreSQL:
                await RunSqlAndAssertAsync(dialect, """
                    CREATE OR REPLACE VIEW migration_test.vw_user_orders AS
                      SELECT u.id AS user_id, u.username, COUNT(o.id) AS order_count
                      FROM migration_test.users u
                      LEFT JOIN migration_test.orders o ON o.user_id = u.id
                      GROUP BY u.id, u.username;
                    CREATE INDEX IF NOT EXISTS idx_e2e_orders_user
                      ON migration_test.orders (user_id);
                    CREATE SEQUENCE IF NOT EXISTS migration_test.seq_e2e_demo
                      START WITH 1000 INCREMENT BY 5;
                    """);
                break;

            case DatabaseType.SqlServer:
                await RunSqlAndAssertAsync(dialect, """
                    CREATE OR ALTER VIEW migration_test.vw_user_orders AS
                      SELECT u.id AS user_id, u.username, COUNT(o.id) AS order_count
                      FROM migration_test.users u
                      LEFT JOIN migration_test.orders o ON o.user_id = u.id
                      GROUP BY u.id, u.username;
                    GO
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_e2e_orders_user')
                      CREATE INDEX idx_e2e_orders_user ON migration_test.orders (user_id);
                    GO
                    IF NOT EXISTS (SELECT 1 FROM sys.sequences seq
                                   JOIN sys.schemas s ON seq.schema_id = s.schema_id
                                   WHERE s.name = 'migration_test' AND seq.name = 'seq_e2e_demo')
                      CREATE SEQUENCE migration_test.seq_e2e_demo AS BIGINT START WITH 1000 INCREMENT BY 5;
                    GO
                    """);
                break;

            case DatabaseType.Oracle:
                // In Oracle creare una vista richiede il privilegio CREATE VIEW, non incluso
                // nel ruolo RESOURCE: lo si concede con un'utenza amministrativa.
                await RunSqlAndAssertAsync(dialect,
                    "GRANT CREATE VIEW TO migration_test;", asSystem: true);
                await RunSqlAndAssertAsync(dialect, """
                    CREATE OR REPLACE VIEW vw_user_orders AS
                      SELECT u.id AS user_id, u.username, COUNT(o.id) AS order_count
                      FROM users u LEFT JOIN orders o ON o.user_id = u.id
                      GROUP BY u.id, u.username;
                    BEGIN EXECUTE IMMEDIATE 'CREATE INDEX idx_e2e_orders_user ON orders (user_id)';
                    EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;
                    /
                    BEGIN EXECUTE IMMEDIATE 'CREATE SEQUENCE seq_e2e_demo START WITH 1000 INCREMENT BY 5';
                    EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;
                    /
                    """);
                break;
        }
    }

    private static async Task RunSqlAndAssertAsync(DatabaseType dialect, string sql, bool asSystem = false)
    {
        var (exitCode, output) = await RunViaClientAsync(dialect, sql, asSystem);
        Assert.True(exitCode == 0,
            $"Preparazione fixture per {dialect} fallita (exit code {exitCode}).\n{output}");
    }

    /// <summary>
    /// Esegue uno script SQL nel database indicato tramite il client SQL nativo del
    /// rispettivo container Docker (psql / sqlcmd / sqlplus). Restituisce exit code e output.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunViaClientAsync(
        DatabaseType dialect, string sql, bool asSystem = false)
    {
        string container;
        string prologue = string.Empty;
        string epilogue = string.Empty;
        var args = new List<string> { "exec", "-i" };

        switch (dialect)
        {
            case DatabaseType.PostgreSQL:
                container = "dbmigrator-postgres";
                args.Add(container);
                args.AddRange(new[] { "psql", "-U", "pguser", "-d", "testdb", "-v", "ON_ERROR_STOP=1", "-q" });
                break;

            case DatabaseType.SqlServer:
                container = "dbmigrator-sqlserver";
                args.Add(container);
                // -b: exit code diverso da 0 in caso di errore; -f 65001: input UTF-8.
                args.AddRange(new[]
                {
                    "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
                    "-P", "SqlServer@123", "-C", "-b", "-d", "TestDB", "-f", "65001"
                });
                break;

            case DatabaseType.Oracle:
                container = "dbmigrator-oracle";
                args.Add(container);
                string credentials = asSystem
                    ? "system/oraclepass123@//localhost:1521/FREEPDB1"
                    : "migration_test/oraclepass123@//localhost:1521/FREEPDB1";
                args.AddRange(new[] { "sqlplus", "-S", credentials });
                // Fa terminare sqlplus con exit code di errore al primo errore SQL.
                prologue = "WHENEVER SQLERROR EXIT FAILURE\n";
                epilogue = "\nEXIT;\n";
                break;

            default:
                throw new NotSupportedException($"Database type {dialect} non supportato");
        }

        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare il processo 'docker'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(prologue + sql + epilogue);
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        string output = (await stdoutTask) + (await stderrTask);
        return (process.ExitCode, output);
    }
}
