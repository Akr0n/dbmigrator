using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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

    public async Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await connection.OpenAsync();
                return connection.State == ConnectionState.Open;
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo)
    {
        var tables = new List<TableInfo>();
        
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await connection.OpenAsync();

                string query = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => @"
                        SELECT TABLE_SCHEMA, TABLE_NAME, 
                               (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = t.TABLE_SCHEMA AND TABLE_NAME = t.TABLE_NAME) as RowCount
                        FROM INFORMATION_SCHEMA.TABLES t
                        WHERE TABLE_TYPE = 'BASE TABLE'
                        ORDER BY TABLE_SCHEMA, TABLE_NAME",
                    
                    DatabaseType.PostgreSQL => @"
                        SELECT schemaname, tablename, 0 as RowCount
                        FROM pg_tables
                        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                        ORDER BY schemaname, tablename",
                    
                    DatabaseType.Oracle => @"
                        SELECT owner, table_name, 0 as RowCount
                        FROM all_tables
                        WHERE owner NOT IN ('SYS', 'SYSTEM', 'XDB', 'APEX_030200')
                        ORDER BY owner, table_name",
                    
                    _ => throw new NotSupportedException($"Database type {connectionInfo.DatabaseType} not supported")
                };

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

                // Ottieni row count per ogni tabella
                foreach (var table in tables)
                {
                    table.RowCount = await GetTableRowCountAsync(connectionInfo, table.Schema, table.TableName);
                }
            }
        }
        catch (Exception ex)
        {
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
        // Crea connessione al sistema database (senza database specifico)
        var systemConnInfo = new ConnectionInfo
        {
            DatabaseType = connectionInfo.DatabaseType,
            Server = connectionInfo.Server,
            Port = connectionInfo.Port,
            Username = connectionInfo.Username,
            Password = connectionInfo.Password,
            Database = connectionInfo.DatabaseType == DatabaseType.Oracle ? "XE" : "master"
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
            }
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
        return sb.ToString();
    }

    private string FormatSqlValue(DatabaseType dbType, object? value)
    {
        if (value == null || value is DBNull)
            return "NULL";

        return value switch
        {
            string s => $"'{EscapeSqlString(s)}'",
            bool b => dbType == DatabaseType.Oracle ? (b ? "1" : "0") : (b ? "1" : "0"),
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
            SELECT 'CREATE TABLE ""{0}"".""{1}"" (' || 
                   array_to_string(ARRAY_AGG(
                       '"" ' || a.attname || ' ' || 
                       pg_catalog.format_type(a.atttypid, a.atttypmod) ||
                       CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END
                   ), ', ') || ')'
            FROM pg_attribute a
            JOIN pg_class c ON a.attrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = '{EscapeSqlString(schema)}'
            AND c.relname = '{EscapeSqlString(tableName)}'
            AND a.attnum > 0".Replace("{0}", schema).Replace("{1}", tableName);
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
