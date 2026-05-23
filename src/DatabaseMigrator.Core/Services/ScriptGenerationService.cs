using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// Genera uno script .sql (DDL + dati) per gli oggetti selezionati di un database sorgente,
/// in modo simile alla funzione "Genera Script" di SQL Server Management Studio.
/// L'utente sceglie il dialetto SQL di destinazione tra SQL Server, PostgreSQL e Oracle.
/// </summary>
public class ScriptGenerationService : DatabaseServiceBase
{
    // Schema/owner Oracle da escludere perché di sistema (allineato a DatabaseService.GetTablesAsync).
    private const string OracleSystemOwners =
        "'SYS','SYSTEM','XDB','ANONYMOUS','APEX_030200','APEX_040200','APEX_050000','APEX_050100'," +
        "'APEX_180200','APEX_190100','APEX_200100','APEX_210100','APEX_220100','AUDSYS','CTXSYS'," +
        "'DBSNMP','DVSYS','DVF','FLOWS_FILES','GSMADMIN_INTERNAL','GSMCATUSER','GSMROOTUSER'," +
        "'LBACSYS','MDDATA','MDSYS','OJVMSYS','OLAPSYS','ORACLE_OCM','ORDDATA','ORDPLUGINS','ORDSYS'," +
        "'OUTLN','OWBSYS','OWBSYS_AUDIT','REMOTE_SCHEDULER_AGENT','SI_INFORMTN_SCHEMA','SYS$UMF'," +
        "'SYSBACKUP','SYSDG','SYSKM','SYSRAC','WMSYS','XS$NULL'";

