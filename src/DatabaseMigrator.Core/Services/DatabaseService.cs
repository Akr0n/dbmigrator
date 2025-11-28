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

public class DatabaseService : IDatabaseService
{
    private const int BatchSize = 1000;
    private const int CommandTimeout = 300; // 5 minuti
    
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatabaseMigrator", "debug.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}\n";
            File.AppendAllText(LogPath, fullMessage);
            System.Diagnostics.Debug.WriteLine(fullMessage);
        }
        catch { }
    }

    public async Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            Log($"TestConnectionAsync started for {connectionInfo.DatabaseType}");
            var connStr = connectionInfo.GetConnectionString();
            Log($"Connection string: {connStr}");
            
            using (var connection = CreateConnection(connectionInfo))
            {
                Log($"Opening connection...");
                await connection.OpenAsync();
                Log($"Connection opened successfully!");
                return connection.State == ConnectionState.Open;
            }
        }
        catch (Exception ex)
        {
            Log($"Connection error: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo)
    {
        var tables = new List<TableInfo>();
        
        try
        {
            Log($"GetTablesAsync started for {connectionInfo.DatabaseType}");
            
            using (var connection = CreateConnection(connectionInfo))
            {
                await connection.OpenAsync();
                Log($"Connection opened for GetTablesAsync");

                string query = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => @"
                        SELECT TABLE_SCHEMA, TABLE_NAME
                        FROM INFORMATION_SCHEMA.TABLES t
                        WHERE TABLE_TYPE = 'BASE TABLE'
                        ORDER BY TABLE_SCHEMA, TABLE_NAME",
                    
                    DatabaseType.PostgreSQL => @"
                        SELECT schemaname, tablename
                        FROM pg_tables
                        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                        ORDER BY schemaname, tablename",
                    
                    DatabaseType.Oracle => @"
                        SELECT owner, table_name
                        FROM all_tables
                        WHERE owner NOT IN ('SYS', 'SYSTEM', 'XDB', 'APEX_030200')
                        ORDER BY owner, table_name",
                    
                    _ => throw new NotSupportedException($"Database type {connectionInfo.DatabaseType} not supported")
                };

                Log($"Executing query for tables...");
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = CommandTimeout;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string schema = reader[0].ToString() ?? "dbo";
                            string tableName = reader[1].ToString() ?? "";
                            
                            Log($"Found table: {schema}.{tableName}");
                            
                            tables.Add(new TableInfo
                            {
                                Schema = schema,
                                TableName = tableName,
                                RowCount = 0,
                                IsSelected = false
                            });
                        }
                    }
                }

                Log($"Retrieved {tables.Count} tables");

                // Ottieni row count per ogni tabella
                foreach (var table in tables)
                {
                    table.RowCount = await GetTableRowCountAsync(connectionInfo, table.Schema, table.TableName);
                    Log($"Row count for {table.Schema}.{table.TableName}: {table.RowCount}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"GetTablesAsync error: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            throw new InvalidOperationException($"Errore nel recupero tabelle: {ex.Message}", ex);
        }

        return tables;
    }

    public async Task<bool> DatabaseExistsAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            // Crea una connessione senza specificare il database
            var connInfo = new ConnectionInfo
            {
                DatabaseType = connectionInfo.DatabaseType,
                Server = connectionInfo.Server,
                Port = connectionInfo.Port,
                Username = connectionInfo.Username,
                Password = connectionInfo.Password,
                Database = connectionInfo.DatabaseType == DatabaseType.Oracle ? "XE" : "master"
            };

            using (var connection = CreateConnection(connInfo))
            {
                await connection.OpenAsync();

                string query = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => 
                        $"SELECT 1 FROM sys.databases WHERE name = '{EscapeSqlString(connectionInfo.Database)}'",
                    
                    DatabaseType.PostgreSQL => 
                        $"SELECT 1 FROM pg_database WHERE datname = '{EscapeSqlString(connectionInfo.Database)}'",
                    
                    DatabaseType.Oracle => 
                        $"SELECT 1 FROM dba_databases WHERE name = '{EscapeSqlString(connectionInfo.Database)}'",
                    
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = CommandTimeout;
                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task CreateDatabaseAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            // Crea connessione al sistema database (senza database specifico)
            var systemConnInfo = new ConnectionInfo
            {
                DatabaseType = connectionInfo.DatabaseType,
                Server = connectionInfo.Server,
                Port = connectionInfo.Port,
                Username = connectionInfo.Username,
                Password = connectionInfo.Password,
                Database = connectionInfo.DatabaseType switch
                {
                    DatabaseType.Oracle => "XE",
                    DatabaseType.PostgreSQL => "postgres",  // PostgreSQL system database
                    _ => "master"  // SQL Server system database
                }
            };

            using (var connection = CreateConnection(systemConnInfo))
            {
                await connection.OpenAsync();

                string createQuery = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => 
                        $"CREATE DATABASE [{connectionInfo.Database}]",
                    
                    DatabaseType.PostgreSQL => 
                        $"CREATE DATABASE \"{connectionInfo.Database}\"",
                    
                    DatabaseType.Oracle => 
                        $"CREATE DATABASE {connectionInfo.Database}",
                    
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = createQuery;
                    command.CommandTimeout = CommandTimeout;
                    await command.ExecuteNonQueryAsync();
                    Log($"Database {connectionInfo.Database} created successfully");
                }
            }
        }
        catch (Exception ex)
        {
            // Se il database esiste già, ignora l'errore
            string errorMessage = ex.Message.ToLower();
            if (errorMessage.Contains("already exists") || 
                errorMessage.Contains("esiste già") ||
                errorMessage.Contains("42P06"))  // PostgreSQL error code for "database already exists"
            {
                Log($"Database {connectionInfo.Database} already exists, skipping creation");
                return;
            }
            // Se è un errore diverso, rethrow
            throw;
        }
    }

    public async Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, string tableName, string schema)
    {
        using (var connection = CreateConnection(connectionInfo))
        {
            await connection.OpenAsync();

            string query = connectionInfo.DatabaseType switch
            {
                DatabaseType.SqlServer => GetSqlServerTableSchema(schema, tableName),
                DatabaseType.PostgreSQL => GetPostgresTableSchema(schema, tableName),
                DatabaseType.Oracle => GetOracleTableSchema(schema, tableName),
                _ => throw new NotSupportedException()
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = query;
                command.CommandTimeout = CommandTimeout;

                var script = new System.Text.StringBuilder();
                script.AppendLine($"-- Tabella: {schema}.{tableName}");

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        script.AppendLine(reader[0].ToString());
                    }
                }

                return script.ToString();
            }
        }
    }

    public async Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, TableInfo table, IProgress<int> progress)
    {
        using (var sourceConn = CreateConnection(source))
        using (var targetConn = CreateConnection(target))
        {
            await sourceConn.OpenAsync();
            await targetConn.OpenAsync();

            // Tronca la tabella nel target per evitare duplicati
            Log($"[MigrateTableAsync] Truncating table {table.Schema}.{table.TableName} in target...");
            try
            {
                string truncateQuery = target.DatabaseType switch
                {
                    DatabaseType.SqlServer => $"TRUNCATE TABLE {FormatTableName(target.DatabaseType, table.Schema, table.TableName)}",
                    DatabaseType.PostgreSQL => $"TRUNCATE TABLE {FormatTableName(target.DatabaseType, table.Schema, table.TableName)} CASCADE",
                    DatabaseType.Oracle => $"TRUNCATE TABLE {FormatTableName(target.DatabaseType, table.Schema, table.TableName)}",
                    _ => throw new NotSupportedException()
                };
                
                using (var truncateCommand = targetConn.CreateCommand())
                {
                    truncateCommand.CommandText = truncateQuery;
                    truncateCommand.CommandTimeout = CommandTimeout;
                    await truncateCommand.ExecuteNonQueryAsync();
                }
                Log($"[MigrateTableAsync] Table truncated successfully");
            }
            catch (Exception ex)
            {
                Log($"[MigrateTableAsync] Warning: Could not truncate table: {ex.Message}");
                // Se TRUNCATE fallisce, continuiamo comunque (potrebbe essere una tabella vuota)
            }

            // Leggi i dati dalla sorgente in batch
            long totalRows = await GetTableRowCountAsync(source, table.Schema, table.TableName);
            long migratedRows = 0;

            string sourceQuery = $"SELECT * FROM {FormatTableName(source.DatabaseType, table.Schema, table.TableName)}";

            using (var sourceCommand = sourceConn.CreateCommand())
            {
                sourceCommand.CommandText = sourceQuery;
                sourceCommand.CommandTimeout = CommandTimeout;

                using (var reader = await sourceCommand.ExecuteReaderAsync())
                {
                    var batch = new DataTable();
                    batch.Load(reader);

                    if (batch.Rows.Count == 0)
                    {
                        progress?.Report(100);
                        return;
                    }

                    int columnCount = batch.Columns.Count;
                    int processedBatches = 0;
                    int totalBatches = (int)Math.Ceiling((double)batch.Rows.Count / BatchSize);

                    // Inserisci i dati nel target in batch
                    using (var targetCommand = targetConn.CreateCommand())
                    {
                        targetCommand.CommandTimeout = CommandTimeout;

                        for (int i = 0; i < batch.Rows.Count; i += BatchSize)
                        {
                            var batchRows = batch.Rows.Cast<DataRow>()
                                .Skip(i)
                                .Take(BatchSize)
                                .ToList();

                            string insertQuery = BuildInsertQuery(target.DatabaseType, table.Schema, table.TableName, batch.Columns, batchRows);
                            targetCommand.CommandText = insertQuery;

                            try
                            {
                                await targetCommand.ExecuteNonQueryAsync();
                                migratedRows += batchRows.Count;
                                processedBatches++;

                                int percentage = (int)((processedBatches / (double)totalBatches) * 100);
                                progress?.Report(Math.Min(percentage, 100));
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Errore durante l'inserimento batch per {table.Schema}.{table.TableName}: {ex.Message}", ex);
                            }
                        }
                    }
                }
            }

            progress?.Report(100);
        }
    }

    private async Task<long> GetTableRowCountAsync(ConnectionInfo connectionInfo, string schema, string tableName)
    {
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await connection.OpenAsync();

                string query = $"SELECT COUNT(*) FROM {FormatTableName(connectionInfo.DatabaseType, schema, tableName)}";

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = CommandTimeout;
                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt64(result ?? 0);
                }
            }
        }
        catch
        {
            return 0;
        }
    }

    private string FormatTableName(DatabaseType dbType, string schema, string tableName)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => $"[{schema}].[{tableName}]",
            DatabaseType.PostgreSQL => $"\"{schema}\".\"{tableName}\"",
            DatabaseType.Oracle => $"{schema}.{tableName}",
            _ => throw new NotSupportedException()
        };
    }

    private string BuildInsertQuery(DatabaseType dbType, string schema, string tableName, 
        DataColumnCollection columns, List<DataRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        string tableRef = FormatTableName(dbType, schema, tableName);

        // Costruisci colonne
        var columnNames = new List<string>();
        for (int i = 0; i < columns.Count; i++)
        {
            string colName = columns[i].ColumnName;
            columnNames.Add(dbType switch
            {
                DatabaseType.SqlServer => $"[{colName}]",
                DatabaseType.PostgreSQL => $"\"{colName}\"",
                DatabaseType.Oracle => colName,
                _ => colName
            });
        }

        sb.Append($"INSERT INTO {tableRef} ({string.Join(", ", columnNames)}) VALUES ");

        var valueSets = new List<string>();
        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var values = new List<string>();
            for (int colIdx = 0; colIdx < columns.Count; colIdx++)
            {
                var value = rows[rowIdx][colIdx];
                values.Add(FormatSqlValue(dbType, value));
            }
            valueSets.Add($"({string.Join(", ", values)})");
        }

        sb.Append(string.Join(", ", valueSets));
        string result = sb.ToString();
        Log($"[BuildInsertQuery] Generated INSERT query (first 200 chars): {result.Substring(0, Math.Min(200, result.Length))}");
        return result;
    }

    private string FormatSqlValue(DatabaseType dbType, object? value)
    {
        if (value == null || value is DBNull)
            return "NULL";

        return value switch
        {
            string s => $"'{EscapeSqlString(s)}'",
            bool b => b ? "1" : "0",
            byte[] bytes => dbType switch
            {
                DatabaseType.PostgreSQL => $"'\\x{BitConverter.ToString(bytes).Replace("-", "").ToLower()}'",
                DatabaseType.SqlServer => $"0x{BitConverter.ToString(bytes).Replace("-", "")}",
                DatabaseType.Oracle => $"hextoraw('{BitConverter.ToString(bytes).Replace("-", "")}')",
                _ => $"'{BitConverter.ToString(bytes)}'"
            },
            DateTime dt => dbType switch
            {
                DatabaseType.SqlServer => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
                DatabaseType.PostgreSQL => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
                DatabaseType.Oracle => $"TO_DATE('{dt:yyyy-MM-dd HH:mm:ss}', 'YYYY-MM-DD HH24:MI:SS')",
                _ => $"'{dt:yyyy-MM-dd HH:mm:ss}'"
            },
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "NULL"
        };
    }

    private string EscapeSqlString(string value)
    {
        return value.Replace("'", "''");
    }

    private string GetSqlServerTableSchema(string schema, string tableName)
    {
        return $@"
            SELECT 'CREATE TABLE [' + @schema + '].[' + @table + '] (' + 
                   STUFF((SELECT ', [' + c.COLUMN_NAME + '] ' + 
                          CASE WHEN c.DATA_TYPE IN ('int', 'bigint', 'smallint', 'tinyint', 'decimal', 'float', 'real') THEN c.DATA_TYPE
                               WHEN c.DATA_TYPE = 'varchar' THEN 'varchar(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar) + ')'
                               WHEN c.DATA_TYPE = 'nvarchar' THEN 'nvarchar(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar) + ')'
                               WHEN c.DATA_TYPE = 'char' THEN 'char(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar) + ')'
                               WHEN c.DATA_TYPE = 'nchar' THEN 'nchar(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar) + ')'
                               ELSE c.DATA_TYPE
                          END +
                          CASE WHEN c.IS_NULLABLE = 'YES' THEN ' NULL' ELSE ' NOT NULL' END
                        FROM INFORMATION_SCHEMA.COLUMNS c
                        WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
                        ORDER BY c.ORDINAL_POSITION
                        FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 2, '') + 
                   ')'
            FROM INFORMATION_SCHEMA.TABLES t
            WHERE t.TABLE_SCHEMA = @schema AND t.TABLE_NAME = @table";
    }

    private string GetPostgresTableSchema(string schema, string tableName)
    {
        return $@"
            SELECT 'CREATE TABLE ""{EscapeSqlString(schema)}"" ."" {EscapeSqlString(tableName)} "" (' || 
                   COALESCE(array_to_string(ARRAY_AGG(
                       '""' || a.attname || '"" ' || 
                       pg_catalog.format_type(a.atttypid, a.atttypmod) ||
                       CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END
                       ORDER BY a.attnum
                   ), ', '), '') || ')'
            FROM pg_attribute a
            JOIN pg_class c ON a.attrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = '{EscapeSqlString(schema)}'
            AND c.relname = '{EscapeSqlString(tableName)}'
            AND a.attnum > 0";
    }

    private string GetOracleTableSchema(string schema, string tableName)
    {
        return $@"
            SELECT 'CREATE TABLE {schema}.{tableName} (' ||
                   LISTAGG(COLUMN_NAME || ' ' || DATA_TYPE || 
                           CASE WHEN NULLABLE = 'N' THEN ' NOT NULL' ELSE '' END, ', ')
                   WITHIN GROUP (ORDER BY COLUMN_ID) || ')'
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = '{EscapeSqlString(schema)}'
            AND TABLE_NAME = '{EscapeSqlString(tableName)}'";
    }

    private DbConnection CreateConnection(ConnectionInfo connectionInfo) => connectionInfo.DatabaseType switch
    {
        DatabaseType.SqlServer => new SqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.PostgreSQL => new NpgsqlConnection(connectionInfo.GetConnectionString()),
        DatabaseType.Oracle => new OracleConnection(connectionInfo.GetConnectionString()),
        _ => throw new NotSupportedException()
    };
}
