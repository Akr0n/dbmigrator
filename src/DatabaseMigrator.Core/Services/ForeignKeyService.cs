using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// What to do around a data load so FOREIGN KEYs on the target do not reject rows: the order to load tables in,
/// and a <see cref="CompleteAsync"/> step that must always run afterwards (success or failure).
/// </summary>
public sealed class DataLoadPlan
{
    private readonly Func<bool, Task<IReadOnlyList<string>>>? _complete;
    private bool _completed;

    internal DataLoadPlan(IReadOnlyList<TableInfo> orderedTables, bool hadCycles, int disabledForeignKeyCount,
        Func<bool, Task<IReadOnlyList<string>>>? complete)
    {
        OrderedTables = orderedTables;
        HadCycles = hadCycles;
        DisabledForeignKeyCount = disabledForeignKeyCount;
        _complete = complete;
    }

    /// <summary>Tables in load order: parents before the tables that reference them.</summary>
    public IReadOnlyList<TableInfo> OrderedTables { get; }

    /// <summary>True when FOREIGN KEYs form a cycle among the tables, so a strict order does not exist.</summary>
    public bool HadCycles { get; }

    /// <summary>How many target FOREIGN KEYs were switched off for the load.</summary>
    public int DisabledForeignKeyCount { get; }

    /// <summary>Restores what the plan changed on the target. Returns warnings for the user; safe to call twice.</summary>
    public async Task<IReadOnlyList<string>> CompleteAsync(bool succeeded)
    {
        if (_completed || _complete == null)
            return Array.Empty<string>();
        _completed = true;
        return await _complete(succeeded);
    }
}

/// <summary>
/// Makes the data phase work against a target that already enforces FOREIGN KEYs (typically tables created by the
/// application itself, not by this tool): loads parents before children and, on SQL Server, switches the keys off for
/// the duration of the load. A load that walks tables alphabetically otherwise inserts a child before its parent, or a
/// self-referencing row before the row it points to in a later batch, and the target rejects it.
/// </summary>
public class ForeignKeyService : DatabaseServiceBase
{
    /// <summary>Reads FOREIGN KEYs whose child table is one of <paramref name="tables"/>.</summary>
    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(ConnectionInfo connection, IReadOnlyCollection<TableInfo> tables)
    {
        var selected = new HashSet<(string, string)>(tables.Select(t => Key(t.Schema, t.TableName)));
        var foreignKeys = new List<ForeignKeyInfo>();

        using var dbConnection = CreateConnection(connection);
        await ExecuteWithRetryAsync(() => dbConnection.OpenAsync(), "GetForeignKeysAsync.Open");

        using var command = dbConnection.CreateCommand();
        command.CommandText = ForeignKeyQuery(connection.DatabaseType);
        command.CommandTimeout = _commandTimeoutSeconds;

        using var reader = await ExecuteWithRetryAsync(() => command.ExecuteReaderAsync(), "GetForeignKeysAsync.Read");
        while (await reader.ReadAsync())
        {
            var foreignKey = new ForeignKeyInfo(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                Convert.ToInt32(reader.GetValue(5)) != 0);
            if (selected.Contains(Key(foreignKey.ChildSchema, foreignKey.ChildTable)))
                foreignKeys.Add(foreignKey);
        }

        return foreignKeys;
    }

