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
    private const int CommandTimeout = 300; // 5 minutes

    private static void Log(string message) => LoggerService.Log(message);

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

                // Use parameterized queries to prevent SQL injection
                string query = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => "SELECT 1 FROM sys.databases WHERE name = @dbName",
                    DatabaseType.PostgreSQL => "SELECT 1 FROM pg_database WHERE datname = @dbName",
                    DatabaseType.Oracle => "SELECT 1 FROM all_users WHERE username = :dbName",
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = CommandTimeout;
                    
                    // Add parameter to prevent SQL injection
                    var param = command.CreateParameter();
                    param.ParameterName = connectionInfo.DatabaseType == DatabaseType.Oracle ? ":dbName" : "@dbName";
                    param.Value = connectionInfo.DatabaseType == DatabaseType.Oracle 
                        ? connectionInfo.Database.ToUpperInvariant() 
                        : connectionInfo.Database;
                    command.Parameters.Add(param);
                    
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

    public async Task<string?> CreateDatabaseAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            // Per Oracle, il "Database" è sempre uno schema/utente, non il SID
            // Il SID è specificato nella connessione di sistema
            // Se l'utente non specifica uno schema custom, non creiamo nulla
            if (connectionInfo.DatabaseType == DatabaseType.Oracle && 
                string.IsNullOrWhiteSpace(connectionInfo.Database))
            {
                Log($"Oracle: Database/Schema not specified, assuming existing schema");
                return null;
            }

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
                    DatabaseType.Oracle => "XE",  // SID di sistema per Oracle
                    DatabaseType.PostgreSQL => "postgres",  // PostgreSQL system database
                    _ => "master"  // SQL Server system database
                }
            };

            using (var connection = CreateConnection(systemConnInfo))
            {
                await connection.OpenAsync();

                string createQuery = "";
                string? usedPassword = null;  // Traccia la password usata
                
                if (connectionInfo.DatabaseType == DatabaseType.Oracle)
                {
                    // Per Oracle, valida e escapa correttamente la password
                    var oraclePassword = PrepareOraclePassword(connectionInfo.Password);
                    usedPassword = oraclePassword;  // Salva la password originale per la connessione
                    // Usa doppi apici per supportare caratteri speciali come @, !, #, etc
                    createQuery = $"CREATE USER {connectionInfo.Database} IDENTIFIED BY \"{oraclePassword}\"";
                    Log($"Oracle: Creating user {connectionInfo.Database} with escaped password");
                }
                else
                {
                    createQuery = connectionInfo.DatabaseType switch
                    {
                        DatabaseType.SqlServer => 
                            $"CREATE DATABASE [{connectionInfo.Database}]",
                        
                        DatabaseType.PostgreSQL => 
                            $"CREATE DATABASE \"{connectionInfo.Database}\"",
                        
                        _ => throw new NotSupportedException()
                    };
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = createQuery;
                    command.CommandTimeout = CommandTimeout;
                    await command.ExecuteNonQueryAsync();
                    Log($"Database {connectionInfo.Database} created successfully");
                    
                    // For Oracle, after creating the user, assign necessary privileges
                    if (connectionInfo.DatabaseType == DatabaseType.Oracle)
                    {
                        // Escape the database/user name to prevent SQL injection
                        var safeDbName = EscapeOracleIdentifier(connectionInfo.Database);
                        var grantQuery = $"GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE PROCEDURE TO {safeDbName}";
                        command.CommandText = grantQuery;
                        await command.ExecuteNonQueryAsync();
                        Log($"Privileges granted to user {connectionInfo.Database}");
                    }
                }
                
                // Retorna la password usata (per Oracle) o null (per altri database)
                return usedPassword;
            }
        }
        catch (Exception ex)
        {
            // Se il database esiste già, ignora l'errore
            string errorMessage = ex.Message.ToLower();
            if (errorMessage.Contains("already exists") || 
                errorMessage.Contains("esiste già") ||
                errorMessage.Contains("42P06") ||  // PostgreSQL error code for "database already exists"
                errorMessage.Contains("ORA-01501") || // Oracle user already exists
                errorMessage.Contains("ora-01920") ||  // Oracle name already used by another object
                errorMessage.Contains("already exists as another object type"))  // Oracle generic exists
            {
                Log($"Database {connectionInfo.Database} already exists, skipping creation");
                // Se è Oracle e lo user esiste già, ritorna la password comunque
                if (connectionInfo.DatabaseType == DatabaseType.Oracle)
                {
                    return PrepareOraclePassword(connectionInfo.Password);
                }
                return null;
            }
            // Se è un errore diverso, rethrow
            throw;
        }
    }

    private string PrepareOraclePassword(string password)
    {
        // Oracle supports all characters in password
        // Must do proper escaping for special characters in SQL syntax
        
        if (string.IsNullOrWhiteSpace(password))
        {
            // If password is empty, generate a secure random password
            Log("Oracle: Password empty, generating secure random password");
            return GenerateSecurePassword();
        }
        
        if (password.Length < 4)
        {
            // Oracle requires at least 4 characters (default, may vary)
            Log("Oracle: Password too short (needs at least 4 chars), generating secure random password");
            return GenerateSecurePassword();
        }
        
        // Escape single quotes by doubling them for SQL syntax
        // When using double quotes in CREATE USER, single quotes must be escaped
        var escaped = password.Replace("'", "''");
        
        Log("Oracle: Using provided password with proper SQL escaping");
        return escaped;
    }

    private static string GenerateSecurePassword()
    {
        // Generate a cryptographically secure random password using RandomNumberGenerator
        const string upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowerChars = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specialChars = "!@#$%";
        
        var password = new System.Text.StringBuilder();
        
        // Ensure at least one of each required character type
        password.Append(upperChars[GetSecureRandomInt(upperChars.Length)]);
        password.Append(lowerChars[GetSecureRandomInt(lowerChars.Length)]);
        password.Append(digits[GetSecureRandomInt(digits.Length)]);
        password.Append(specialChars[GetSecureRandomInt(specialChars.Length)]);
        
        // Fill remaining characters
        const string allChars = upperChars + lowerChars + digits;
        for (int i = 4; i < 16; i++)
        {
            password.Append(allChars[GetSecureRandomInt(allChars.Length)]);
        }
        
        // Shuffle the password using Fisher-Yates with secure random
        var shuffled = password.ToString().ToCharArray();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = GetSecureRandomInt(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        
        var generatedPassword = new string(shuffled);
        
        // Log the generated password so administrators can retrieve it if needed
        Log($"Oracle: Generated secure password for new user (save this): {generatedPassword}");
        
        return generatedPassword;
    }

    private static int GetSecureRandomInt(int maxValue)
    {
        // Use cryptographically secure random number generator
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var randomBytes = new byte[4];
        rng.GetBytes(randomBytes);
        var randomInt = Math.Abs(BitConverter.ToInt32(randomBytes, 0));
        return randomInt % maxValue;
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

            // Diagnostica: logga lo schema e l'utente per Oracle
            if (target.DatabaseType == DatabaseType.Oracle && targetConn is OracleConnection oracleConn)
            {
                try
                {
                    using (var diagCmd = targetConn.CreateCommand())
                    {
                        diagCmd.CommandText = "SELECT USER FROM DUAL";
                        var currentUser = await diagCmd.ExecuteScalarAsync();
                        Log($"[MigrateTableAsync] Oracle current user: {currentUser}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[MigrateTableAsync] Could not get current user: {ex.Message}");
                }
            }

            DbTransaction? transaction = null;
            if (target.DatabaseType != DatabaseType.Oracle)
            {
                // For SQL Server and PostgreSQL: use driver transaction
                transaction = targetConn.BeginTransaction();
                Log($"[MigrateTableAsync] {target.DatabaseType} transaction started");
            }

            try
            {
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
                        if (transaction != null)
                            truncateCommand.Transaction = transaction;
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
                            await FinalizeTransactionAsync(targetConn, transaction, target.DatabaseType, true);
                            return;
                        }

                        int columnCount = batch.Columns.Count;
                        int processedBatches = 0;
                        int totalBatches = (int)Math.Ceiling((double)batch.Rows.Count / BatchSize);

                        // Inserisci i dati nel target in batch
                        using (var targetCommand = targetConn.CreateCommand())
                        {
                            targetCommand.CommandTimeout = CommandTimeout;
                            if (transaction != null)
                                targetCommand.Transaction = transaction;

                            for (int i = 0; i < batch.Rows.Count; i += BatchSize)
                            {
                                var batchRows = batch.Rows.Cast<DataRow>()
                                    .Skip(i)
                                    .Take(BatchSize)
                                    .ToList();

                                string insertQuery = BuildInsertQuery(target.DatabaseType, table.Schema, table.TableName, batch.Columns, batchRows);
                                
                                try
                                {
                                    // For Oracle: execute each INSERT individually
                                    if (target.DatabaseType == DatabaseType.Oracle)
                                    {
                                        // Split queries using robust parser that respects string literals
                                        var queries = SplitSqlStatements(insertQuery);
                                        Log($"[MigrateTableAsync] Oracle batch: {batchRows.Count} rows, {queries.Count} INSERT statements");
                                        
                                        int rowsInserted = 0;
                                        foreach (var query in queries)
                                        {
                                            var cleanQuery = query.Trim();
                                            if (!string.IsNullOrEmpty(cleanQuery))
                                            {
                                                targetCommand.CommandText = cleanQuery;
                                                var rowsAffected = await targetCommand.ExecuteNonQueryAsync();
                                                rowsInserted += rowsAffected;
                                            }
                                        }
                                        
                                        Log($"[MigrateTableAsync] Oracle batch completed ({rowsInserted} rows inserted)");
                                        // Note: COMMIT will be executed once at the end of migration
                                        // This improves performance for large migrations
                                    }
                                    else
                                    {
                                        // For SQL Server and PostgreSQL: execute the batch
                                        targetCommand.CommandText = insertQuery;
                                        var rowsAffected = await targetCommand.ExecuteNonQueryAsync();
                                        Log($"[MigrateTableAsync] Batch INSERT executed, rows affected: {rowsAffected}");
                                    }
                                    
                                    migratedRows += batchRows.Count;
                                    processedBatches++;

                                    int percentage = (int)((processedBatches / (double)totalBatches) * 100);
                                    progress?.Report(Math.Min(percentage, 100));
                                }
                                catch (Exception ex)
                                {
                                    throw new InvalidOperationException(
                                        $"Error during batch insert for {table.Schema}.{table.TableName}: {ex.Message}", ex);
                                }
                            }
                        }
                    }
                }

                progress?.Report(100);
                await FinalizeTransactionAsync(targetConn, transaction, target.DatabaseType, true);
                Log($"[MigrateTableAsync] Transaction finalized successfully");
            }
            catch (Exception ex)
            {
                Log($"[MigrateTableAsync] Transaction rolled back due to error: {ex.Message}");
                await FinalizeTransactionAsync(targetConn, transaction, target.DatabaseType, false);
                throw;
            }
        }
    }

    private async Task FinalizeTransactionAsync(DbConnection connection, DbTransaction? transaction, DatabaseType dbType, bool commit)
    {
        try
        {
            if (dbType == DatabaseType.Oracle)
            {
                // For Oracle: execute COMMIT or ROLLBACK via SQL
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = commit ? "COMMIT" : "ROLLBACK";
                    cmd.CommandTimeout = CommandTimeout;
                    await cmd.ExecuteNonQueryAsync();
                    Log($"[FinalizeTransactionAsync] Oracle {(commit ? "COMMIT" : "ROLLBACK")} executed");
                }
            }
            else if (transaction != null)
            {
                // For SQL Server and PostgreSQL: use driver transaction
                if (commit)
                {
                    await transaction.CommitAsync();
                    Log($"[FinalizeTransactionAsync] {dbType} transaction committed");
                }
                else
                {
                    await transaction.RollbackAsync();
                    Log($"[FinalizeTransactionAsync] {dbType} transaction rolled back");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[FinalizeTransactionAsync] Error finalizing transaction: {ex.Message}");
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
            DatabaseType.Oracle => tableName,  // Per Oracle, usa solo il nome tabella (senza schema)
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

        // Per Oracle: inserisci una riga per volta (non supporta INSERT batch)
        // Per altri DB: batch INSERT con multiple rows
        if (dbType == DatabaseType.Oracle)
        {
            var queries = new List<string>();
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var values = new List<string>();
                for (int colIdx = 0; colIdx < columns.Count; colIdx++)
                {
                    var value = rows[rowIdx][colIdx];
                    values.Add(FormatSqlValue(dbType, value));
                }
                queries.Add($"INSERT INTO {tableRef} ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", values)})");
            }
            var result = string.Join("; ", queries) + ";";
            Log($"[BuildInsertQuery] Generated Oracle INSERT queries (full length: {result.Length} chars)");
            Log($"[BuildInsertQuery] Oracle query: {result}");
            return result;
        }
        else
        {
            // Batch INSERT per SQL Server e PostgreSQL
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
            Log($"[BuildInsertQuery] Generated INSERT query (full length: {result.Length} chars)");
            return result;
        }
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
                DatabaseType.Oracle => $"hextoraw('{BitConverter.ToString(bytes).Replace("-", "").ToUpper()}')",
                _ => $"'{BitConverter.ToString(bytes)}'"
            },
            DateTime dt => dbType switch
            {
                DatabaseType.SqlServer => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
                DatabaseType.PostgreSQL => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
                DatabaseType.Oracle => $"TO_DATE('{dt:yyyy-MM-dd HH:mm:ss}','YYYY-MM-DD HH24:MI:SS')",
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

    /// <summary>
    /// Splits SQL statements by semicolon while respecting string literals.
    /// This handles cases where semicolons appear inside quoted strings.
    /// </summary>
    private static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sql))
            return statements;

        var current = new System.Text.StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int idx = 0; idx < sql.Length; idx++)
        {
            char ch = sql[idx];

            // Handle single quotes and escaped single quotes (e.g., '')
            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && idx + 1 < sql.Length && sql[idx + 1] == '\'')
                {
                    // Escaped single quote inside string literal
                    current.Append(ch);
                    current.Append(sql[idx + 1]);
                    idx++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                current.Append(ch);
                continue;
            }

            // Handle double quotes (e.g., quoted identifiers)
            if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(ch);
                continue;
            }

            // Treat semicolon as statement terminator only when not inside any quotes
            if (ch == ';' && !inSingleQuote && !inDoubleQuote)
            {
                var statement = current.ToString().Trim();
                if (statement.Length > 0)
                    statements.Add(statement);
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        var lastStatement = current.ToString().Trim();
        if (lastStatement.Length > 0)
            statements.Add(lastStatement);

        return statements;
    }

    /// <summary>
    /// Escapes an Oracle identifier to prevent SQL injection.
    /// Oracle identifiers can only contain alphanumeric characters, underscores, and dollar signs.
    /// </summary>
    private string EscapeOracleIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        
        // Oracle identifiers: only allow alphanumeric, underscore, dollar sign
        // Remove any invalid characters and validate using LINQ
        var sanitized = new System.Text.StringBuilder();
        foreach (char c in identifier.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '$'))
        {
            sanitized.Append(c);
        }
        
        var result = sanitized.ToString();
        if (string.IsNullOrEmpty(result))
            throw new ArgumentException("Identifier contains no valid characters", nameof(identifier));
        
        // Oracle identifiers cannot start with a digit
        if (char.IsDigit(result[0]))
            result = "_" + result;
        
        return result.ToUpperInvariant();
    }

    /// <summary>
    /// Escapes a SQL Server identifier to prevent SQL injection.
    /// </summary>
    private string EscapeSqlServerIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        
        // Replace ] with ]] for SQL Server bracketed identifiers
        return identifier.Replace("]", "]]");
    }

    /// <summary>
    /// Escapes a PostgreSQL identifier to prevent SQL injection.
    /// </summary>
    private string EscapePostgresIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        
        // Replace " with "" for PostgreSQL quoted identifiers
        return identifier.Replace("\"", "\"\"");
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
