using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// Service for schema migration (DDL) between different database types.
/// Handles cross-database data type mapping.
/// </summary>
public class SchemaMigrationService
{
    private const int CommandTimeout = 300;

    private static void Log(string message) => LoggerService.Log(message);

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
            await connection.OpenAsync();
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
                await connection.OpenAsync();
                
                string dropQuery = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => $"DROP TABLE [{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(tableName)}]",
                    DatabaseType.PostgreSQL => $"DROP TABLE \"{EscapePostgresIdentifier(schema)}\".\"{EscapePostgresIdentifier(tableName)}\"",
                    DatabaseType.Oracle => $"DROP TABLE {tableName.ToUpperInvariant()}",
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = dropQuery;
                    command.CommandTimeout = CommandTimeout;
                    await command.ExecuteNonQueryAsync();
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
    /// Creates the schema in the target database based on the source database.
    /// Handles automatic data type mapping.
    /// </summary>
    public async Task MigrateSchemaAsync(ConnectionInfo source, ConnectionInfo target, 
        List<TableInfo> tablesToMigrate)
    {
        using (var sourceConn = CreateConnection(source))
        using (var targetConn = CreateConnection(target))
        {
            await sourceConn.OpenAsync();
            await targetConn.OpenAsync();

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
                        Log($"[SchemaMigration] DDL:\n{createTableDdl}");

                        // Execute the DDL in the target
                        Log($"[SchemaMigration] Executing DDL on target...");
                        using (var command = targetConn.CreateCommand())
                        {
                            command.CommandText = createTableDdl;
                            command.CommandTimeout = CommandTimeout;
                            await command.ExecuteNonQueryAsync();
                        }
                        Log($"[SchemaMigration] Table created successfully");
                    }

                    // Create primary keys and unique constraints with full column information
                    var constraintsWithColumns = await GetTableConstraintsWithColumnsAsync(sourceConn, source.DatabaseType, 
                        table.Schema, table.TableName);
                    
                    Log($"[SchemaMigration] Found {constraintsWithColumns.Count} constraints to migrate");
                    
                    foreach (var constraint in constraintsWithColumns)
                    {
                        string constraintDdl = BuildConstraintDdl(constraint, target.DatabaseType, 
                            table.Schema, table.TableName);
                        
                        if (!string.IsNullOrEmpty(constraintDdl))
                        {
                            using (var command = targetConn.CreateCommand())
                            {
                                command.CommandText = constraintDdl;
                                command.CommandTimeout = CommandTimeout;
                                try
                                {
                                    Log($"[SchemaMigration] Executing constraint: {constraintDdl}");
                                    await command.ExecuteNonQueryAsync();
                                    Log($"[SchemaMigration] Constraint {constraint.ConstraintName} ({constraint.ConstraintType}) created successfully");
                                }
                                catch (Exception ex)
                                {
                                    Log($"[SchemaMigration] Constraint failed (ignored): {ex.Message}");
                                    // Some constraints may fail, continue
                                }
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
                    // For Oracle: search only by table name (independent of source schema)
                    // because migrated tables go into the connected user's schema
                    "SELECT 1 FROM all_tables WHERE table_name = :tableName",
                _ => throw new NotSupportedException()
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = query;
                command.CommandTimeout = CommandTimeout;
                
                // Add parameters to prevent SQL injection
                if (dbType != DatabaseType.Oracle)
                {
                    var schemaParam = command.CreateParameter();
                    schemaParam.ParameterName = "@schema";
                    schemaParam.Value = schema;
                    command.Parameters.Add(schemaParam);
                }
                
                var tableParam = command.CreateParameter();
                tableParam.ParameterName = dbType == DatabaseType.Oracle ? ":tableName" : "@tableName";
                tableParam.Value = dbType == DatabaseType.Oracle ? tableName.ToUpperInvariant() : tableName;
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

    private async Task<List<ColumnDefinition>> GetTableColumnsAsync(DbConnection connection, 
        DatabaseType dbType, string schema, string tableName)
    {
        var columns = new List<ColumnDefinition>();
        
        string query = dbType switch
        {
            DatabaseType.SqlServer => GetSqlServerColumnsQuery(schema, tableName),
            DatabaseType.PostgreSQL => GetPostgresColumnsQuery(schema, tableName),
            DatabaseType.Oracle => GetOracleColumnsQuery(schema, tableName),
            _ => throw new NotSupportedException()
        };

        using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.CommandTimeout = CommandTimeout;

            using (var reader = await command.ExecuteReaderAsync())
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
        
        // Validate identifiers to prevent SQL injection - defense in depth
        schema = ValidateIdentifier(schema, nameof(schema));
        tableName = ValidateIdentifier(tableName, nameof(tableName));
        
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
            command.CommandTimeout = CommandTimeout;
            
            // Use parameters to prevent SQL injection
            var schemaParam = command.CreateParameter();
            schemaParam.ParameterName = dbType == DatabaseType.Oracle ? ":schema" : "@schema";
            schemaParam.Value = schema;
            command.Parameters.Add(schemaParam);
            
            var tableParam = command.CreateParameter();
            tableParam.ParameterName = dbType == DatabaseType.Oracle ? ":tableName" : "@tableName";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);

            using (var reader = await command.ExecuteReaderAsync())
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

    private async Task<List<string>> GetTableConstraintsAsync(DbConnection connection, 
        DatabaseType dbType, string schema, string tableName)
    {
        var constraints = new List<string>();
        
        string query = dbType switch
        {
            DatabaseType.SqlServer => GetSqlServerConstraintsQuery(schema, tableName),
            DatabaseType.PostgreSQL => GetPostgresConstraintsQuery(schema, tableName),
            DatabaseType.Oracle => GetOracleConstraintsQuery(schema, tableName),
            _ => throw new NotSupportedException()
        };

        using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.CommandTimeout = CommandTimeout;

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    constraints.Add(reader[0].ToString() ?? "");
                }
            }
        }

        return constraints;
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
        if (targetDbType == DatabaseType.Oracle)
            sb.AppendLine(")");
        else
            sb.AppendLine(");");

        return sb.ToString();
    }

    private string FormatColumnDefinition(DatabaseType dbType, ColumnDefinition column)
    {
        string colName = FormatColumnName(dbType, column.Name);
        string dataType = MapDataType(column.SourceDbType, dbType, column.DataType, 
            column.MaxLength, column.NumericPrecision, column.NumericScale);
        
        // Oracle has different NULL/NOT NULL semantics:
        // - Columns are nullable by default in Oracle
        // - Only specify NOT NULL explicitly when needed
        // For other databases: always specify NULL/NOT NULL explicitly
        string nullable = "";
        if (dbType != DatabaseType.Oracle)
        {
            nullable = column.IsNullable ? " NULL" : " NOT NULL";
        }
        else if (!column.IsNullable)
        {
            nullable = " NOT NULL";  // Oracle: only specify if NOT NULL
        }
        
        // For Oracle: ignore DEFAULT values that are SQL Server functions
        string defaultValue = "";
        if (dbType != DatabaseType.Oracle && !string.IsNullOrEmpty(column.DefaultValue))
        {
            // For SQL Server and PostgreSQL, keep the default
            defaultValue = $" DEFAULT {column.DefaultValue}";
        }
        else if (dbType == DatabaseType.Oracle && !string.IsNullOrEmpty(column.DefaultValue))
        {
            // For Oracle: accept only literal values, not functions like (getutcdate())
            var defaultVal = column.DefaultValue.Trim();
            
            // More robust function detection:
            // - Look for known SQL Server function names used as function calls
            //   (function name followed by optional whitespace and an opening parenthesis)
            var sqlServerFunctions = new[] { "getdate", "getutcdate", "newid", "sysdatetime", "sysutcdatetime" };
            var lowerDefaultVal = defaultVal.ToLowerInvariant();
            bool isFunction = sqlServerFunctions.Any(f =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    lowerDefaultVal,
                    @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(f) + @"\s*\("));
            
            if (!isFunction)
            {
                defaultValue = $" DEFAULT {defaultVal}";
            }
            // If it's a function, skip it - Oracle doesn't support SQL Server functions
        }

        return $"{colName} {dataType}{nullable}{defaultValue}";
    }

