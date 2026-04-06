using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

public sealed class SchemaMigrationResult
{
    public IReadOnlyList<ConstraintAddedInfo> ConstraintsAdded { get; }

    public SchemaMigrationResult(IReadOnlyList<ConstraintAddedInfo> constraintsAdded)
    {
        ConstraintsAdded = constraintsAdded;
    }
}

public sealed class ConstraintAddedInfo
{
    public string Schema { get; }
    public string TableName { get; }
    public string ConstraintName { get; }
    public string ConstraintType { get; } // e.g. "PRIMARY KEY" or "UNIQUE"

    public ConstraintAddedInfo(string schema, string tableName, string constraintName, string constraintType)
    {
        Schema = schema;
        TableName = tableName;
        ConstraintName = constraintName;
        ConstraintType = constraintType;
    }
}

/// <summary>
/// Service for schema migration (DDL) between different database types.
/// Handles cross-database data type mapping.
/// </summary>
public class SchemaMigrationService : DatabaseServiceBase
{
    /// <summary>SQL Server built-in functions commonly used in column defaults (T-SQL).</summary>
    private static readonly string[] SqlServerBuiltinDefaultFunctionNames =
    [
        "getdate", "getutcdate", "newid", "newsequentialid", "sysdatetime", "sysutcdatetime",
        "sysdatetimeoffset"
    ];

    /// <summary>
    /// Checks if a table exists in the specified database.
    /// </summary>
    /// <param name="connectionInfo">Connection info for the database to check</param>
    /// <param name="schema">Schema name</param>
    /// <param name="tableName">Table name</param>
    /// <returns>True if the table exists, false otherwise</returns>
    public async Task<bool> CheckTableExistsAsync(ConnectionInfo connectionInfo, string schema, string tableName)
    {
        using (var connection = CreateConnection(connectionInfo))
        {
            await ExecuteWithRetryAsync(() => connection.OpenAsync(), "CheckTableExistsAsync.Open");
            return await TableExistsAsync(connection, connectionInfo.DatabaseType, schema, tableName);
        }
    }

    /// <summary>
    /// Drops a table from the specified database.
    /// Used to rollback schema creation when data migration fails.
    /// </summary>
    /// <param name="connectionInfo">Connection info for the database</param>
    /// <param name="schema">Schema name</param>
    /// <param name="tableName">Table name</param>
    /// <returns>True if the table was dropped successfully, false otherwise</returns>
    public async Task<bool> DropTableAsync(ConnectionInfo connectionInfo, string schema, string tableName)
    {
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "DropTableAsync.Open");