    /// <summary>Prepares the target for loading <paramref name="tables"/>; see <see cref="DataLoadPlan"/>.</summary>
    public async Task<DataLoadPlan> PrepareDataLoadAsync(ConnectionInfo target, IReadOnlyList<TableInfo> tables)
    {
        List<ForeignKeyInfo> foreignKeys;
        try
        {
            foreignKeys = await GetForeignKeysAsync(target, tables);
        }
        catch (Exception ex)
        {
            // Never stop a migration because the catalog could not be read (e.g. no permission): load as before.
            Log($"[ForeignKeyService] Could not read foreign keys from the target, loading tables in the given order: {ex.Message}");
            return new DataLoadPlan(tables.ToList(), false, 0, null);
        }

        var order = TableDependencyOrderer.Order(tables, foreignKeys);
        Log($"[ForeignKeyService] {foreignKeys.Count} foreign key(s) on the selected tables; load order: " +
            string.Join(", ", order.Tables.Select(t => $"{t.Schema}.{t.TableName}")));
        if (order.HadCycles)
            Log("[ForeignKeyService] Foreign keys form a cycle among the selected tables; no strict load order exists.");

        // SQL Server can switch a key off and back on (NOCHECK / CHECK), which also covers cycles and self-references.
        // PostgreSQL and Oracle rely on the load order alone.
        if (target.DatabaseType != DatabaseType.SqlServer)
            return new DataLoadPlan(order.Tables, order.HadCycles, 0, null);

        var disabled = await DisableForeignKeysAsync(target, foreignKeys.Where(fk => !fk.IsDisabled));
        return new DataLoadPlan(order.Tables, order.HadCycles, disabled.Count,
            disabled.Count == 0 ? null : succeeded => EnableForeignKeysAsync(target, disabled, succeeded));
    }

    private async Task<List<ForeignKeyInfo>> DisableForeignKeysAsync(ConnectionInfo target, IEnumerable<ForeignKeyInfo> foreignKeys)
    {
        var disabled = new List<ForeignKeyInfo>();
        using var connection = CreateConnection(target);
        await ExecuteWithRetryAsync(() => connection.OpenAsync(), "DisableForeignKeysAsync.Open");

        foreach (var foreignKey in foreignKeys)
        {
            try
            {
                // Logged BEFORE the change: if the app is closed or the connection drops mid-load, the log still says
                // exactly which key was switched off and how to put it back.
                Log($"[ForeignKeyService] Switching off {foreignKey.ConstraintName}. " +
                    $"To restore by hand: ALTER TABLE {TableOf(foreignKey)} WITH CHECK CHECK CONSTRAINT {NameOf(foreignKey)}");
                await ExecuteAsync(connection, $"ALTER TABLE {TableOf(foreignKey)} NOCHECK CONSTRAINT {NameOf(foreignKey)}",
                    "DisableForeignKeysAsync.Alter");
                disabled.Add(foreignKey);
            }
            catch (Exception ex)
            {
                // The load can still succeed on the load order alone, so do not abort here.
                Log($"[ForeignKeyService] Could not switch off {foreignKey.ConstraintName} on {TableOf(foreignKey)}: {ex.Message}");
            }
        }

        Log($"[ForeignKeyService] Switched off {disabled.Count} foreign key(s) for the load");
        return disabled;
    }

    private async Task<IReadOnlyList<string>> EnableForeignKeysAsync(ConnectionInfo target, List<ForeignKeyInfo> disabled, bool succeeded)
    {
        var warnings = await EnableForeignKeysCoreAsync(target, disabled, succeeded);
        // Logged on every path, including the early return when the target cannot be reached.
        foreach (var warning in warnings)
            Log($"[ForeignKeyService] WARNING: {warning}");
        return warnings;
    }