    private string MapDataType(DatabaseType sourceDbType, DatabaseType targetDbType, 
        string sourceDataType, int? maxLength, int? precision, int? scale)
    {
        // Normalize the source data type
        string normalized = sourceDataType.ToLowerInvariant().Trim();

        // Handle SQL Server MAX length (-1 means MAX)
        bool isMaxLength = maxLength.HasValue && maxLength == -1;

        // Same database type: preserve original types with their sizes
        if (sourceDbType == targetDbType)
        {
            return BuildSameDbTypeMapping(sourceDbType, normalized, maxLength, precision, scale, isMaxLength);
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
                "decimal" => precision.HasValue 
                    ? $"numeric({precision},{scale ?? 0})" 
                    : "numeric",
                "float" => "double precision",
                "real" => "real",
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
                "datetime" => "timestamp",
                "datetime2" => "timestamp",
                "smalldatetime" => "timestamp",
                "date" => "date",
                "time" => "time",
                "bit" => "boolean",
                "binary" => "bytea",
                "varbinary" => "bytea",
                "uniqueidentifier" => "uuid",
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
                "decimal" => precision.HasValue 
                    ? $"NUMBER({precision},{scale ?? 0})" 
                    : "NUMBER",
                "float" => "BINARY_DOUBLE",
                "real" => "BINARY_FLOAT",
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
                "datetime" => "TIMESTAMP(6)",
                "datetime2" => "TIMESTAMP(6)",
                "smalldatetime" => "TIMESTAMP(0)",
                "date" => "DATE",
                "time" => "TIMESTAMP(0)",
                "bit" => "NUMBER(1)",
                "binary" => "RAW",
                "varbinary" => isMaxLength ? "BLOB" : "RAW(2000)",
                "uniqueidentifier" => "RAW(16)",
                _ => "VARCHAR2(4000)"
            };
        }

