using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
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
    /// Crea lo schema nel database target basato sul database sorgente.
    /// Gestisce il mapping automatico dei tipi dati.
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
                    
                    // Verifica se la tabella esiste già nel target
                    Log($"[SchemaMigration] Checking if table exists in target...");
                    bool tableExists = await TableExistsAsync(targetConn, target.DatabaseType, 
                        table.Schema, table.TableName);
                    
                    if (tableExists)
                    {
                        Log($"[SchemaMigration] Table {table.Schema}.{table.TableName} already exists in target, skipping creation");
                    }
                    else
                    {
                        // Recupera la definizione della tabella dalla sorgente
                        Log($"[SchemaMigration] Fetching columns from source...");
                        var columns = await GetTableColumnsAsync(sourceConn, source.DatabaseType, 
                            table.Schema, table.TableName);
                        Log($"[SchemaMigration] Found {columns.Count} columns");

                        // Costruisci il DDL per il target
                        Log($"[SchemaMigration] Building CREATE TABLE statement for target...");
                        string createTableDdl = BuildCreateTableStatement(target.DatabaseType, 
                            table.Schema, table.TableName, columns);
                        Log($"[SchemaMigration] DDL:\n{createTableDdl}");

                        // Esegui il DDL nel target
                        Log($"[SchemaMigration] Executing DDL on target...");
                        using (var command = targetConn.CreateCommand())
                        {
                            command.CommandText = createTableDdl;
                            command.CommandTimeout = CommandTimeout;
                            await command.ExecuteNonQueryAsync();
                        }
                        Log($"[SchemaMigration] Table created successfully");
                    }

                    // Crea gli indici primari e vincoli
                    var constraints = await GetTableConstraintsAsync(sourceConn, source.DatabaseType, 
                        table.Schema, table.TableName);
                    
                    foreach (var constraint in constraints)
                    {
                        string constraintDdl = TranslateConstraintDdl(constraint, target.DatabaseType, 
                            source.DatabaseType, table.Schema, table.TableName);
                        
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
                                }
                                catch (Exception ex)
                                {
                                    Log($"[SchemaMigration] Constraint failed (ignored): {ex.Message}");
                                    // Alcuni vincoli potrebbero fallire, continua
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

        // Crea le righe con indentazione corretta
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
            // - Contains parentheses (function calls)
            // - Contains common SQL Server function names
            var sqlServerFunctions = new[] { "getdate", "getutcdate", "newid", "sysdatetime", "sysutcdatetime" };
            bool isFunction = defaultVal.Contains("(") || defaultVal.Contains(")") ||
                             sqlServerFunctions.Any(f => defaultVal.ToLowerInvariant().Contains(f));
            
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
        // Normalizza il tipo di dato sorgente
        string normalized = sourceDataType.ToLowerInvariant().Trim();

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
                "varchar" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength})" 
                    : "text",
                "nvarchar" => maxLength.HasValue && maxLength > 0 
                    ? $"varchar({maxLength / 2})" 
                    : "text",
                "char" => maxLength.HasValue 
                    ? $"char({maxLength})" 
                    : "char(10)",
                "nchar" => maxLength.HasValue 
                    ? $"char({maxLength / 2})" 
                    : "char(10)",
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
                "varchar" => maxLength.HasValue && maxLength > 0 
                    ? $"VARCHAR2({maxLength})" 
                    : "VARCHAR2(4000)",
                "nvarchar" => maxLength.HasValue && maxLength > 0 
                    ? $"NVARCHAR2({maxLength / 2})" 
                    : "NVARCHAR2(2000)",
                "char" => maxLength.HasValue 
                    ? $"CHAR({maxLength})" 
                    : "CHAR(10)",
                "nchar" => maxLength.HasValue 
                    ? $"NCHAR({maxLength / 2})" 
                    : "NCHAR(10)",
                "text" => "CLOB",
                "ntext" => "NCLOB",
                "datetime" => "TIMESTAMP(6)",
                "datetime2" => "TIMESTAMP(6)",
                "smalldatetime" => "TIMESTAMP(0)",
                "date" => "DATE",
                "time" => "TIMESTAMP(0)",
                "bit" => "NUMBER(1)",
                "binary" => "RAW",
                "varbinary" => "BLOB",
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

    private string TranslateConstraintDdl(string sourceDdl, DatabaseType targetDbType, 
        DatabaseType sourceDbType, string schema, string tableName)
    {
        // Questa è una semplificazione. In produzione, parseremmo il DDL completo
        // Per ora ritorniamo una stringa vuota per vincoli complessi
        return "";
    }

    private string FormatTableName(DatabaseType dbType, string schema, string tableName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{schema}].[{tableName}]",
            DatabaseType.PostgreSQL => $"\"{schema}\".\"{tableName}\"",
            DatabaseType.Oracle => tableName,  // Oracle: usa solo il table name, non schema.table
            _ => throw new NotSupportedException()
        };
    }

    private string FormatColumnName(DatabaseType dbType, string columnName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{columnName}]",
            DatabaseType.PostgreSQL => $"\"{columnName}\"",
            DatabaseType.Oracle => columnName,
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
}

/// <summary>
/// Definizione di una colonna per schema migration
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