    private async Task<IReadOnlyList<string>> EnableForeignKeysCoreAsync(ConnectionInfo target, List<ForeignKeyInfo> disabled, bool succeeded)
    {
        var warnings = new List<string>();
        using var connection = CreateConnection(target);
        try
        {
            await ExecuteWithRetryAsync(() => connection.OpenAsync(), "EnableForeignKeysAsync.Open");
        }
        catch (Exception ex)
        {
            warnings.AddRange(disabled.Select(fk =>
                $"FOREIGN KEY {fk.ConstraintName} on {TableOf(fk)} is still switched off ({ex.Message}). " +
                $"Run: ALTER TABLE {TableOf(fk)} WITH CHECK CHECK CONSTRAINT {NameOf(fk)}"));
            return warnings;
        }

        foreach (var foreignKey in disabled)
        {
            string table = TableOf(foreignKey);
            string name = NameOf(foreignKey);

            if (succeeded)
            {
                try
                {
                    // Re-validates the loaded rows, which also makes the key trusted by the optimizer again.
                    await ExecuteAsync(connection, $"ALTER TABLE {table} WITH CHECK CHECK CONSTRAINT {name}", "EnableForeignKeysAsync.Check");
                    continue;
                }
                catch (Exception ex)
                {
                    warnings.Add($"FOREIGN KEY {foreignKey.ConstraintName} on {table}: rows that were migrated violate it " +
                                 $"({ex.Message}). It was switched back on without re-checking them: either the source holds orphan rows " +
                                 "or a table it references was not migrated.");
                }
            }

            try
            {
                await ExecuteAsync(connection, $"ALTER TABLE {table} CHECK CONSTRAINT {name}", "EnableForeignKeysAsync.Enable");
            }
            catch (Exception ex)
            {
                warnings.Add($"FOREIGN KEY {foreignKey.ConstraintName} on {table} could NOT be switched back on ({ex.Message}). " +
                             $"Run: ALTER TABLE {table} CHECK CONSTRAINT {name}");
            }
        }

        // After a failed load the keys are switched on without scanning the rows already loaded (cheap and safe, but
        // SQL Server then treats them as not trusted). Each key that could not be re-enabled has its own warning above.
        if (!succeeded && warnings.Count < disabled.Count)
            warnings.Add($"{disabled.Count - warnings.Count} FOREIGN KEY(s) were switched back on without re-checking the rows already loaded, " +
                         "so SQL Server treats them as not trusted. To validate them: ALTER TABLE <table> WITH CHECK CHECK CONSTRAINT <key>.");

        return warnings;
    }

    private async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql, string operationName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _commandTimeoutSeconds;
        await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), operationName);
    }

    private static string TableOf(ForeignKeyInfo foreignKey) =>
        FormatTableName(DatabaseType.SqlServer, foreignKey.ChildSchema, foreignKey.ChildTable);

    private static string NameOf(ForeignKeyInfo foreignKey) =>
        $"[{EscapeSqlServerIdentifier(foreignKey.ConstraintName)}]";

    private static (string, string) Key(string schema, string table) =>
        (schema.ToUpperInvariant(), table.ToUpperInvariant());

    // Columns: constraint name, child schema, child table, parent schema, parent table, 1 when disabled.
    private static string ForeignKeyQuery(DatabaseType type) => type switch
    {
        DatabaseType.SqlServer => @"
            SELECT fk.name, cs.name, ct.name, ps.name, pt.name, fk.is_disabled
            FROM sys.foreign_keys fk
            JOIN sys.tables ct ON ct.object_id = fk.parent_object_id
            JOIN sys.schemas cs ON cs.schema_id = ct.schema_id
            JOIN sys.tables pt ON pt.object_id = fk.referenced_object_id
            JOIN sys.schemas ps ON ps.schema_id = pt.schema_id",

        DatabaseType.PostgreSQL => @"
            SELECT c.conname, cn.nspname, ct.relname, pn.nspname, pt.relname, 0
            FROM pg_constraint c
            JOIN pg_class ct ON ct.oid = c.conrelid
            JOIN pg_namespace cn ON cn.oid = ct.relnamespace
            JOIN pg_class pt ON pt.oid = c.confrelid
            JOIN pg_namespace pn ON pn.oid = pt.relnamespace
            WHERE c.contype = 'f'",

        DatabaseType.Oracle => @"
            SELECT c.constraint_name, c.owner, c.table_name, p.owner, p.table_name,
                   CASE c.status WHEN 'DISABLED' THEN 1 ELSE 0 END
            FROM all_constraints c
            JOIN all_constraints p ON p.owner = c.r_owner AND p.constraint_name = c.r_constraint_name
            WHERE c.constraint_type = 'R'",

        _ => throw new NotSupportedException($"Database type {type} not supported")
    };
}