        if (sourceDbType == DatabaseType.PostgreSQL && targetDbType == DatabaseType.SqlServer)
        {
            return normalized switch
            {
                "integer" => "int",
                "bigint" => "bigint",
                "smallint" => "smallint",
                "numeric" => precision.HasValue 
                    ? $"decimal({precision},{scale ?? 0})" 
                    : "decimal(18,2)",
                "double precision" => "float",
                "real" => "real",
                "varchar" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "varchar(max)",
                "text" => "varchar(max)",
                "char" => maxLength.HasValue 
                    ? $"char({maxLength})" 
                    : "char(10)",
                "boolean" => "bit",
                "bytea" => "varbinary(max)",
                "uuid" => "uniqueidentifier",
                "timestamp" => "datetime2",
                "date" => "date",
                "time" => "time",
                _ => "varchar(max)"
            };
        }

        if (sourceDbType == DatabaseType.PostgreSQL && targetDbType == DatabaseType.Oracle)
        {
            return normalized switch
            {
                "integer" => "NUMBER(10)",
                "bigint" => "NUMBER(19)",
                "smallint" => "NUMBER(5)",
                "numeric" => precision.HasValue 
                    ? $"NUMBER({precision},{scale ?? 0})" 
                    : "NUMBER",
                "double precision" => "BINARY_DOUBLE",
                "real" => "BINARY_FLOAT",
                "varchar" => maxLength.HasValue && maxLength > 0 
                    ? $"VARCHAR2({maxLength})" 
                    : "VARCHAR2(4000)",
                "text" => "CLOB",
                "char" => maxLength.HasValue 
                    ? $"CHAR({maxLength})" 
                    : "CHAR(10)",
                "boolean" => "NUMBER(1)",
                "bytea" => "BLOB",
                "uuid" => "RAW(16)",
                "timestamp" => "TIMESTAMP",
                "date" => "DATE",
                "time" => "TIMESTAMP",
                _ => "VARCHAR2(4000)"
            };
        }

        if (sourceDbType == DatabaseType.Oracle && targetDbType == DatabaseType.SqlServer)
        {
            return normalized switch
            {
                "number" => precision.HasValue 
                    ? $"decimal({precision},{scale ?? 0})" 
                    : "decimal(18,2)",
                "integer" => "int",
                "float" => "float",
                "binary_float" => "real",
                "binary_double" => "float",
                "varchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "varchar(max)",
                "nvarchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"nvarchar({maxLength})" 
                    : "nvarchar(max)",
                "char" => maxLength.HasValue 
                    ? $"char({maxLength})" 
                    : "char(10)",
                "nchar" => maxLength.HasValue 
                    ? $"nchar({maxLength})" 
                    : "nchar(10)",
                "clob" => "varchar(max)",
                "nclob" => "nvarchar(max)",
                "blob" => "varbinary(max)",
                "date" => "datetime2",
                "timestamp" => "datetime2",
                "raw" => "varbinary(max)",
                _ => "varchar(max)"
            };
        }