    private readonly SchemaMigrationService _schemaService = new();
    private readonly DatabaseService _databaseService = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Elenco oggetti
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recupera tutti gli oggetti del database sorgente esportabili come script
    /// (tabelle, viste, stored procedure, funzioni, trigger, sequenze, indici).
    /// </summary>
    public async Task<List<DatabaseObject>> GetDatabaseObjectsAsync(
        ConnectionInfo source, CancellationToken cancellationToken = default)
    {
        var result = new List<DatabaseObject>();

        // Le tabelle (con conteggio righe) sono recuperate dal servizio esistente.
        var tables = await _databaseService.GetTablesAsync(source);
        foreach (var t in tables)
        {
            result.Add(new DatabaseObject
            {
                ObjectType = DatabaseObjectType.Table,
                Schema = t.Schema,
                Name = t.TableName,
                RowCount = t.RowCount
            });
        }

        using (var connection = CreateConnection(source))
        {
            await ExecuteWithRetryAsync(() => connection.OpenAsync(cancellationToken), "GetDatabaseObjects.Open");
            await AddNonTableObjectsAsync(connection, source.DatabaseType, result, cancellationToken);
        }

        return result
            .OrderBy(o => o.ObjectType)
            .ThenBy(o => o.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task AddNonTableObjectsAsync(DbConnection connection, DatabaseType dbType,
        List<DatabaseObject> target, CancellationToken ct)
    {
        var specs = GetObjectListQueries(dbType);
        foreach (var spec in specs)
        {
            try
            {
                await foreach (var obj in QueryObjectsAsync(connection, spec, ct))
                    target.Add(obj);
            }
            catch (Exception ex)
            {
                // Una singola query di introspezione non disponibile (permessi, versione DB)
                // non deve impedire il caricamento degli altri tipi di oggetto.
                Log($"[ScriptGeneration] Impossibile elencare {spec.Type}: {ex.Message}");
            }
        }
    }

    private sealed record ObjectListQuery(DatabaseObjectType Type, string Sql, bool HasParent);

    private static List<ObjectListQuery> GetObjectListQueries(DatabaseType dbType) => dbType switch
    {
        DatabaseType.SqlServer => new()
        {
            new(DatabaseObjectType.View,
                @"SELECT s.name, v.name, '' FROM sys.views v
                  JOIN sys.schemas s ON v.schema_id = s.schema_id
                  WHERE v.is_ms_shipped = 0", false),
            new(DatabaseObjectType.StoredProcedure,
                @"SELECT s.name, p.name, '' FROM sys.procedures p
                  JOIN sys.schemas s ON p.schema_id = s.schema_id
                  WHERE p.is_ms_shipped = 0", false),
            new(DatabaseObjectType.Function,
                @"SELECT s.name, o.name, '' FROM sys.objects o
                  JOIN sys.schemas s ON o.schema_id = s.schema_id
                  WHERE o.type IN ('FN','IF','TF','FS','FT') AND o.is_ms_shipped = 0", false),
            new(DatabaseObjectType.Trigger,
                @"SELECT s.name, tr.name, t.name FROM sys.triggers tr
                  JOIN sys.tables t ON tr.parent_id = t.object_id
                  JOIN sys.schemas s ON t.schema_id = s.schema_id
                  WHERE tr.is_ms_shipped = 0", true),
            new(DatabaseObjectType.Sequence,
                @"SELECT s.name, seq.name, '' FROM sys.sequences seq
                  JOIN sys.schemas s ON seq.schema_id = s.schema_id", false),
            new(DatabaseObjectType.Index,
                @"SELECT s.name, i.name, t.name FROM sys.indexes i
                  JOIN sys.tables t ON i.object_id = t.object_id
                  JOIN sys.schemas s ON t.schema_id = s.schema_id
                  WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0
                    AND i.type > 0 AND i.name IS NOT NULL AND t.is_ms_shipped = 0", true)
        },
        DatabaseType.PostgreSQL => new()
        {
            new(DatabaseObjectType.View,
                @"SELECT schemaname, viewname, '' FROM pg_views
                  WHERE schemaname NOT IN ('pg_catalog','information_schema')", false),
            new(DatabaseObjectType.StoredProcedure,
                @"SELECT DISTINCT n.nspname, p.proname, '' FROM pg_proc p
                  JOIN pg_namespace n ON p.pronamespace = n.oid
                  WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND p.prokind = 'p'", false),
            new(DatabaseObjectType.Function,
                @"SELECT DISTINCT n.nspname, p.proname, '' FROM pg_proc p
                  JOIN pg_namespace n ON p.pronamespace = n.oid
                  WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND p.prokind = 'f'", false),
            new(DatabaseObjectType.Trigger,
                @"SELECT n.nspname, t.tgname, c.relname FROM pg_trigger t
                  JOIN pg_class c ON t.tgrelid = c.oid
                  JOIN pg_namespace n ON c.relnamespace = n.oid
                  WHERE NOT t.tgisinternal
                    AND n.nspname NOT IN ('pg_catalog','information_schema')", true),
            new(DatabaseObjectType.Sequence,
                @"SELECT schemaname, sequencename, '' FROM pg_sequences
                  WHERE schemaname NOT IN ('pg_catalog','information_schema')", false),
            new(DatabaseObjectType.Index,
                @"SELECT pi.schemaname, pi.indexname, pi.tablename FROM pg_indexes pi
                  WHERE pi.schemaname NOT IN ('pg_catalog','information_schema')
                    AND NOT EXISTS (
                      SELECT 1 FROM pg_constraint c
                      JOIN pg_class ic ON c.conindid = ic.oid
                      JOIN pg_namespace ns ON ic.relnamespace = ns.oid
                      WHERE ic.relname = pi.indexname AND ns.nspname = pi.schemaname)", true)
        },
        DatabaseType.Oracle => new()
        {
            new(DatabaseObjectType.View,
                $@"SELECT owner, view_name, '' FROM all_views WHERE owner NOT IN ({OracleSystemOwners})", false),
            new(DatabaseObjectType.StoredProcedure,
                $@"SELECT owner, object_name, '' FROM all_objects
                   WHERE object_type = 'PROCEDURE' AND owner NOT IN ({OracleSystemOwners})", false),
            new(DatabaseObjectType.Function,
                $@"SELECT owner, object_name, '' FROM all_objects
                   WHERE object_type = 'FUNCTION' AND owner NOT IN ({OracleSystemOwners})", false),
            new(DatabaseObjectType.Trigger,
                $@"SELECT owner, trigger_name, table_name FROM all_triggers
                   WHERE owner NOT IN ({OracleSystemOwners})", true),
            new(DatabaseObjectType.Sequence,
                $@"SELECT sequence_owner, sequence_name, '' FROM all_sequences
                   WHERE sequence_owner NOT IN ({OracleSystemOwners})", false),
            new(DatabaseObjectType.Index,
                $@"SELECT i.owner, i.index_name, i.table_name FROM all_indexes i
                   WHERE i.owner NOT IN ({OracleSystemOwners})
                     AND NOT EXISTS (
                       SELECT 1 FROM all_constraints c
                       WHERE c.owner = i.owner AND c.index_name = i.index_name
                         AND c.constraint_type IN ('P','U'))", true)
        },
        _ => throw new NotSupportedException($"Database type {dbType} non supportato")
    };

    private async IAsyncEnumerable<DatabaseObject> QueryObjectsAsync(DbConnection connection,
        ObjectListQuery spec, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = spec.Sql;
        command.CommandTimeout = _commandTimeoutSeconds;

        using var reader = await ExecuteWithRetryAsync(
            () => command.ExecuteReaderAsync(ct), $"QueryObjects.{spec.Type}");

        while (await reader.ReadAsync(ct))
        {
            yield return new DatabaseObject
            {
                ObjectType = spec.Type,
                Schema = reader[0]?.ToString() ?? string.Empty,
                Name = reader[1]?.ToString() ?? string.Empty,
                ParentName = spec.HasParent ? reader[2]?.ToString() ?? string.Empty : string.Empty
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generazione script
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera lo script .sql per gli oggetti selezionati, scrivendolo in streaming su
    /// <paramref name="output"/>.
    /// </summary>
    public async Task GenerateScriptAsync(
        ConnectionInfo source,
        IReadOnlyList<DatabaseObject> selectedObjects,
        ScriptGenerationOptions options,
        TextWriter output,
        IProgress<ScriptGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (selectedObjects.Count == 0)
            throw new InvalidOperationException("Nessun oggetto selezionato per la generazione dello script.");

        DatabaseType dialect = options.TargetDialect;
        var state = new ScriptGenerationProgress { TotalObjects = selectedObjects.Count };

        List<DatabaseObject> sequences = Filter(selectedObjects, DatabaseObjectType.Sequence);
        List<DatabaseObject> tables = Filter(selectedObjects, DatabaseObjectType.Table);
        List<DatabaseObject> indexes = Filter(selectedObjects, DatabaseObjectType.Index);
        List<DatabaseObject> views = Filter(selectedObjects, DatabaseObjectType.View);
        List<DatabaseObject> routines = selectedObjects
            .Where(o => o.ObjectType is DatabaseObjectType.StoredProcedure or DatabaseObjectType.Function)
            .ToList();
        List<DatabaseObject> triggers = Filter(selectedObjects, DatabaseObjectType.Trigger);

        await WriteHeaderAsync(output, source, options, selectedObjects.Count);

        if (dialect == DatabaseType.SqlServer)
        {
            await output.WriteLineAsync("SET NOCOUNT ON;");
            await output.WriteLineAsync("GO");
            await output.WriteLineAsync();
        }

        using var connection = CreateConnection(source);
        await ExecuteWithRetryAsync(() => connection.OpenAsync(cancellationToken), "GenerateScript.Open");

        // Statement DROP (ordine inverso delle dipendenze).
        if (options.IncludeDropStatements)
        {
            await WriteSectionAsync(output, "DROP DEGLI OGGETTI ESISTENTI");

            // SQL Server non supporta DROP TABLE ... CASCADE: le FOREIGN KEY che referenziano
            // le tabelle vanno rimosse esplicitamente prima dei DROP TABLE.
            // (PostgreSQL usa CASCADE, Oracle usa CASCADE CONSTRAINTS — vedi BuildDropStatement.)
            if (dialect == DatabaseType.SqlServer)
            {
                foreach (var table in tables)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (var fk in await GetForeignKeysAsync(connection, source.DatabaseType, table))
                            await WriteStatementAsync(output, dialect,
                                $"ALTER TABLE {FormatTableName(dialect, table.Schema, table.Name)} " +
                                $"DROP CONSTRAINT IF EXISTS {FormatConstraintName(dialect, fk.Name)}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[ScriptGeneration] FOREIGN KEY non rimosse per {table.QualifiedName}: {ex.Message}");
                    }
                }
            }

            foreach (var o in triggers.Concat(routines).Concat(views).Concat(indexes)
                         .Concat(tables).Concat(sequences))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteStatementAsync(output, dialect, BuildDropStatement(dialect, o));
            }
        }

        // Creazione degli schemi/namespace necessari, per rendere lo script auto-contenuto.
        if (options.IncludeSchema)
            await WriteSchemasAsync(output, dialect, selectedObjects.Select(o => o.Schema));

        if (options.IncludeSchema && sequences.Count > 0)
        {
            await WriteSectionAsync(output, "SEQUENZE");
            foreach (var seq in sequences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, state, seq);
                try
                {
                    var meta = await GetSequenceMetaAsync(connection, source.DatabaseType, seq);
                    await WriteStatementAsync(output, dialect,
                        BuildCreateSequence(dialect, source.DatabaseType, seq, meta));
                }
                catch (Exception ex)
                {
                    await WriteErrorCommentAsync(output, $"Sequenza {seq.QualifiedName}", ex);
                }
            }
        }

        if (options.IncludeSchema && tables.Count > 0)
        {
            await WriteSectionAsync(output, "TABELLE");
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // La tabella viene conteggiata una sola volta: qui se i dati NON sono inclusi,
                // altrimenti nel ciclo della sezione DATI.
                if (!options.IncludeData)
                    Report(progress, state, table);
                try
                {
                    var columns = await _schemaService.GetTableColumnsAsync(
                        connection, source.DatabaseType, table.Schema, table.Name);
                    if (columns.Count == 0)
                    {
                        await output.WriteLineAsync($"-- ATTENZIONE: nessuna colonna trovata per {table.QualifiedName}");
                        await output.WriteLineAsync();
                        continue;
                    }
                    string ddl = _schemaService.BuildCreateTableStatement(dialect, table.Schema, table.Name, columns);
                    await WriteStatementAsync(output, dialect, ddl);
                }
                catch (Exception ex)
                {
                    await WriteErrorCommentAsync(output, $"Tabella {table.QualifiedName}", ex);
                }
            }
        }

        if (options.IncludeData && tables.Count > 0)
        {
            await WriteSectionAsync(output, "DATI");
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, state, table, $"dati di {table.QualifiedName}");
                try
                {
                    await WriteTableDataAsync(connection, source.DatabaseType, dialect,
                        table, options, output, progress, state, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await WriteErrorCommentAsync(output, $"Dati di {table.QualifiedName}", ex);
                }
            }
        }

        if (options.IncludeSchema && tables.Count > 0)
        {
            // Vincoli PRIMARY KEY / UNIQUE dopo l'inserimento dei dati.
            var pkUnique = new List<string>();
            var foreignKeys = new List<string>();
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    foreach (var c in await GetKeyConstraintsAsync(connection, source.DatabaseType, table))
                        pkUnique.Add(BuildKeyConstraintDdl(dialect, table, c));
                    foreach (var fk in await GetForeignKeysAsync(connection, source.DatabaseType, table))
                        foreignKeys.Add(BuildForeignKeyDdl(dialect, table, fk));
                }
                catch (Exception ex)
                {
                    Log($"[ScriptGeneration] Vincoli non estratti per {table.QualifiedName}: {ex.Message}");
                }
            }

            if (pkUnique.Count > 0)
            {
                await WriteSectionAsync(output, "VINCOLI PRIMARY KEY / UNIQUE");
                foreach (var ddl in pkUnique)
                    await WriteStatementAsync(output, dialect, ddl);
            }

            if (indexes.Count > 0)
            {
                await WriteSectionAsync(output, "INDICI");
                foreach (var index in indexes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Report(progress, state, index);
                    try
                    {
                        var def = await GetIndexDefAsync(connection, source.DatabaseType, index);
                        if (def == null)
                        {
                            await output.WriteLineAsync($"-- Indice {index.QualifiedName} ignorato (nessuna colonna semplice).");
                            await output.WriteLineAsync();
                            continue;
                        }
                        await WriteStatementAsync(output, dialect, BuildIndexDdl(dialect, def));
                    }
                    catch (Exception ex)
                    {
                        await WriteErrorCommentAsync(output, $"Indice {index.QualifiedName}", ex);
                    }
                }
            }

            if (foreignKeys.Count > 0)
            {
                await WriteSectionAsync(output, "VINCOLI FOREIGN KEY");
                foreach (var ddl in foreignKeys)
                    await WriteStatementAsync(output, dialect, ddl);
            }
        }

        if (options.IncludeSchema && (views.Count > 0 || routines.Count > 0 || triggers.Count > 0))
        {
            await WriteProgrammableObjectsAsync(connection, source.DatabaseType, dialect,
                views, routines, triggers, output, progress, state, cancellationToken);
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("-- Fine dello script.");
        await output.FlushAsync(cancellationToken);
    }

    private static List<DatabaseObject> Filter(IReadOnlyList<DatabaseObject> objs, DatabaseObjectType type)
        => objs.Where(o => o.ObjectType == type).ToList();

    private static void Report(IProgress<ScriptGenerationProgress>? progress,
        ScriptGenerationProgress state, DatabaseObject obj, string? label = null)
    {
        state.ProcessedObjects++;
        state.CurrentObject = label ?? $"{obj.DisplayType} {obj.QualifiedName}";
        progress?.Report(new ScriptGenerationProgress
        {
            CurrentObject = state.CurrentObject,
            ProcessedObjects = state.ProcessedObjects,
            TotalObjects = state.TotalObjects,
            RowsWritten = state.RowsWritten
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Oggetti programmabili (viste, procedure, funzioni, trigger)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task WriteProgrammableObjectsAsync(DbConnection connection, DatabaseType sourceDbType,
        DatabaseType dialect, List<DatabaseObject> views, List<DatabaseObject> routines,
        List<DatabaseObject> triggers, TextWriter output, IProgress<ScriptGenerationProgress>? progress,
        ScriptGenerationProgress state, CancellationToken ct)
    {
        bool crossDialect = dialect != sourceDbType;

        async Task EmitGroup(string section, List<DatabaseObject> group)
        {
            if (group.Count == 0) return;
            await WriteSectionAsync(output, section);
            foreach (var obj in group)
            {
                ct.ThrowIfCancellationRequested();
                Report(progress, state, obj);
                bool plsql = sourceDbType == DatabaseType.Oracle
                    && obj.ObjectType != DatabaseObjectType.View;

                if (crossDialect)
                {
                    await output.WriteLineAsync(
                        $"-- ┌─ ATTENZIONE: {obj.DisplayType} \"{obj.QualifiedName}\" ───────────────");
                    await output.WriteLineAsync(
                        $"-- │ Definizione estratta nel dialetto di origine ({sourceDbType}).");
                    await output.WriteLineAsync(
                        $"-- │ Il dialetto di destinazione scelto è {dialect}: il corpo potrebbe");
                    await output.WriteLineAsync(
                        "-- │ richiedere una revisione/adattamento manuale prima dell'esecuzione.");
                    await output.WriteLineAsync(
                        "-- └────────────────────────────────────────────────────────────");
                }

                List<string> definitions;
                try
                {
                    definitions = await GetObjectDefinitionsAsync(connection, sourceDbType, obj);
                }
                catch (Exception ex)
                {
                    await output.WriteLineAsync($"-- Impossibile estrarre la definizione: {ex.Message}");
                    await output.WriteLineAsync();
                    continue;
                }

                if (definitions.Count == 0)
                {
                    await output.WriteLineAsync("-- Definizione non disponibile (oggetto cifrato o vuoto).");
                    await output.WriteLineAsync();
                    continue;
                }

                foreach (var def in definitions)
                    await WriteStatementAsync(output, dialect, def, plsql);
            }
        }

        await EmitGroup("VISTE", views);
        await EmitGroup("STORED PROCEDURE / FUNZIONI", routines);
        await EmitGroup("TRIGGER", triggers);
    }

    private async Task<List<string>> GetObjectDefinitionsAsync(DbConnection connection,
        DatabaseType sourceDbType, DatabaseObject obj)
    {
        var results = new List<string>();

        switch (sourceDbType)
        {
            case DatabaseType.SqlServer:
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT m.definition FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(@qn)";
                cmd.CommandTimeout = _commandTimeoutSeconds;
                AddParameter(cmd, "@qn", $"{obj.Schema}.{obj.Name}");
                var def = await cmd.ExecuteScalarAsync();
                if (def != null && def != DBNull.Value)
                    results.Add(def.ToString()!.Trim());
                break;
            }
            case DatabaseType.PostgreSQL:
            {
                if (obj.ObjectType == DatabaseObjectType.View)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"SELECT pg_get_viewdef(c.oid, true) FROM pg_class c
                        JOIN pg_namespace n ON c.relnamespace = n.oid
                        WHERE n.nspname = @s AND c.relname = @n";
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    AddParameter(cmd, "@s", obj.Schema);
                    AddParameter(cmd, "@n", obj.Name);
                    var body = (await cmd.ExecuteScalarAsync())?.ToString();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        results.Add($"CREATE OR REPLACE VIEW \"{obj.Schema}\".\"{obj.Name}\" AS\n{body.Trim()}");
                    }
                }
                else if (obj.ObjectType == DatabaseObjectType.Trigger)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"SELECT pg_get_triggerdef(t.oid) FROM pg_trigger t
                        JOIN pg_class c ON t.tgrelid = c.oid
                        JOIN pg_namespace n ON c.relnamespace = n.oid
                        WHERE n.nspname = @s AND t.tgname = @n AND NOT t.tgisinternal";
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    AddParameter(cmd, "@s", obj.Schema);
                    AddParameter(cmd, "@n", obj.Name);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        if (!reader.IsDBNull(0)) results.Add(reader.GetString(0).Trim());
                }
                else // StoredProcedure / Function
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"SELECT pg_get_functiondef(p.oid) FROM pg_proc p
                        JOIN pg_namespace n ON p.pronamespace = n.oid
                        WHERE n.nspname = @s AND p.proname = @n";
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    AddParameter(cmd, "@s", obj.Schema);
                    AddParameter(cmd, "@n", obj.Name);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        if (!reader.IsDBNull(0)) results.Add(reader.GetString(0).Trim());
                }
                break;
            }
            case DatabaseType.Oracle:
            {
                string metadataType = obj.ObjectType switch
                {
                    DatabaseObjectType.View => "VIEW",
                    DatabaseObjectType.StoredProcedure => "PROCEDURE",
                    DatabaseObjectType.Function => "FUNCTION",
                    DatabaseObjectType.Trigger => "TRIGGER",
                    _ => throw new NotSupportedException()
                };
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DBMS_METADATA.GET_DDL(:otype, :oname, :oowner) FROM DUAL";
                cmd.CommandTimeout = _commandTimeoutSeconds;
                AddParameter(cmd, ":otype", metadataType);
                AddParameter(cmd, ":oname", obj.Name);
                AddParameter(cmd, ":oowner", obj.Schema);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync() && !reader.IsDBNull(0))
                    results.Add(reader.GetString(0).Trim());
                break;
            }
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dati (INSERT)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task WriteTableDataAsync(DbConnection connection, DatabaseType sourceDbType,
        DatabaseType dialect, DatabaseObject table, ScriptGenerationOptions options, TextWriter output,
        IProgress<ScriptGenerationProgress>? progress, ScriptGenerationProgress state, CancellationToken ct)
    {
        string sourceTable = FormatTableName(sourceDbType, table.Schema, table.Name);
        string targetTable = FormatTableName(dialect, table.Schema, table.Name);
        int batchSize = Math.Clamp(options.RowsPerInsertBatch, 1, 1000);

        await output.WriteLineAsync($"-- Dati per {table.QualifiedName}");

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {sourceTable}";
        command.CommandTimeout = _commandTimeoutSeconds;

        using var reader = await ExecuteWithRetryAsync(
            () => command.ExecuteReaderAsync(ct), "WriteTableData.Reader");

        if (reader.FieldCount == 0)
        {
            await output.WriteLineAsync("-- (tabella senza colonne)");
            await output.WriteLineAsync();
            return;
        }

        var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        string columnList = string.Join(", ", columnNames.Select(c => FormatColumnName(dialect, c)));
        string insertHead = $"INSERT INTO {targetTable} ({columnList}) VALUES";

        var batch = new List<string>(batchSize);
        long rowsForTable = 0;
        int statementsSinceGo = 0;

        async Task FlushBatchAsync()
        {
            if (batch.Count == 0) return;
            if (dialect == DatabaseType.Oracle)
            {
                // Oracle non supporta INSERT multi-riga: uno statement per riga.
                foreach (var row in batch)
                    await output.WriteLineAsync($"{insertHead} ({row});");
            }
            else
            {
                await output.WriteLineAsync(insertHead);
                for (int i = 0; i < batch.Count; i++)
                {
                    string sep = i < batch.Count - 1 ? "," : ";";
                    await output.WriteLineAsync($"  ({batch[i]}){sep}");
                }
            }
            statementsSinceGo++;
            if (dialect == DatabaseType.SqlServer && statementsSinceGo >= 100)
            {
                await output.WriteLineAsync("GO");
                statementsSinceGo = 0;
            }
            batch.Clear();
        }

        bool isSqlServerReader = reader is Microsoft.Data.SqlClient.SqlDataReader;
        while (await reader.ReadAsync(ct))
        {
            var values = new object[reader.FieldCount];
            if (isSqlServerReader)
            {
                var sqlReader = (Microsoft.Data.SqlClient.SqlDataReader)reader;
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    if (sqlReader.IsDBNull(col))
                        values[col] = DBNull.Value;
                    else if (reader.GetFieldType(col) == typeof(decimal) &&
                             reader.GetDataTypeName(col).ToLowerInvariant() is "decimal" or "numeric")
                        values[col] = sqlReader.GetSqlDecimal(col);
                    else
                        values[col] = reader.GetValue(col);
                }
            }
            else
            {
                reader.GetValues(values);
            }

            string formatted = string.Join(", ",
                values.Select(v => _databaseService.FormatSqlValue(dialect, v, unicodeStringLiterals: true)));
            batch.Add(formatted);
            rowsForTable++;
            state.RowsWritten++;

            if (batch.Count >= batchSize)
            {
                await FlushBatchAsync();
                progress?.Report(new ScriptGenerationProgress
                {
                    CurrentObject = $"dati di {table.QualifiedName} ({rowsForTable} righe)",
                    ProcessedObjects = state.ProcessedObjects,
                    TotalObjects = state.TotalObjects,
                    RowsWritten = state.RowsWritten
                });
            }
        }
        await FlushBatchAsync();

        if (dialect == DatabaseType.SqlServer && statementsSinceGo > 0)
            await output.WriteLineAsync("GO");

        await output.WriteLineAsync(rowsForTable == 0
            ? "-- (nessun dato)"
            : $"-- {rowsForTable} righe esportate.");
        await output.WriteLineAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Metadati: sequenze, vincoli, indici
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SequenceMeta
    {
        public string Start = "1";
        public string Increment = "1";
        public string? MinValue;
        public string? MaxValue;
        public bool Cycle;
    }

    private async Task<SequenceMeta> GetSequenceMetaAsync(DbConnection connection,
        DatabaseType dbType, DatabaseObject seq)
    {
        var meta = new SequenceMeta();
        string sql;
        switch (dbType)
        {
            case DatabaseType.SqlServer:
                sql = @"SELECT CONVERT(varchar(64), start_value), CONVERT(varchar(64), increment),
                               CONVERT(varchar(64), minimum_value), CONVERT(varchar(64), maximum_value), is_cycling
                        FROM sys.sequences seq JOIN sys.schemas s ON seq.schema_id = s.schema_id
                        WHERE s.name = @s AND seq.name = @n";
                break;
            case DatabaseType.PostgreSQL:
                sql = @"SELECT start_value::text, increment_by::text, min_value::text, max_value::text, cycle
                        FROM pg_sequences WHERE schemaname = @s AND sequencename = @n";
                break;
            default: // Oracle
                sql = @"SELECT TO_CHAR(last_number), TO_CHAR(increment_by), TO_CHAR(min_value),
                               TO_CHAR(max_value), cycle_flag
                        FROM all_sequences WHERE sequence_owner = :s AND sequence_name = :n";
                break;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _commandTimeoutSeconds;
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":s" : "@s", seq.Schema);
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":n" : "@n", seq.Name);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            meta.Start = reader[0]?.ToString() ?? "1";
            meta.Increment = reader[1]?.ToString() ?? "1";
            meta.MinValue = reader.IsDBNull(2) ? null : reader[2]?.ToString();
            meta.MaxValue = reader.IsDBNull(3) ? null : reader[3]?.ToString();
            string cycle = reader[4]?.ToString() ?? "";
            meta.Cycle = cycle is "Y" or "y" || cycle is "True" or "true" || cycle == "1";
        }
        return meta;
    }

    private sealed class KeyConstraint
    {
        public string Name = string.Empty;
        public string Type = string.Empty; // "PRIMARY KEY" | "UNIQUE"
        public List<string> Columns = new();
    }

    private async Task<List<KeyConstraint>> GetKeyConstraintsAsync(DbConnection connection,
        DatabaseType dbType, DatabaseObject table)
    {
        string sql = dbType switch
        {
            DatabaseType.SqlServer => @"
                SELECT kc.name,
                       CASE kc.type WHEN 'PK' THEN 'PRIMARY KEY' ELSE 'UNIQUE' END,
                       c.name, ic.key_ordinal
                FROM sys.key_constraints kc
                JOIN sys.tables t ON kc.parent_object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                JOIN sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
                JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ic.column_id
                WHERE s.name = @s AND t.name = @n
                ORDER BY kc.name, ic.key_ordinal",
            DatabaseType.PostgreSQL => @"
                SELECT con.conname, CASE con.contype WHEN 'p' THEN 'PRIMARY KEY' ELSE 'UNIQUE' END,
                       att.attname, k.ord
                FROM pg_constraint con
                JOIN pg_class cl ON con.conrelid = cl.oid
                JOIN pg_namespace ns ON cl.relnamespace = ns.oid
                JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord) ON true
                JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
                WHERE con.contype IN ('p','u') AND ns.nspname = @s AND cl.relname = @n
                ORDER BY con.conname, k.ord",
            DatabaseType.Oracle => @"
                SELECT c.constraint_name,
                       CASE c.constraint_type WHEN 'P' THEN 'PRIMARY KEY' ELSE 'UNIQUE' END,
                       cc.column_name, cc.position
                FROM all_constraints c
                JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
                WHERE c.constraint_type IN ('P','U') AND c.owner = :s AND c.table_name = :n
                ORDER BY c.constraint_name, cc.position",
            _ => throw new NotSupportedException()
        };

        var map = new Dictionary<string, KeyConstraint>();
        await ReadConstraintRowsAsync(connection, dbType, sql, table, reader =>
        {
            string name = reader[0]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;
            if (!map.TryGetValue(name, out var kc))
            {
                kc = new KeyConstraint { Name = name, Type = reader[1]?.ToString() ?? "UNIQUE" };
                map[name] = kc;
            }
            kc.Columns.Add(reader[2]?.ToString() ?? "");
        });
        return map.Values.ToList();
    }

    private sealed class ForeignKey
    {
        public string Name = string.Empty;
        public List<string> Columns = new();
        public string RefSchema = string.Empty;
        public string RefTable = string.Empty;
        public List<string> RefColumns = new();
    }

    private async Task<List<ForeignKey>> GetForeignKeysAsync(DbConnection connection,
        DatabaseType dbType, DatabaseObject table)
    {
        string sql = dbType switch
        {
            DatabaseType.SqlServer => @"
                SELECT fk.name, c.name, rs.name, rt.name, rc.name, fkc.constraint_column_id
                FROM sys.foreign_keys fk
                JOIN sys.tables t ON fk.parent_object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
                JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                WHERE s.name = @s AND t.name = @n
                ORDER BY fk.name, fkc.constraint_column_id",
            DatabaseType.PostgreSQL => @"
                SELECT con.conname, att.attname, rns.nspname, rcl.relname, ratt.attname, k.ord
                FROM pg_constraint con
                JOIN pg_class cl ON con.conrelid = cl.oid
                JOIN pg_namespace ns ON cl.relnamespace = ns.oid
                JOIN pg_class rcl ON con.confrelid = rcl.oid
                JOIN pg_namespace rns ON rcl.relnamespace = rns.oid
                JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(att, ratt, ord) ON true
                JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.att
                JOIN pg_attribute ratt ON ratt.attrelid = con.confrelid AND ratt.attnum = k.ratt
                WHERE con.contype = 'f' AND ns.nspname = @s AND cl.relname = @n
                ORDER BY con.conname, k.ord",
            DatabaseType.Oracle => @"
                SELECT c.constraint_name, cc.column_name, rc.owner, rc.table_name, rcc.column_name, cc.position
                FROM all_constraints c
                JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
                JOIN all_constraints rc ON rc.owner = c.r_owner AND rc.constraint_name = c.r_constraint_name
                JOIN all_cons_columns rcc ON rcc.owner = rc.owner AND rcc.constraint_name = rc.constraint_name
                                          AND rcc.position = cc.position
                WHERE c.constraint_type = 'R' AND c.owner = :s AND c.table_name = :n
                ORDER BY c.constraint_name, cc.position",
            _ => throw new NotSupportedException()
        };

        var map = new Dictionary<string, ForeignKey>();
        await ReadConstraintRowsAsync(connection, dbType, sql, table, reader =>
        {
            string name = reader[0]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;
            if (!map.TryGetValue(name, out var fk))
            {
                fk = new ForeignKey
                {
                    Name = name,
                    RefSchema = reader[2]?.ToString() ?? "",
                    RefTable = reader[3]?.ToString() ?? ""
                };
                map[name] = fk;
            }
            fk.Columns.Add(reader[1]?.ToString() ?? "");
            fk.RefColumns.Add(reader[4]?.ToString() ?? "");
        });
        return map.Values.ToList();
    }

    private async Task ReadConstraintRowsAsync(DbConnection connection, DatabaseType dbType,
        string sql, DatabaseObject table, Action<DbDataReader> onRow)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _commandTimeoutSeconds;
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":s" : "@s", table.Schema);
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":n" : "@n", table.Name);
        using var reader = await ExecuteWithRetryAsync(() => cmd.ExecuteReaderAsync(), "ReadConstraints");
        while (await reader.ReadAsync())
            onRow(reader);
    }

    private sealed class IndexDef
    {
        public string Name = string.Empty;
        public string Schema = string.Empty;
        public string Table = string.Empty;
        public bool IsUnique;
        public List<(string Column, bool Descending)> Columns = new();
    }

    private async Task<IndexDef?> GetIndexDefAsync(DbConnection connection,
        DatabaseType dbType, DatabaseObject index)
    {
        string sql = dbType switch
        {
            DatabaseType.SqlServer => @"
                SELECT i.is_unique, c.name, ic.is_descending_key, ic.key_ordinal
                FROM sys.indexes i
                JOIN sys.tables t ON i.object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
                WHERE s.name = @s AND i.name = @n AND ic.is_included_column = 0
                ORDER BY ic.key_ordinal",
            DatabaseType.PostgreSQL => @"
                SELECT ix.indisunique, a.attname, false, k.ord
                FROM pg_index ix
                JOIN pg_class i ON i.oid = ix.indexrelid
                JOIN pg_class t ON t.oid = ix.indrelid
                JOIN pg_namespace ns ON ns.oid = t.relnamespace
                JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                WHERE ns.nspname = @s AND i.relname = @n AND k.attnum <> 0
                ORDER BY k.ord",
            DatabaseType.Oracle => @"
                SELECT CASE i.uniqueness WHEN 'UNIQUE' THEN 1 ELSE 0 END,
                       ic.column_name, CASE ic.descend WHEN 'DESC' THEN 1 ELSE 0 END, ic.column_position
                FROM all_indexes i
                JOIN all_ind_columns ic ON ic.index_owner = i.owner AND ic.index_name = i.index_name
                WHERE i.owner = :s AND i.index_name = :n
                ORDER BY ic.column_position",
            _ => throw new NotSupportedException()
        };

        var def = new IndexDef { Name = index.Name, Schema = index.Schema, Table = index.ParentName };
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _commandTimeoutSeconds;
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":s" : "@s", index.Schema);
        AddParameter(cmd, dbType == DatabaseType.Oracle ? ":n" : "@n", index.Name);

        using var reader = await ExecuteWithRetryAsync(() => cmd.ExecuteReaderAsync(), "GetIndexDef");
        while (await reader.ReadAsync())
        {
            def.IsUnique = Convert.ToBoolean(reader[0]);
            def.Columns.Add((reader[1]?.ToString() ?? "", Convert.ToBoolean(reader[2])));
        }
        return def.Columns.Count == 0 ? null : def;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Costruttori DDL (generici cross-dialetto)
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildCreateSequence(DatabaseType dialect, DatabaseType sourceDbType,
        DatabaseObject seq, SequenceMeta meta)
    {
        string name = FormatTableName(dialect, seq.Schema, seq.Name);
        var parts = new List<string> { $"CREATE SEQUENCE {name}" };
        parts.Add($"START WITH {meta.Start}");
        parts.Add($"INCREMENT BY {meta.Increment}");

        // MINVALUE/MAXVALUE solo se il dialetto di destinazione coincide con quello di origine,
        // per evitare valori fuori range nel dialetto di destinazione.
        if (dialect == sourceDbType)
        {
            if (!string.IsNullOrWhiteSpace(meta.MinValue)) parts.Add($"MINVALUE {meta.MinValue}");
            if (!string.IsNullOrWhiteSpace(meta.MaxValue)) parts.Add($"MAXVALUE {meta.MaxValue}");
        }

        string cycle = dialect == DatabaseType.Oracle
            ? (meta.Cycle ? "CYCLE" : "NOCYCLE")
            : (meta.Cycle ? "CYCLE" : "NO CYCLE");
        parts.Add(cycle);

        return string.Join(" ", parts);
    }

    private static string BuildKeyConstraintDdl(DatabaseType dialect, DatabaseObject table, KeyConstraint c)
    {
        string tableRef = FormatTableName(dialect, table.Schema, table.Name);
        string cols = string.Join(", ", c.Columns.Select(col => FormatColumnName(dialect, col)));
        string name = FormatConstraintName(dialect, c.Name);
        return $"ALTER TABLE {tableRef} ADD CONSTRAINT {name} {c.Type} ({cols})";
    }

    private static string BuildForeignKeyDdl(DatabaseType dialect, DatabaseObject table, ForeignKey fk)
    {
        string tableRef = FormatTableName(dialect, table.Schema, table.Name);
        string refRef = FormatTableName(dialect, fk.RefSchema, fk.RefTable);
        string cols = string.Join(", ", fk.Columns.Select(c => FormatColumnName(dialect, c)));
        string refCols = string.Join(", ", fk.RefColumns.Select(c => FormatColumnName(dialect, c)));
        string name = FormatConstraintName(dialect, fk.Name);
        return $"ALTER TABLE {tableRef} ADD CONSTRAINT {name} FOREIGN KEY ({cols}) " +
               $"REFERENCES {refRef} ({refCols})";
    }

    private static string BuildIndexDdl(DatabaseType dialect, IndexDef def)
    {
        string tableRef = FormatTableName(dialect, def.Schema, def.Table);
        string indexName = FormatConstraintName(dialect, def.Name);
        bool supportsDescending = dialect != DatabaseType.PostgreSQL;
        string cols = string.Join(", ", def.Columns.Select(c =>
            FormatColumnName(dialect, c.Column) + (c.Descending && supportsDescending ? " DESC" : "")));
        string unique = def.IsUnique ? "UNIQUE " : "";
        return $"CREATE {unique}INDEX {indexName} ON {tableRef} ({cols})";
    }

    private static string BuildDropStatement(DatabaseType dialect, DatabaseObject obj)
    {
        string name = FormatTableName(dialect, obj.Schema, obj.Name);
        return obj.ObjectType switch
        {
            DatabaseObjectType.Table => dialect switch
            {
                DatabaseType.PostgreSQL => $"DROP TABLE IF EXISTS {name} CASCADE",
                // PURGE evita di lasciare la tabella nel recycle bin di Oracle a ogni ri-esecuzione.
                DatabaseType.Oracle => $"DROP TABLE {name} CASCADE CONSTRAINTS PURGE",
                _ => $"DROP TABLE IF EXISTS {name}"
            },
            DatabaseObjectType.View => $"DROP VIEW IF EXISTS {name}",
            DatabaseObjectType.StoredProcedure => $"DROP PROCEDURE IF EXISTS {name}",
            DatabaseObjectType.Function => $"DROP FUNCTION IF EXISTS {name}",
            DatabaseObjectType.Sequence => $"DROP SEQUENCE IF EXISTS {name}",
            // PostgreSQL richiede la tabella: DROP TRIGGER nome ON schema.tabella.
            DatabaseObjectType.Trigger => dialect switch
            {
                DatabaseType.PostgreSQL =>
                    $"DROP TRIGGER IF EXISTS {FormatConstraintName(dialect, obj.Name)} ON " +
                    $"{FormatTableName(dialect, obj.Schema, obj.ParentName)}",
                DatabaseType.Oracle => $"DROP TRIGGER {name}",
                _ => $"DROP TRIGGER IF EXISTS {name}"
            },
            DatabaseObjectType.Index => dialect == DatabaseType.SqlServer
                ? $"DROP INDEX IF EXISTS {FormatConstraintName(dialect, obj.Name)} ON " +
                  $"{FormatTableName(dialect, obj.Schema, obj.ParentName)}"
                : $"DROP INDEX IF EXISTS {FormatTableName(dialect, obj.Schema, obj.Name)}",
            _ => $"-- DROP non supportato per {obj.QualifiedName}"
        };
    }

    private static string FormatConstraintName(DatabaseType dialect, string name) => dialect switch
    {
        DatabaseType.SqlServer => $"[{name.Replace("]", "]]")}]",
        DatabaseType.PostgreSQL => $"\"{name.Replace("\"", "\"\"").ToLowerInvariant()}\"",
        DatabaseType.Oracle => $"\"{name.Replace("\"", "\"\"").ToUpperInvariant()}\"",
        _ => name
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Scrittura su file
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task WriteHeaderAsync(TextWriter w, ConnectionInfo source,
        ScriptGenerationOptions options, int objectCount)
    {
        await w.WriteLineAsync("-- ============================================================");
        await w.WriteLineAsync("-- Script generato da Database Migrator");
        await w.WriteLineAsync($"-- Data:                  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await w.WriteLineAsync($"-- Database di origine:   {source.DatabaseType} @ {source.Server}/{source.Database}");
        await w.WriteLineAsync($"-- Dialetto di output:    {options.TargetDialect}");
        await w.WriteLineAsync($"-- Oggetti selezionati:   {objectCount}");
        await w.WriteLineAsync($"-- Includi schema (DDL):  {(options.IncludeSchema ? "sì" : "no")}");
        await w.WriteLineAsync($"-- Includi dati (INSERT): {(options.IncludeData ? "sì" : "no")}");
        await w.WriteLineAsync($"-- Includi DROP:          {(options.IncludeDropStatements ? "sì" : "no")}");
        await w.WriteLineAsync("-- ============================================================");
        await w.WriteLineAsync();
    }

    private static async Task WriteSectionAsync(TextWriter w, string title)
    {
        await w.WriteLineAsync();
        await w.WriteLineAsync($"-- ─────────────────────────────────────────────");
        await w.WriteLineAsync($"-- {title}");
        await w.WriteLineAsync($"-- ─────────────────────────────────────────────");
        await w.WriteLineAsync();
    }

    private static async Task WriteSchemasAsync(TextWriter output, DatabaseType dialect,
        IEnumerable<string> schemas)
    {
        var distinct = schemas
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => !(dialect == DatabaseType.SqlServer && s.Equals("dbo", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (distinct.Count == 0)
            return;

        await WriteSectionAsync(output, "SCHEMI");
        foreach (var schema in distinct)
        {
            if (dialect == DatabaseType.Oracle)
            {
                // In Oracle lo schema coincide con l'utente: la sua creazione richiede privilegi DBA.
                await output.WriteLineAsync(
                    $"-- Verificare che lo schema/utente \"{schema.ToUpperInvariant()}\" esista " +
                    "(CREATE USER richiede privilegi DBA).");
                await output.WriteLineAsync();
            }
            else
            {
                await WriteStatementAsync(output, dialect, BuildCreateSchema(dialect, schema));
            }
        }
    }

    private static string BuildCreateSchema(DatabaseType dialect, string schema) => dialect switch
    {
        DatabaseType.PostgreSQL =>
            $"CREATE SCHEMA IF NOT EXISTS \"{EscapePostgresIdentifier(schema.ToLowerInvariant())}\"",
        DatabaseType.SqlServer =>
            $"IF SCHEMA_ID(N'{schema.Replace("'", "''")}') IS NULL " +
            $"EXEC(N'CREATE SCHEMA [{EscapeSqlServerIdentifier(schema)}]')",
        _ => throw new NotSupportedException()
    };

    private static async Task WriteErrorCommentAsync(TextWriter w, string what, Exception ex)
    {
        Log($"[ScriptGeneration] {what}: {ex.Message}");
        string message = ex.Message.Replace("\r", " ").Replace("\n", " ");
        await w.WriteLineAsync($"-- !! ERRORE durante l'esportazione di: {what}");
        await w.WriteLineAsync($"-- !! {message}");
        await w.WriteLineAsync();
    }

    private static async Task WriteStatementAsync(TextWriter w, DatabaseType dialect,
        string sql, bool plsqlBlock = false)
    {
        sql = sql.TrimEnd();
        if (dialect == DatabaseType.Oracle && plsqlBlock)
        {
            // Blocco PL/SQL: il corpo (con i suoi ';') resta intatto, terminato da '/'.
            await w.WriteLineAsync(sql);
            await w.WriteLineAsync("/");
        }
        else
        {
            if (!sql.EndsWith(';'))
                sql += ";";
            await w.WriteLineAsync(sql);
            if (dialect == DatabaseType.SqlServer)
                await w.WriteLineAsync("GO");
        }
        await w.WriteLineAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }
}