                string dropQuery = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => $"DROP TABLE [{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(tableName)}]",
                    // Match FormatTableName: PostgreSQL DDL always uses lowercased quoted identifiers.
                    DatabaseType.PostgreSQL => $"DROP TABLE {FormatTableName(DatabaseType.PostgreSQL, schema, tableName)}",
                    // Oracle: include schema prefix (was missing) so the correct table is dropped.
                    DatabaseType.Oracle => $"DROP TABLE {schema.ToUpperInvariant()}.{tableName.ToUpperInvariant()}",
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = dropQuery;
                    command.CommandTimeout = _commandTimeoutSeconds;
                    await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "DropTableAsync.Execute");
                }

                Log($"[DropTableAsync] Successfully dropped table {schema}.{tableName}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"[DropTableAsync] Error dropping table {schema}.{tableName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Drops a PK/UNIQUE constraint from the target database.
    /// Used to rollback schema changes when data migration fails.
    /// </summary>
    public async Task<bool> DropConstraintAsync(
        ConnectionInfo connectionInfo,
        string schema,
        string tableName,
        string constraintName,
        string constraintType)
    {
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "DropConstraintAsync.Open");

                string tableRef = FormatTableName(connectionInfo.DatabaseType, schema, tableName);

                string dropQuery = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer =>
                        $"ALTER TABLE {tableRef} DROP CONSTRAINT [{EscapeSqlServerIdentifier(constraintName)}]",
                    DatabaseType.PostgreSQL =>
                        $"ALTER TABLE {tableRef} DROP CONSTRAINT \"{EscapePostgresIdentifier(constraintName)}\"",
                    DatabaseType.Oracle =>
                        $"ALTER TABLE {tableRef} DROP CONSTRAINT \"{EscapeOracleIdentifier(constraintName)}\"",
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = dropQuery;
                    command.CommandTimeout = _commandTimeoutSeconds;
                    await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "DropConstraintAsync.Execute");
                }

                Log($"[DropConstraintAsync] Dropped constraint '{constraintName}' ({constraintType}) on {schema}.{tableName}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"[DropConstraintAsync] Error dropping constraint '{constraintName}' ({constraintType}) on {schema}.{tableName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates the schema in the target database based on the source database.
    /// Handles automatic data type mapping.
    /// </summary>
    public async Task<SchemaMigrationResult> MigrateSchemaAsync(ConnectionInfo source, ConnectionInfo target, 
        List<TableInfo> tablesToMigrate)
    {
        var constraintsAdded = new List<ConstraintAddedInfo>();

        using (var sourceConn = CreateConnection(source))
        using (var targetConn = CreateConnection(target))
        {
            await ExecuteWithRetryAsync(() => sourceConn.OpenAsync(), "MigrateSchemaAsync.SourceOpen");
            await ExecuteWithRetryAsync(() => targetConn.OpenAsync(), "MigrateSchemaAsync.TargetOpen");

            foreach (var table in tablesToMigrate)
            {
                try
                {
                    Log($"[SchemaMigration] Starting migration for table {table.Schema}.{table.TableName}");
                    
                    // Check if the table already exists in the target
                    Log($"[SchemaMigration] Checking if table exists in target...");
                    bool tableExists = await TableExistsAsync(targetConn, target.DatabaseType, 
                        table.Schema, table.TableName);
                    
                    if (tableExists)
                    {
                        Log($"[SchemaMigration] Table {table.Schema}.{table.TableName} already exists in target, skipping creation");
                    }
                    else
                    {
                        // Retrieve the table definition from the source
                        Log($"[SchemaMigration] Fetching columns from source...");
                        var columns = await GetTableColumnsAsync(sourceConn, source.DatabaseType, 
                            table.Schema, table.TableName);
                        Log($"[SchemaMigration] Found {columns.Count} columns");

                        // Build the DDL for the target
                        Log($"[SchemaMigration] Building CREATE TABLE statement for target...");
                        string createTableDdl = BuildCreateTableStatement(target.DatabaseType, 
                            table.Schema, table.TableName, columns);
                        Log($"[SchemaMigration] CREATE TABLE DDL built for {table.Schema}.{table.TableName} (len={createTableDdl.Length})");

                        // Execute the DDL in the target
                        Log($"[SchemaMigration] Executing DDL on target...");
                        using (var command = targetConn.CreateCommand())
                        {
                            command.CommandText = createTableDdl;
                            command.CommandTimeout = _commandTimeoutSeconds;
                            await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "MigrateSchemaAsync.CreateTable");
                        }
                        Log($"[SchemaMigration] Table created successfully");
                    }

                    // Create primary keys and unique constraints with full column information
                    var constraintsWithColumns = await GetTableConstraintsWithColumnsAsync(sourceConn, source.DatabaseType, 
                        table.Schema, table.TableName);
                    
                    Log($"[SchemaMigration] Found {constraintsWithColumns.Count} constraints to migrate");
                    
                    foreach (var constraint in constraintsWithColumns)
                    {
                        if (string.IsNullOrWhiteSpace(constraint.ConstraintName) ||
                            string.IsNullOrWhiteSpace(constraint.ConstraintType))
                        {
                            // Skip malformed constraint metadata from the source.
                            continue;
                        }

                        string constraintTypeUpper = constraint.ConstraintType.ToUpperInvariant();
                        string generatedConstraintName = GenerateConstraintName(
                            constraint.ConstraintName,
                            target.DatabaseType,
                            table.Schema,
                            table.TableName,
                            constraintTypeUpper);

                        bool constraintExists = await ConstraintExistsAsync(
                            targetConn,
                            target.DatabaseType,
                            table.Schema,
                            table.TableName,
                            generatedConstraintName,
                            constraintTypeUpper);

                        if (constraintExists)
                        {
                            Log($"[SchemaMigration] Constraint already exists, skipping: {generatedConstraintName} ({constraintTypeUpper}) on {table.Schema}.{table.TableName}");
                            continue;
                        }

                        string constraintDdl = BuildConstraintDdl(constraint, target.DatabaseType, table.Schema, table.TableName);
                        if (string.IsNullOrEmpty(constraintDdl))
                        {
                            continue;
                        }

                        using (var command = targetConn.CreateCommand())
                        {
                            command.CommandText = constraintDdl;
                            command.CommandTimeout = _commandTimeoutSeconds;

                            try
                            {
                                await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "MigrateSchemaAsync.AddConstraint");
                                Log($"[SchemaMigration] Constraint created: {generatedConstraintName} ({constraintTypeUpper}) on {table.Schema}.{table.TableName}");
                                constraintsAdded.Add(new ConstraintAddedInfo(
                                    table.Schema,
                                    table.TableName,
                                    generatedConstraintName,
                                    constraintTypeUpper));
                            }
                            catch (Exception ex)
                            {
                                // Fail fast: if we couldn't create a constraint that didn't exist,
                                // we should abort the schema migration to avoid silent partial schemas.
                                throw new InvalidOperationException(
                                    $"Failed to create constraint '{generatedConstraintName}' ({constraintTypeUpper}) on {table.Schema}.{table.TableName}. {ex.Message}",
                                    ex);
                            }
                        }
                    }
                    Log($"[SchemaMigration] Migration completed for {table.Schema}.{table.TableName}");
                }
                catch (Exception ex)
                {
                    Log($"[SchemaMigration] ERROR migrating {table.Schema}.{table.TableName}: {ex.Message}");
                    Log($"[SchemaMigration] Stack trace: {ex.StackTrace}");
                    throw;
                }
            }
        }

        return new SchemaMigrationResult(constraintsAdded);
    }

    private async Task<bool> TableExistsAsync(DbConnection connection, DatabaseType dbType, 
        string schema, string tableName)
    {
        try
        {
            // Use parameterized queries to prevent SQL injection
            string query = dbType switch
            {
                DatabaseType.SqlServer => 
                    "SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName",
                DatabaseType.PostgreSQL => 
                    "SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @tableName",
                DatabaseType.Oracle => 
                    // For Oracle: the target "schema" is the connected user, so use USER_TABLES.
                    // This avoids false positives from other owners visible in ALL_TABLES.
                    "SELECT 1 FROM user_tables WHERE table_name = :tableName",
                _ => throw new NotSupportedException()
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = query;
                command.CommandTimeout = _commandTimeoutSeconds;
                
                // Add parameters to prevent SQL injection
                if (dbType != DatabaseType.Oracle)
                {
                    var schemaParam = command.CreateParameter();
                    schemaParam.ParameterName = "@schema";
                    // PostgreSQL: information_schema stores names lowercased for identifiers we create
                    // via FormatTableName (Oracle/SQL Server sources often report uppercase names).
                    schemaParam.Value = dbType == DatabaseType.PostgreSQL
                        ? schema.ToLowerInvariant()
                        : schema;
                    command.Parameters.Add(schemaParam);
                }
                
                var tableParam = command.CreateParameter();
                tableParam.ParameterName = dbType == DatabaseType.Oracle ? ":tableName" : "@tableName";
                tableParam.Value = dbType == DatabaseType.Oracle
                    ? tableName.ToUpperInvariant()
                    : dbType == DatabaseType.PostgreSQL
                        ? tableName.ToLowerInvariant()
                        : tableName;
                command.Parameters.Add(tableParam);
                
                var result = await command.ExecuteScalarAsync();
                return result != null;
            }
        }
        catch (Exception ex)
        {
            Log($"[TableExistsAsync] Error checking if table exists: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConstraintExistsAsync(
        DbConnection connection,
        DatabaseType dbType,
        string schema,
        string tableName,
        string constraintName,
        string constraintTypeUpper)
    {
        // constraintTypeUpper is expected to be "PRIMARY KEY" or "UNIQUE"
        if (string.IsNullOrWhiteSpace(constraintName) || string.IsNullOrWhiteSpace(constraintTypeUpper))
            return false;

        string query = dbType switch
        {
            DatabaseType.Oracle => @"
                SELECT 1
                FROM user_constraints
                WHERE constraint_name = :constraintName
                  AND table_name = UPPER(:tableName)
                  AND constraint_type = :constraintType",
            DatabaseType.SqlServer => @"
                SELECT 1
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @tableName
                  AND CONSTRAINT_NAME = @constraintName
                  AND CONSTRAINT_TYPE = @constraintType",
            DatabaseType.PostgreSQL => @"
                SELECT 1
                FROM information_schema.table_constraints
                WHERE table_schema = @schema
                  AND table_name = @tableName
                  AND constraint_name = @constraintName
                  AND constraint_type = @constraintType",
            _ => throw new NotSupportedException()
        };

        using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.CommandTimeout = _commandTimeoutSeconds;

            if (dbType == DatabaseType.Oracle)
            {
                string oracleConstraintType = constraintTypeUpper switch
                {
                    "PRIMARY KEY" => "P",
                    "UNIQUE" => "U",
                    _ => ""
                };

                if (string.IsNullOrWhiteSpace(oracleConstraintType))
                    return false;

                var cnParam = command.CreateParameter();
                cnParam.ParameterName = ":constraintName";
                cnParam.Value = EscapeOracleIdentifier(constraintName);
                command.Parameters.Add(cnParam);

                var tnParam = command.CreateParameter();
                tnParam.ParameterName = ":tableName";
                tnParam.Value = tableName.ToUpperInvariant();
                command.Parameters.Add(tnParam);

                var ctParam = command.CreateParameter();
                ctParam.ParameterName = ":constraintType";
                ctParam.Value = oracleConstraintType;
                command.Parameters.Add(ctParam);
            }
            else if (dbType == DatabaseType.PostgreSQL)
            {
                var schemaParam = command.CreateParameter();
                schemaParam.ParameterName = "@schema";
                schemaParam.Value = schema.ToLowerInvariant();
                command.Parameters.Add(schemaParam);

                var tableParam = command.CreateParameter();
                tableParam.ParameterName = "@tableName";
                tableParam.Value = tableName.ToLowerInvariant();
                command.Parameters.Add(tableParam);

                var constraintNameParam = command.CreateParameter();
                constraintNameParam.ParameterName = "@constraintName";
                constraintNameParam.Value = constraintName.ToLowerInvariant();
                command.Parameters.Add(constraintNameParam);

                var constraintTypeParam = command.CreateParameter();
                constraintTypeParam.ParameterName = "@constraintType";
                constraintTypeParam.Value = constraintTypeUpper;
                command.Parameters.Add(constraintTypeParam);
            }
            else
            {
                var schemaParam = command.CreateParameter();
                schemaParam.ParameterName = "@schema";
                schemaParam.Value = schema;
                command.Parameters.Add(schemaParam);

                var tableParam = command.CreateParameter();
                tableParam.ParameterName = "@tableName";
                tableParam.Value = tableName;
                command.Parameters.Add(tableParam);

                var constraintNameParam = command.CreateParameter();
                constraintNameParam.ParameterName = "@constraintName";
                constraintNameParam.Value = constraintName;
                command.Parameters.Add(constraintNameParam);

                var constraintTypeParam = command.CreateParameter();
                constraintTypeParam.ParameterName = "@constraintType";
                constraintTypeParam.Value = constraintTypeUpper;
                command.Parameters.Add(constraintTypeParam);
            }

            var result = await command.ExecuteScalarAsync();
            return result != null;
        }
    }

    private async Task<List<ColumnDefinition>> GetTableColumnsAsync(DbConnection connection, 
        DatabaseType dbType, string schema, string tableName)
    {
        var columns = new List<ColumnDefinition>();
        
        string query = dbType switch
        {
            DatabaseType.SqlServer => GetSqlServerColumnsQuery(),
            DatabaseType.PostgreSQL => GetPostgresColumnsQuery(),
            DatabaseType.Oracle => GetOracleColumnsQuery(),
            _ => throw new NotSupportedException()
        };

        using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.CommandTimeout = _commandTimeoutSeconds;

            var schemaParam = command.CreateParameter();
            schemaParam.ParameterName = dbType == DatabaseType.Oracle ? ":schema" : "@schema";
            schemaParam.Value = dbType == DatabaseType.Oracle ? schema.ToUpperInvariant() : schema;
            command.Parameters.Add(schemaParam);

            var tableParam = command.CreateParameter();
            tableParam.ParameterName = dbType == DatabaseType.Oracle ? ":tableName" : "@tableName";
            tableParam.Value = dbType == DatabaseType.Oracle ? tableName.ToUpperInvariant() : tableName;
            command.Parameters.Add(tableParam);

            using (var reader = await ExecuteWithRetryAsync(() => command.ExecuteReaderAsync(), "GetTableColumnsAsync.ExecuteReader"))
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(new ColumnDefinition
                    {
                        Name = reader["ColumnName"].ToString() ?? "",
                        DataType = reader["DataType"].ToString() ?? "",
                        IsNullable = Convert.ToBoolean(reader["IsNullable"]),
                        MaxLength = reader["MaxLength"] != DBNull.Value 
                            ? Convert.ToInt32(reader["MaxLength"]) 
                            : null,
                        NumericPrecision = reader["Precision"] != DBNull.Value 
                            ? Convert.ToInt32(reader["Precision"]) 
                            : null,
                        NumericScale = reader["Scale"] != DBNull.Value 
                            ? Convert.ToInt32(reader["Scale"]) 
                            : null,
                        DateTimePrecision = reader["DateTimePrecision"] != DBNull.Value 
                            ? Convert.ToInt32(reader["DateTimePrecision"]) 
                            : null,
                        DefaultValue = reader["DefaultValue"]?.ToString(),
                        SourceDbType = dbType
                    });
                }
            }
        }

        return columns;
    }

    /// <summary>
    /// Represents a constraint with its columns
    /// </summary>
    private class ConstraintInfo
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string ConstraintType { get; set; } = string.Empty;  // PRIMARY KEY, UNIQUE, FOREIGN KEY
        public List<string> Columns { get; set; } = new List<string>();
        public string? ReferencedTable { get; set; }
        public string? ReferencedSchema { get; set; }
        public List<string> ReferencedColumns { get; set; } = new List<string>();
    }

    private async Task<List<ConstraintInfo>> GetTableConstraintsWithColumnsAsync(DbConnection connection, 
        DatabaseType dbType, string schema, string tableName)
    {
        var constraints = new Dictionary<string, ConstraintInfo>();
        
        // Parameterized queries are used below to prevent SQL injection
        string query = dbType switch
        {
            DatabaseType.SqlServer => GetSqlServerConstraintsWithColumnsQuery(),
            DatabaseType.PostgreSQL => GetPostgresConstraintsWithColumnsQuery(),
            DatabaseType.Oracle => GetOracleConstraintsWithColumnsQuery(),
            _ => throw new NotSupportedException()
        };

        using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.CommandTimeout = _commandTimeoutSeconds;
            
            // Use parameters to prevent SQL injection
            var schemaParam = command.CreateParameter();
            schemaParam.ParameterName = dbType == DatabaseType.Oracle ? ":schema" : "@schema";
            schemaParam.Value = schema;
            command.Parameters.Add(schemaParam);
            
            var tableParam = command.CreateParameter();
            tableParam.ParameterName = dbType == DatabaseType.Oracle ? ":tableName" : "@tableName";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);

            using (var reader = await ExecuteWithRetryAsync(() => command.ExecuteReaderAsync(), "GetTableConstraintsWithColumnsAsync.ExecuteReader"))
            {
                while (await reader.ReadAsync())
                {
                    var constraintName = reader["ConstraintName"].ToString() ?? "";
                    var constraintType = reader["ConstraintType"].ToString() ?? "";
                    var columnName = reader["ColumnName"].ToString() ?? "";
                    
                    if (!constraints.ContainsKey(constraintName))
                    {
                        constraints[constraintName] = new ConstraintInfo
                        {
                            ConstraintName = constraintName,
                            ConstraintType = constraintType
                        };
                    }
                    
                    constraints[constraintName].Columns.Add(columnName);
                }
            }
        }

        return constraints.Values.ToList();
    }

    private string GetSqlServerConstraintsWithColumnsQuery()
    {
        return @"
            SELECT 
                tc.CONSTRAINT_NAME as ConstraintName,
                tc.CONSTRAINT_TYPE as ConstraintType,
                ccu.COLUMN_NAME as ColumnName
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu 
                ON tc.CONSTRAINT_NAME = ccu.CONSTRAINT_NAME 
                AND tc.TABLE_SCHEMA = ccu.TABLE_SCHEMA
                AND tc.TABLE_NAME = ccu.TABLE_NAME
            WHERE tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @tableName
            AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE')
            ORDER BY tc.CONSTRAINT_NAME, ccu.COLUMN_NAME";
    }

    private string GetPostgresConstraintsWithColumnsQuery()
    {
        return @"
            SELECT 
                tc.constraint_name as ConstraintName,
                tc.constraint_type as ConstraintType,
                kcu.column_name as ColumnName
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name 
                AND tc.table_schema = kcu.table_schema
                AND tc.table_name = kcu.table_name
            WHERE tc.table_schema = @schema AND tc.table_name = @tableName
            AND tc.constraint_type IN ('PRIMARY KEY', 'UNIQUE')
            ORDER BY tc.constraint_name, kcu.ordinal_position";
    }

    private string GetOracleConstraintsWithColumnsQuery()
    {
        // Oracle stores identifiers in uppercase by default
        // Use UPPER() to handle case-insensitive matching
        return @"
            SELECT 
                c.constraint_name as ConstraintName,
                CASE c.constraint_type 
                    WHEN 'P' THEN 'PRIMARY KEY' 
                    WHEN 'U' THEN 'UNIQUE' 
                END as ConstraintType,
                cc.column_name as ColumnName
            FROM all_constraints c
            JOIN all_cons_columns cc 
                ON c.constraint_name = cc.constraint_name 
                AND c.owner = cc.owner
            WHERE c.owner = UPPER(:schema) AND c.table_name = UPPER(:tableName)
            AND c.constraint_type IN ('P', 'U')
            ORDER BY c.constraint_name, cc.position";
    }

    private string BuildCreateTableStatement(DatabaseType targetDbType, string schema, 
        string tableName, List<ColumnDefinition> columns)
    {
        var sb = new System.Text.StringBuilder();

        string tableRef = FormatTableName(targetDbType, schema, tableName);
        sb.AppendLine($"CREATE TABLE {tableRef} (");

        var columnDefs = new List<string>();
        foreach (var col in columns)
        {
            string colDef = FormatColumnDefinition(targetDbType, col);
            columnDefs.Add(colDef);
        }

        // Create rows with correct indentation
        for (int i = 0; i < columnDefs.Count; i++)
        {
            sb.Append("    " + columnDefs[i]);
            if (i < columnDefs.Count - 1)
                sb.Append(",");
            sb.AppendLine();
        }
        
        // Oracle doesn't like semicolon at the end of CREATE TABLE in this context
        sb.AppendLine(targetDbType == DatabaseType.Oracle ? ")" : ");");

        return sb.ToString();
    }

    /// <summary>
    /// Detects T-SQL function-call defaults (e.g. getutcdate()) anywhere in the expression text.
    /// </summary>
    private static bool IsSqlServerFunctionCallDefault(string defaultVal)
    {
        var lower = defaultVal.ToLowerInvariant();
        return SqlServerBuiltinDefaultFunctionNames.Any(f =>
            Regex.IsMatch(lower, @"(?<!\w)" + Regex.Escape(f) + @"\s*\("));
    }

    /// <summary>
    /// Strips redundant outer parentheses from catalog default text, e.g. "(getutcdate())" → "getutcdate()".
    /// </summary>
    private static string NormalizeDefaultExpressionParens(string s)
    {
        s = s.Trim();
        while (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
        {
            var inner = s.Substring(1, s.Length - 2).Trim();
            if (!AreParensBalanced(inner))
                break;
            s = inner;
        }

        return s;
    }

    private static bool AreParensBalanced(string s)
    {
        int depth = 0;
        foreach (var c in s)
        {
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    /// <summary>
    /// Maps common SQL Server column default expressions to PostgreSQL when migrating SqlServer → PostgreSQL.
    /// </summary>
    private static string? TryMapSqlServerDefaultToPostgreSql(string defaultVal)
    {
        var expr = NormalizeDefaultExpressionParens(defaultVal);
        var lower = expr.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"^getutcdate\s*\(\s*\)$") ||
            Regex.IsMatch(lower, @"^sysutcdatetime\s*\(\s*\)$"))
            return "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')";
        if (Regex.IsMatch(lower, @"^getdate\s*\(\s*\)$") ||
            Regex.IsMatch(lower, @"^sysdatetime\s*\(\s*\)$"))
            return "CURRENT_TIMESTAMP";
        if (Regex.IsMatch(lower, @"^sysdatetimeoffset\s*\(\s*\)$"))
            return "CURRENT_TIMESTAMP";
        if (Regex.IsMatch(lower, @"^newid\s*\(\s*\)$") ||
            Regex.IsMatch(lower, @"^newsequentialid\s*\(\s*\)$"))
            return "gen_random_uuid()";
        return null;
    }

    private string FormatColumnDefinition(DatabaseType dbType, ColumnDefinition column)
    {
        string colName = FormatColumnName(dbType, column.Name);
        string dataType = MapDataType(column.SourceDbType, dbType, column.DataType, 
            column.MaxLength, column.NumericPrecision, column.NumericScale, column.DateTimePrecision);
        
        // Oracle has different NULL/NOT NULL semantics:
        // - Columns are nullable by default in Oracle
        // - Only specify NOT NULL explicitly when needed
        // For other databases: always specify NULL/NOT NULL explicitly
        string nullable = dbType != DatabaseType.Oracle
            ? (column.IsNullable ? " NULL" : " NOT NULL")
            : (!column.IsNullable ? " NOT NULL" : "");
        
        string defaultValue = "";
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            var defaultVal = column.DefaultValue.Trim();

            if (dbType == DatabaseType.PostgreSQL && column.SourceDbType == DatabaseType.SqlServer)
            {
                var mapped = TryMapSqlServerDefaultToPostgreSql(defaultVal);
                if (mapped != null)
                    defaultValue = $" DEFAULT {mapped}";
                else if (IsSqlServerFunctionCallDefault(defaultVal))
                    defaultValue = "";
                else
                    defaultValue = $" DEFAULT {defaultVal}";
            }
            else if (dbType == DatabaseType.Oracle)
            {
                if (!IsSqlServerFunctionCallDefault(defaultVal))
                    defaultValue = $" DEFAULT {defaultVal}";
            }
            else
            {
                defaultValue = $" DEFAULT {column.DefaultValue}";
            }
        }

        return $"{colName} {dataType}{nullable}{defaultValue}";
    }

    private string MapDataType(DatabaseType sourceDbType, DatabaseType targetDbType, 
        string sourceDataType, int? maxLength, int? precision, int? scale, int? dateTimePrecision)
    {
        // Normalize the source data type
        string normalized = sourceDataType.ToLowerInvariant().Trim();

        // Handle SQL Server MAX length (-1 means MAX)
        bool isMaxLength = maxLength.HasValue && maxLength == -1;

        // Same database type: preserve original types with their sizes
        if (sourceDbType == targetDbType)
        {
            return BuildSameDbTypeMapping(sourceDbType, normalized, maxLength, precision, scale, isMaxLength, dateTimePrecision);
        }

        // Mapping cross-database
        if (sourceDbType == DatabaseType.SqlServer && targetDbType == DatabaseType.PostgreSQL)
        {
            return normalized switch
            {
                "int" => "integer",
                "bigint" => "bigint",
                "smallint" => "smallint",
                "tinyint" => "smallint",
                "decimal" or "numeric" => precision.HasValue 
                    ? $"numeric({precision},{scale ?? 0})" 
                    : "numeric",
                "float" => "double precision",
                "real" => "real",
                "money" => "numeric(19,4)",
                "smallmoney" => "numeric(10,4)",
                "varchar" => isMaxLength ? "text" : (maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text"),
                "nvarchar" => isMaxLength ? "text" : (maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})"  // PostgreSQL varchar is already Unicode, no division needed
                    : "text"),
                "char" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})" 
                    : "char(1)",
                "nchar" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})"  // PostgreSQL char is already Unicode
                    : "char(1)",
                "text" => "text",
                "ntext" => "text",
                "datetime" => "timestamp(3)",  // datetime has ~3.33ms precision
                "datetime2" => dateTimePrecision.HasValue 
                    ? $"timestamp({Math.Min(dateTimePrecision.Value, 6)})"  // PostgreSQL max is 6
                    : "timestamp(6)",
                "smalldatetime" => "timestamp(0)",
                "date" => "date",
                "time" => dateTimePrecision.HasValue 
                    ? $"time({Math.Min(dateTimePrecision.Value, 6)})" 
                    : "time(6)",
                "datetimeoffset" => dateTimePrecision.HasValue 
                    ? $"timestamptz({Math.Min(dateTimePrecision.Value, 6)})" 
                    : "timestamptz(6)",
                "bit" => "boolean",
                "binary" => maxLength.HasValue && maxLength > 0 ? "bytea" : "bytea",
                "varbinary" => "bytea",
                "image" => "bytea",
                "uniqueidentifier" => "uuid",
                "xml" => "xml",
                "sql_variant" => "text",
                _ => normalized
            };
        }

        if (sourceDbType == DatabaseType.SqlServer && targetDbType == DatabaseType.Oracle)
        {
            return normalized switch
            {
                "int" => "NUMBER(10)",
                "bigint" => "NUMBER(19)",
                "smallint" => "NUMBER(5)",
                "tinyint" => "NUMBER(3)",
                "decimal" or "numeric" => precision.HasValue 
                    ? $"NUMBER({precision},{scale ?? 0})" 
                    : "NUMBER",
                "float" => precision.HasValue && precision <= 24 ? "BINARY_FLOAT" : "BINARY_DOUBLE",
                "real" => "BINARY_FLOAT",
                "money" => "NUMBER(19,4)",
                "smallmoney" => "NUMBER(10,4)",
                "varchar" => isMaxLength ? "CLOB" : (maxLength.HasValue && maxLength > 0 
                    ? $"VARCHAR2({Math.Min(maxLength.Value, 4000)})" 
                    : "VARCHAR2(4000)"),
                "nvarchar" => isMaxLength ? "NCLOB" : (maxLength.HasValue && maxLength > 0 
                    ? $"NVARCHAR2({Math.Min(maxLength.Value, 2000)})"  // Oracle NVARCHAR2 max is 2000
                    : "NVARCHAR2(2000)"),
                "char" => maxLength.HasValue && maxLength > 0
                    ? $"CHAR({Math.Min(maxLength.Value, 2000)})" 
                    : "CHAR(1)",
                "nchar" => maxLength.HasValue && maxLength > 0
                    ? $"NCHAR({Math.Min(maxLength.Value, 1000)})"  // Oracle NCHAR max is 1000
                    : "NCHAR(1)",
                "text" => "CLOB",
                "ntext" => "NCLOB",
                "datetime" => "TIMESTAMP(3)",  // datetime has ~3.33ms precision
                "datetime2" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)})"  // Oracle max is 9
                    : "TIMESTAMP(6)",
                "smalldatetime" => "TIMESTAMP(0)",
                "date" => "DATE",
                "time" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)})" 
                    : "TIMESTAMP(0)",  // Oracle doesn't have TIME, use TIMESTAMP
                "datetimeoffset" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)}) WITH TIME ZONE" 
                    : "TIMESTAMP(6) WITH TIME ZONE",
                "bit" => "NUMBER(1)",
                "binary" => maxLength.HasValue && maxLength > 0 
                    ? $"RAW({Math.Min(maxLength.Value, 2000)})" 
                    : "RAW(1)",
                "varbinary" => isMaxLength ? "BLOB" : (maxLength.HasValue && maxLength > 0 
                    ? $"RAW({Math.Min(maxLength.Value, 2000)})" 
                    : "RAW(2000)"),
                "image" => "BLOB",
                "uniqueidentifier" => "RAW(16)",
                "xml" => "XMLTYPE",
                _ => "VARCHAR2(4000)"
            };
        }

        if (sourceDbType == DatabaseType.PostgreSQL && targetDbType == DatabaseType.SqlServer)
        {
            return normalized switch
            {
                "integer" or "int" or "int4" => "int",
                "bigint" or "int8" => "bigint",
                "smallint" or "int2" => "smallint",
                "numeric" or "decimal" => precision.HasValue 
                    ? $"decimal({precision},{scale ?? 0})" 
                    : "decimal(18,2)",
                "double precision" or "float8" => "float",
                "real" or "float4" => "real",
                "money" => "decimal(19,4)",
                "varchar" or "character varying" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "varchar(max)",
                "text" => "varchar(max)",
                "char" or "character" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})" 
                    : "char(1)",
                "boolean" or "bool" => "bit",
                "bytea" => "varbinary(max)",
                "uuid" => "uniqueidentifier",
                "timestamp" or "timestamp without time zone" => dateTimePrecision.HasValue 
                    ? $"datetime2({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "datetime2(6)",
                "timestamptz" or "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"datetimeoffset({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "datetimeoffset(6)",
                "date" => "date",
                "time" or "time without time zone" => dateTimePrecision.HasValue 
                    ? $"time({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "time(6)",
                "timetz" or "time with time zone" => dateTimePrecision.HasValue 
                    ? $"time({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "time(6)",  // SQL Server doesn't have time with tz
                "interval" => "varchar(100)",  // No direct equivalent
                "json" or "jsonb" => "nvarchar(max)",
                "xml" => "xml",
                "serial" => "int",  // IDENTITY will be handled separately
                "bigserial" => "bigint",
                "smallserial" => "smallint",
                _ => "varchar(max)"
            };
        }

        if (sourceDbType == DatabaseType.PostgreSQL && targetDbType == DatabaseType.Oracle)
        {
            return normalized switch
            {
                "integer" or "int" or "int4" => "NUMBER(10)",
                "bigint" or "int8" => "NUMBER(19)",
                "smallint" or "int2" => "NUMBER(5)",
                "numeric" or "decimal" => precision.HasValue 
                    ? $"NUMBER({precision},{scale ?? 0})" 
                    : "NUMBER",
                "double precision" or "float8" => "BINARY_DOUBLE",
                "real" or "float4" => "BINARY_FLOAT",
                "money" => "NUMBER(19,4)",
                "varchar" or "character varying" => maxLength.HasValue && maxLength > 0 
                    ? $"VARCHAR2({Math.Min(maxLength.Value, 4000)})" 
                    : "VARCHAR2(4000)",
                "text" => "CLOB",
                "char" or "character" => maxLength.HasValue && maxLength > 0
                    ? $"CHAR({Math.Min(maxLength.Value, 2000)})" 
                    : "CHAR(1)",
                "boolean" or "bool" => "NUMBER(1)",
                "bytea" => "BLOB",
                "uuid" => "RAW(16)",
                "timestamp" or "timestamp without time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)})" 
                    : "TIMESTAMP(6)",
                "timestamptz" or "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)}) WITH TIME ZONE" 
                    : "TIMESTAMP(6) WITH TIME ZONE",
                "date" => "DATE",
                "time" or "time without time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)})" 
                    : "TIMESTAMP(0)",  // Oracle doesn't have TIME type
                "timetz" or "time with time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({Math.Min(dateTimePrecision.Value, 9)}) WITH TIME ZONE" 
                    : "TIMESTAMP(0) WITH TIME ZONE",
                "interval" => "INTERVAL DAY TO SECOND",
                "json" or "jsonb" => "CLOB",  // Oracle 21c+ has JSON, but CLOB is safer
                "xml" => "XMLTYPE",
                "serial" => "NUMBER(10)",
                "bigserial" => "NUMBER(19)",
                "smallserial" => "NUMBER(5)",
                _ => "VARCHAR2(4000)"
            };
        }

        if (sourceDbType == DatabaseType.Oracle && targetDbType == DatabaseType.SqlServer)
        {
            return normalized switch
            {
                "number" => precision.HasValue 
                    ? (scale.HasValue && scale > 0 
                        ? $"decimal({precision},{scale})" 
                        : (precision <= 10 ? "int" : (precision <= 19 ? "bigint" : $"decimal({precision},0)")))
                    : "decimal(18,2)",
                "integer" => "int",
                "float" => precision.HasValue 
                    ? (precision <= 24 ? "real" : "float") 
                    : "float",
                "binary_float" => "real",
                "binary_double" => "float",
                "varchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "varchar(max)",
                "nvarchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"nvarchar({maxLength})" 
                    : "nvarchar(max)",
                "char" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})" 
                    : "char(1)",
                "nchar" => maxLength.HasValue && maxLength > 0
                    ? $"nchar({maxLength})" 
                    : "nchar(1)",
                "clob" => "varchar(max)",
                "nclob" => "nvarchar(max)",
                "blob" => "varbinary(max)",
                "long" => "varchar(max)",
                "long raw" => "varbinary(max)",
                "date" => "datetime2(0)",  // Oracle DATE has second precision
                "timestamp" => dateTimePrecision.HasValue 
                    ? $"datetime2({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "datetime2(6)",
                "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"datetimeoffset({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "datetimeoffset(6)",
                "timestamp with local time zone" => dateTimePrecision.HasValue 
                    ? $"datetimeoffset({Math.Min(dateTimePrecision.Value, 7)})" 
                    : "datetimeoffset(6)",
                "interval year to month" => "varchar(50)",
                "interval day to second" => "varchar(50)",
                "raw" => maxLength.HasValue && maxLength > 0 
                    ? $"varbinary({maxLength})" 
                    : "varbinary(max)",
                "rowid" => "varchar(18)",
                "urowid" => "varchar(4000)",
                "xmltype" => "xml",
                "bfile" => "varbinary(max)",
                _ => "varchar(max)"
            };
        }

        if (sourceDbType == DatabaseType.Oracle && targetDbType == DatabaseType.PostgreSQL)
        {
            return normalized switch
            {
                "number" => precision.HasValue 
                    ? (scale.HasValue && scale > 0 
                        ? $"numeric({precision},{scale})" 
                        : (precision <= 5 ? "smallint" : (precision <= 10 ? "integer" : (precision <= 19 ? "bigint" : $"numeric({precision},0)"))))
                    : "numeric",
                "integer" => "integer",
                "float" => precision.HasValue 
                    ? (precision <= 24 ? "real" : "double precision") 
                    : "double precision",
                "binary_float" => "real",
                "binary_double" => "double precision",
                "varchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text",
                "nvarchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text",
                "char" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})" 
                    : "char(1)",
                "nchar" => maxLength.HasValue && maxLength > 0
                    ? $"char({maxLength})" 
                    : "char(1)",
                "clob" => "text",
                "nclob" => "text",
                "blob" => "bytea",
                "long" => "text",
                "long raw" => "bytea",
                "date" => "timestamp(0)",  // Oracle DATE has second precision
                "timestamp" => dateTimePrecision.HasValue 
                    ? $"timestamp({Math.Min(dateTimePrecision.Value, 6)})" 
                    : "timestamp(6)",
                "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"timestamptz({Math.Min(dateTimePrecision.Value, 6)})" 
                    : "timestamptz(6)",
                "timestamp with local time zone" => dateTimePrecision.HasValue 
                    ? $"timestamptz({Math.Min(dateTimePrecision.Value, 6)})" 
                    : "timestamptz(6)",
                "interval year to month" => "interval",
                "interval day to second" => "interval",
                "raw" => "bytea",
                "rowid" => "varchar(18)",
                "urowid" => "varchar(4000)",
                "xmltype" => "xml",
                "bfile" => "bytea",
                _ => "text"
            };
        }

        // Default fallback
        return normalized;
    }

    /// <summary>
    /// Builds type mapping when source and target are the same database type.
    /// Preserves original types with correct sizes, handling special cases like MAX.
    /// </summary>
    private string BuildSameDbTypeMapping(DatabaseType dbType, string normalized, 
        int? maxLength, int? precision, int? scale, bool isMaxLength, int? dateTimePrecision)
    {
        if (dbType == DatabaseType.SqlServer)
        {
            return normalized switch
            {
                "varchar" => isMaxLength ? "varchar(max)" : (maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" : "varchar(max)"),
                "nvarchar" => isMaxLength ? "nvarchar(max)" : (maxLength.HasValue && maxLength > 0 
                    ? $"nvarchar({maxLength})" : "nvarchar(max)"),
                "char" => maxLength.HasValue && maxLength > 0 ? $"char({maxLength})" : "char(1)",
                "nchar" => maxLength.HasValue && maxLength > 0 ? $"nchar({maxLength})" : "nchar(1)",
                "varbinary" => isMaxLength ? "varbinary(max)" : (maxLength.HasValue && maxLength > 0 
                    ? $"varbinary({maxLength})" : "varbinary(max)"),
                "binary" => maxLength.HasValue && maxLength > 0 ? $"binary({maxLength})" : "binary(1)",
                "decimal" or "numeric" => precision.HasValue 
                    ? $"{normalized}({precision},{scale ?? 0})" : $"{normalized}(18,0)",
                "float" => precision.HasValue && precision <= 53 ? $"float({precision})" : "float",
                // DateTime types with precision preservation
                "datetime2" => dateTimePrecision.HasValue ? $"datetime2({dateTimePrecision})" : "datetime2(7)",
                "time" => dateTimePrecision.HasValue ? $"time({dateTimePrecision})" : "time(7)",
                "datetimeoffset" => dateTimePrecision.HasValue ? $"datetimeoffset({dateTimePrecision})" : "datetimeoffset(7)",
                // Types that don't need size specification
                "int" or "bigint" or "smallint" or "tinyint" or "bit" => normalized,
                "money" or "smallmoney" => normalized,
                "real" => normalized,
                "date" or "datetime" or "smalldatetime" => normalized,
                "text" or "ntext" or "image" => normalized,
                "uniqueidentifier" or "xml" or "sql_variant" => normalized,
                _ => normalized
            };
        }
        
        if (dbType == DatabaseType.PostgreSQL)
        {
            return normalized switch
            {
                "varchar" or "character varying" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" : "text",
                "char" or "character" => maxLength.HasValue && maxLength > 0 
                    ? $"char({maxLength})" : "char(1)",
                "numeric" or "decimal" => precision.HasValue 
                    ? $"numeric({precision},{scale ?? 0})" : "numeric",
                // DateTime types with precision preservation (PostgreSQL max is 6)
                "timestamp" or "timestamp without time zone" => dateTimePrecision.HasValue 
                    ? $"timestamp({dateTimePrecision})" : "timestamp(6)",
                "timestamptz" or "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"timestamptz({dateTimePrecision})" : "timestamptz(6)",
                "time" or "time without time zone" => dateTimePrecision.HasValue 
                    ? $"time({dateTimePrecision})" : "time(6)",
                "timetz" or "time with time zone" => dateTimePrecision.HasValue 
                    ? $"timetz({dateTimePrecision})" : "timetz(6)",
                "interval" => dateTimePrecision.HasValue 
                    ? $"interval({dateTimePrecision})" : "interval",
                _ => normalized
            };
        }
        
        if (dbType == DatabaseType.Oracle)
        {
            return normalized switch
            {
                "varchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"VARCHAR2({Math.Min(maxLength.Value, 4000)})" : "VARCHAR2(4000)",
                "nvarchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"NVARCHAR2({Math.Min(maxLength.Value, 2000)})" : "NVARCHAR2(2000)",
                "char" => maxLength.HasValue && maxLength > 0 
                    ? $"CHAR({Math.Min(maxLength.Value, 2000)})" : "CHAR(1)",
                "nchar" => maxLength.HasValue && maxLength > 0 
                    ? $"NCHAR({Math.Min(maxLength.Value, 1000)})" : "NCHAR(1)",
                "number" => precision.HasValue 
                    ? $"NUMBER({precision},{scale ?? 0})" : "NUMBER",
                "float" => precision.HasValue 
                    ? $"FLOAT({precision})" : "FLOAT",
                "raw" => maxLength.HasValue && maxLength > 0 
                    ? $"RAW({Math.Min(maxLength.Value, 2000)})" : "RAW(2000)",
                // DateTime types with precision preservation (Oracle max is 9)
                "timestamp" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({dateTimePrecision})" : "TIMESTAMP(6)",
                "timestamp with time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({dateTimePrecision}) WITH TIME ZONE" : "TIMESTAMP(6) WITH TIME ZONE",
                "timestamp with local time zone" => dateTimePrecision.HasValue 
                    ? $"TIMESTAMP({dateTimePrecision}) WITH LOCAL TIME ZONE" : "TIMESTAMP(6) WITH LOCAL TIME ZONE",
                "interval day to second" => dateTimePrecision.HasValue 
                    ? $"INTERVAL DAY TO SECOND({dateTimePrecision})" : "INTERVAL DAY TO SECOND",
                _ => normalized.ToUpperInvariant()
            };
        }
        
        return normalized;
    }

    /// <summary>
    /// Builds a constraint DDL statement (PRIMARY KEY or UNIQUE) for the target database
    /// </summary>
    private string BuildConstraintDdl(ConstraintInfo constraint, DatabaseType targetDbType, 
        string schema, string tableName)
    {
        if (constraint.Columns.Count == 0)
        {
            Log($"[BuildConstraintDdl] No columns for constraint {constraint.ConstraintName}, skipping");
            return "";
        }

        string constraintType = constraint.ConstraintType.ToUpperInvariant();
        
        // Format column names appropriately for each database
        // Oracle uses uppercase identifiers, PostgreSQL uses lowercase
        var formattedColumns = constraint.Columns
            .Select(col => FormatColumnName(targetDbType, col))
            .ToList();
        string columnList = string.Join(", ", formattedColumns);

        // Format table reference for target database
        string tableRef = FormatTableName(targetDbType, schema, tableName);

        // Generate constraint name (ensuring it's valid for target DB)
        string constraintName = GenerateConstraintName(constraint.ConstraintName, targetDbType, schema, tableName, constraintType);

        string ddl = targetDbType switch
        {
            DatabaseType.SqlServer => 
                $"ALTER TABLE {tableRef} ADD CONSTRAINT [{EscapeSqlServerIdentifier(constraintName)}] {constraintType} ({columnList})",
            DatabaseType.PostgreSQL => 
                $"ALTER TABLE {tableRef} ADD CONSTRAINT \"{EscapePostgresIdentifier(constraintName)}\" {constraintType} ({columnList})",
            DatabaseType.Oracle => 
                $"ALTER TABLE {tableRef} ADD CONSTRAINT \"{EscapeOracleIdentifier(constraintName)}\" {constraintType} ({columnList})",
            _ => throw new NotSupportedException()
        };

        Log($"[BuildConstraintDdl] Generated {constraintType} DDL for {tableName} (len={ddl.Length})");
        return ddl;
    }

    /// <summary>
    /// Generates a valid constraint name for the target database
    /// </summary>
    private string GenerateConstraintName(string originalName, DatabaseType targetDbType, 
        string schema, string tableName, string constraintType)
    {
        // Use original name if it's valid, otherwise generate a new one
        string baseName = !string.IsNullOrEmpty(originalName) ? originalName : 
            $"{(constraintType == "PRIMARY KEY" ? "PK" : "UQ")}_{tableName}";

        // Apply case conventions based on target database
        baseName = targetDbType switch
        {
            DatabaseType.Oracle => baseName.ToUpperInvariant(),
            DatabaseType.PostgreSQL => baseName.ToLowerInvariant(),
            _ => baseName  // SQL Server is case-insensitive
        };

        // Track if we need to truncate (to add collision prevention)
        bool needsTruncation = false;
        int maxLength = 0;

        // Oracle: Use 30-char limit for maximum compatibility with all Oracle versions.
        // Note: Oracle 12.2+ supports 128 chars, but we intentionally use the conservative
        // 30-char limit to ensure compatibility with older Oracle versions without requiring
        // runtime version detection.
        if (targetDbType == DatabaseType.Oracle && baseName.Length > 30)
        {
            needsTruncation = true;
            maxLength = 30;
        }
        // SQL Server has 128 character limit
        else if (targetDbType == DatabaseType.SqlServer && baseName.Length > 128)
        {
            needsTruncation = true;
            maxLength = 128;
        }
        // PostgreSQL has 63 character limit
        else if (targetDbType == DatabaseType.PostgreSQL && baseName.Length > 63)
        {
            needsTruncation = true;
            maxLength = 63;
        }

        // If truncation is needed, add a hash suffix to reduce collision risk
        if (needsTruncation)
        {
            // Generate a stable hash from the full name to ensure uniqueness across migrations
            string hashSuffix = GetStableHashSuffix(baseName);
            
            // Reserve space for the hash suffix
            int truncateLength = maxLength - hashSuffix.Length;
            baseName = truncateLength > 0
                ? baseName[..truncateLength] + hashSuffix
                : baseName[..maxLength];
        }

        return baseName;
    }

    private string GetSqlServerColumnsQuery()
    {
        return @"
            SELECT 
                COLUMN_NAME as ColumnName,
                DATA_TYPE as DataType,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END as IsNullable,
                CHARACTER_MAXIMUM_LENGTH as MaxLength,
                NUMERIC_PRECISION as Precision,
                NUMERIC_SCALE as Scale,
                DATETIME_PRECISION as DateTimePrecision,
                COLUMN_DEFAULT as DefaultValue
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION";
    }

    private string GetPostgresColumnsQuery()
    {
        return @"
            SELECT 
                column_name as ColumnName,
                data_type as DataType,
                CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END as IsNullable,
                character_maximum_length as MaxLength,
                numeric_precision as Precision,
                numeric_scale as Scale,
                datetime_precision as DateTimePrecision,
                column_default as DefaultValue
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @tableName
            ORDER BY ordinal_position";
    }

    private string GetOracleColumnsQuery()
    {
        return @"
            SELECT 
                column_name as ColumnName,
                data_type as DataType,
                CASE WHEN nullable = 'Y' THEN 1 ELSE 0 END as IsNullable,
                data_length as MaxLength,
                data_precision as Precision,
                data_scale as Scale,
                CASE WHEN data_type LIKE 'TIMESTAMP%' THEN data_scale ELSE NULL END as DateTimePrecision,
                data_default as DefaultValue
            FROM all_tab_columns
            WHERE owner = :schema AND table_name = :tableName
            ORDER BY column_id";
    }

    private string GetSqlServerConstraintsQuery()
    {
        return @"
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
            AND CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')";
    }

    private string GetPostgresConstraintsQuery()
    {
        return @"
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE table_schema = @schema AND table_name = @tableName
            AND constraint_type IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')";
    }

    private string GetOracleConstraintsQuery()
    {
        return @"
            SELECT constraint_name
            FROM all_constraints
            WHERE owner = :schema AND table_name = :tableName
            AND constraint_type IN ('P', 'U', 'R')";
    }

    /// <summary>
    /// Generates a stable hash suffix for constraint names to ensure uniqueness across migrations.
    /// Uses SHA256 for deterministic hashing that remains consistent across application runs.
    /// </summary>
    private string GetStableHashSuffix(string input)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        // Use first 4 bytes and convert to a 4-digit number for the suffix
        // Use unsigned int to avoid Math.Abs overflow with int.MinValue
        uint hashValue = BitConverter.ToUInt32(hashBytes, 0);
        return $"_{hashValue % 10000:D4}";
    }
}

/// <summary>
/// Column definition for schema migration
/// </summary>
internal class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
    public int? DateTimePrecision { get; set; }  // Fractional seconds precision for datetime/time types as reported by the source database
    public string? DefaultValue { get; set; }
    public DatabaseType SourceDbType { get; set; }
}