        if (sourceDbType == DatabaseType.Oracle && targetDbType == DatabaseType.PostgreSQL)
        {
            return normalized switch
            {
                "number" => precision.HasValue 
                    ? $"numeric({precision},{scale ?? 0})" 
                    : "numeric",
                "integer" => "integer",
                "float" => "double precision",
                "binary_float" => "real",
                "binary_double" => "double precision",
                "varchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text",
                "nvarchar2" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text",
                "char" => maxLength.HasValue 
                    ? $"char({maxLength})" 
                    : "char(10)",
                "nchar" => maxLength.HasValue 
                    ? $"char({maxLength})" 
                    : "char(10)",
                "clob" => "text",
                "nclob" => "text",
                "blob" => "bytea",
                "date" => "timestamp",
                "timestamp" => "timestamp",
                "raw" => "bytea",
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
        int? maxLength, int? precision, int? scale, bool isMaxLength)
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
                "float" => precision.HasValue ? $"float({precision})" : "float",
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
                "raw" => maxLength.HasValue && maxLength > 0 
                    ? $"RAW({Math.Min(maxLength.Value, 2000)})" : "RAW(2000)",
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
            .Select(col => FormatColumnNameForTarget(targetDbType, col))
            .ToList();
        string columnList = string.Join(", ", formattedColumns);

        // Format table reference for target database
        string tableRef = FormatTableNameForTarget(targetDbType, schema, tableName);

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

        Log($"[BuildConstraintDdl] Generated DDL for {constraintType}: {ddl}");
        return ddl;
    }

    /// <summary>
    /// Formats a column name for the target database with proper quoting and case
    /// </summary>
    private string FormatColumnNameForTarget(DatabaseType targetDbType, string columnName)
    {
        return targetDbType switch
        {
            DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(columnName)}]",
            DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(columnName.ToLowerInvariant())}\"",  // PostgreSQL prefers lowercase
            DatabaseType.Oracle => columnName.ToUpperInvariant(),  // Oracle uses uppercase
            _ => columnName
        };
    }

    /// <summary>
    /// Formats a table reference for the target database with proper schema handling
    /// </summary>
    private string FormatTableNameForTarget(DatabaseType targetDbType, string schema, string tableName)
    {
        return targetDbType switch
        {
            DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(tableName)}]",
            DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(schema.ToLowerInvariant())}\".\"{EscapePostgresIdentifier(tableName.ToLowerInvariant())}\"",
            DatabaseType.Oracle => tableName.ToUpperInvariant(),  // Oracle: just table name, uppercase
            _ => throw new NotSupportedException()
        };
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

    private string TranslateConstraintDdl(string sourceDdl, DatabaseType targetDbType, 
        DatabaseType sourceDbType, string schema, string tableName)
    {
        // This method is kept for backward compatibility but is no longer used
        // The new BuildConstraintDdl method is used instead
        return "";
    }

    private string FormatTableName(DatabaseType dbType, string schema, string tableName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(schema)}].[{EscapeSqlServerIdentifier(tableName)}]",
            DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(schema.ToLowerInvariant())}\".\"{EscapePostgresIdentifier(tableName.ToLowerInvariant())}\"",
            DatabaseType.Oracle => tableName.ToUpperInvariant(),  // Oracle: uppercase, no schema prefix
            _ => throw new NotSupportedException()
        };
    }

    private string FormatColumnName(DatabaseType dbType, string columnName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(columnName)}]",
            DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(columnName.ToLowerInvariant())}\"",  // PostgreSQL: lowercase
            DatabaseType.Oracle => columnName.ToUpperInvariant(),  // Oracle: uppercase
            _ => columnName
        };
    }

    private string GetSqlServerColumnsQuery(string schema, string tableName)
    {
        return $@"
            SELECT 
                COLUMN_NAME as ColumnName,
                DATA_TYPE as DataType,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END as IsNullable,
                CHARACTER_MAXIMUM_LENGTH as MaxLength,
                NUMERIC_PRECISION as Precision,
                NUMERIC_SCALE as Scale,
                COLUMN_DEFAULT as DefaultValue
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{tableName}'
            ORDER BY ORDINAL_POSITION";
    }

    private string GetPostgresColumnsQuery(string schema, string tableName)
    {
        return $@"
            SELECT 
                column_name as ColumnName,
                data_type as DataType,
                CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END as IsNullable,
                character_maximum_length as MaxLength,
                numeric_precision as Precision,
                numeric_scale as Scale,
                column_default as DefaultValue
            FROM information_schema.columns
            WHERE table_schema = '{schema}' AND table_name = '{tableName}'
            ORDER BY ordinal_position";
    }

    private string GetOracleColumnsQuery(string schema, string tableName)
    {
        return $@"
            SELECT 
                column_name as ColumnName,
                data_type as DataType,
                CASE WHEN nullable = 'Y' THEN 1 ELSE 0 END as IsNullable,
                data_length as MaxLength,
                data_precision as Precision,
                data_scale as Scale,
                data_default as DefaultValue
            FROM all_tab_columns
            WHERE owner = '{schema}' AND table_name = '{tableName}'
            ORDER BY column_id";
    }

    private string GetSqlServerConstraintsQuery(string schema, string tableName)
    {
        return $@"
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{tableName}'
            AND CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')";
    }

    private string GetPostgresConstraintsQuery(string schema, string tableName)
    {
        return $@"
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE table_schema = '{schema}' AND table_name = '{tableName}'
            AND constraint_type IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')";
    }

    private string GetOracleConstraintsQuery(string schema, string tableName)
    {
        return $@"
            SELECT constraint_name
            FROM all_constraints
            WHERE owner = '{schema}' AND table_name = '{tableName}'
            AND constraint_type IN ('P', 'U', 'R')";
    }

    private DbConnection CreateConnection(ConnectionInfo connectionInfo) => connectionInfo.DatabaseType switch
    {
        DatabaseType.SqlServer => new SqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.PostgreSQL => new NpgsqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.Oracle => new OracleConnection(connectionInfo.GetConnectionString()),
        _ => throw new NotSupportedException()
    };

    /// <summary>
    /// Escapes a SQL Server identifier by replacing ] with ]]
    /// </summary>
    private string EscapeSqlServerIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        
        // SQL Server uses square brackets, escape ] by doubling it
        return identifier.Replace("]", "]]");
    }

    /// <summary>
    /// Escapes a PostgreSQL identifier by replacing " with ""
    /// </summary>
    private string EscapePostgresIdentifier(string identifier)
    {
        // PostgreSQL uses double quotes, escape " by doubling it
        return EscapeQuotedIdentifier(identifier);
    }

    /// <summary>
    /// Escapes an Oracle identifier by replacing " with ""
    /// </summary>
    private string EscapeOracleIdentifier(string identifier)
    {
        // Oracle uses double quotes for quoted identifiers, escape " by doubling it
        return EscapeQuotedIdentifier(identifier);
    }

    /// <summary>
    /// Escapes a double-quoted identifier by replacing " with ""
    /// This is used for PostgreSQL and Oracle identifiers
    /// </summary>
    private string EscapeQuotedIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;
        
        // Escape double quote by doubling it
        return identifier.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Validates database identifiers to prevent SQL injection by enforcing a strict character set.
    /// Only allows alphanumeric characters and underscores - the most conservative approach for database identifiers.
    /// This is a defense-in-depth measure for identifiers used in dynamic SQL construction.
    /// Note: This may reject legitimate identifiers containing special characters that are valid when quoted.
    /// </summary>
    private string ValidateIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);

        // Allow only alphanumeric characters and underscore - most conservative for database identifiers.
        // This is a defense-in-depth measure for identifiers used in dynamic SQL.
        // Use foreach for early exit on first invalid character for better performance
        foreach (char c in identifier)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                Log($"[ValidateIdentifier] WARNING: Identifier '{identifier}' contains potentially unsafe characters");
                throw new ArgumentException(
                    $"{parameterName} contains invalid characters. Only letters, digits, and underscores are allowed. " +
                    $"Provided: '{identifier}'", parameterName);
            }
        }

        return identifier;
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
    public string? DefaultValue { get; set; }
    public DatabaseType SourceDbType { get; set; }
}
