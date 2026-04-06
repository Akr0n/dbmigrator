using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

public class DatabaseService : DatabaseServiceBase, IDatabaseService
{
    private readonly int _batchSize;
    private readonly int _rowCountMaxConcurrency;

    /// <summary>
    /// Optional handler invoked when TRUNCATE TABLE fails during data migration.
    /// Returns true to continue inserting, false to abort the migration.
    /// </summary>
    public Func<TruncateFailureContext, Task<bool>>? TruncateFailedHandlerAsync { get; set; }

    public DatabaseService()
    {
        var options = RuntimeOptionsProvider.Current.Database;
        _batchSize = options.BatchSize;
        _rowCountMaxConcurrency = options.RowCountMaxConcurrency;
    }

    private static string DescribeConnection(ConnectionInfo connectionInfo)
    {
        return $"{connectionInfo.DatabaseType} {connectionInfo.Server}:{connectionInfo.Port}/{connectionInfo.Database}";
    }

    public async Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo)
    {
        try
        {
            Log($"TestConnectionAsync started for {DescribeConnection(connectionInfo)}");
            
            using (var connection = CreateConnection(connectionInfo))
            {
                Log("Opening connection...");
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "TestConnectionAsync.Open");
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
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "GetTablesAsync.Open");
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
                        WHERE owner NOT IN (
                            'SYS', 'SYSTEM', 'XDB', 'ANONYMOUS',
                            'APEX_030200', 'APEX_040200', 'APEX_050000', 'APEX_050100',
                            'APEX_180200', 'APEX_190100', 'APEX_200100', 'APEX_210100', 'APEX_220100',
                            'AUDSYS', 'CTXSYS', 'DBSNMP', 'DVSYS', 'DVF',
                            'FLOWS_FILES', 'GSMADMIN_INTERNAL', 'GSMCATUSER', 'GSMROOTUSER',
                            'LBACSYS', 'MDDATA', 'MDSYS', 'OJVMSYS', 'OLAPSYS',
                            'ORACLE_OCM', 'ORDDATA', 'ORDPLUGINS', 'ORDSYS', 'OUTLN',
                            'OWBSYS', 'OWBSYS_AUDIT', 'REMOTE_SCHEDULER_AGENT',
                            'SI_INFORMTN_SCHEMA', 'SYS$UMF', 'SYSBACKUP', 'SYSDG',
                            'SYSKM', 'SYSRAC', 'WMSYS', 'XS$NULL'
                        )
                        ORDER BY owner, table_name",
                    
                    _ => throw new NotSupportedException($"Database type {connectionInfo.DatabaseType} not supported")
                };

                Log($"Executing query for tables...");
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = _commandTimeoutSeconds;

                    using (var reader = await ExecuteWithRetryAsync(() => command.ExecuteReaderAsync(), "GetTablesAsync.ExecuteReader"))
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

                // Get row count for all tables in batch to avoid too many connections
                // Use controlled parallelism for databases with many tables
                Log($"Starting row count retrieval for {tables.Count} tables...");
                await GetAllTableRowCountsAsync(connectionInfo, tables);
                Log($"Row count retrieval completed");
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
            // Crea una connessione (per Oracle usa il service name dall'utente: FREEPDB1, XE, ecc.)
            var connInfo = new ConnectionInfo
            {
                DatabaseType = connectionInfo.DatabaseType,
                Server = connectionInfo.Server,
                Port = connectionInfo.Port,
                Username = connectionInfo.Username,
                Password = connectionInfo.Password,
                TrustServerCertificate = connectionInfo.TrustServerCertificate,
                Database = connectionInfo.DatabaseType switch
                {
                    DatabaseType.Oracle => string.IsNullOrWhiteSpace(connectionInfo.Database)
                        ? "FREEPDB1"
                        : connectionInfo.Database,
                    DatabaseType.PostgreSQL => "postgres",
                    _ => "master"
                }
            };

            using (var connection = CreateConnection(connInfo))
            {
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "DatabaseExistsAsync.Open");

                // Use parameterized queries to prevent SQL injection
                // For Oracle: use all_users (accessible to normal users), check Username = schema to migrate to
                string query = connectionInfo.DatabaseType switch
                {
                    DatabaseType.SqlServer => "SELECT 1 FROM sys.databases WHERE name = @dbName",
                    DatabaseType.PostgreSQL => "SELECT 1 FROM pg_database WHERE datname = @dbName",
                    DatabaseType.Oracle => "SELECT 1 FROM all_users WHERE username = :userName",
                    _ => throw new NotSupportedException()
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = _commandTimeoutSeconds;
                    
                    // Add parameter to prevent SQL injection
                    var param = command.CreateParameter();
                    param.ParameterName = connectionInfo.DatabaseType == DatabaseType.Oracle ? ":userName" : "@dbName";
                    // For Oracle the "database" is a schema/user — check the target Database name, not the login Username.
                    param.Value = connectionInfo.DatabaseType == DatabaseType.Oracle
                        ? connectionInfo.Database.ToUpperInvariant()
                        : connectionInfo.Database;
                    command.Parameters.Add(param);
                    
                    var scalarResult = await ExecuteWithRetryAsync(() => command.ExecuteScalarAsync(), "DatabaseExistsAsync.ExecuteScalar");
                    return scalarResult != null;
                }
            }
        }
        catch (Exception ex)
        {
            // Log so the operator can distinguish "DB not found" from a real connection/auth error.
            // We return false so the caller can attempt creation, but the log makes diagnosis possible.
            Log($"[DatabaseExistsAsync] Could not verify existence of '{connectionInfo.Database}' on {connectionInfo.Server}: {ex.GetType().Name}: {ex.Message}");
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

            // Crea connessione al sistema database (per Oracle usa il service name dall'utente)
            var systemConnInfo = new ConnectionInfo
            {
                DatabaseType = connectionInfo.DatabaseType,
                Server = connectionInfo.Server,
                Port = connectionInfo.Port,
                Username = connectionInfo.Username,
                Password = connectionInfo.Password,
                TrustServerCertificate = connectionInfo.TrustServerCertificate,
                Database = connectionInfo.DatabaseType switch
                {
                    DatabaseType.Oracle => string.IsNullOrWhiteSpace(connectionInfo.Database) ? "FREEPDB1" : connectionInfo.Database,
                    DatabaseType.PostgreSQL => "postgres",  // PostgreSQL system database
                    _ => "master"  // SQL Server system database
                }
            };

            using (var connection = CreateConnection(systemConnInfo))
            {
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "CreateDatabaseAsync.Open");

                string createQuery = "";
                string? usedPassword = null;  // Traccia la password usata
                string? safeDbName = null;
                
                if (connectionInfo.DatabaseType == DatabaseType.Oracle)
                {
                    // Escape the database/user name to prevent SQL injection
                    safeDbName = ValidateOracleUserIdentifier(connectionInfo.Database);
                    // Per Oracle, valida e escapa correttamente la password
                    var oraclePassword = PrepareOraclePassword(connectionInfo.Password);
                    usedPassword = oraclePassword;  // Salva la password originale per la connessione
                    // Usa doppi apici per supportare caratteri speciali come @, !, #, etc
                    createQuery = $"CREATE USER {safeDbName} IDENTIFIED BY \"{oraclePassword}\"";
                    Log($"Oracle: Creating user {safeDbName}");
                }
                else
                {
                    createQuery = connectionInfo.DatabaseType switch
                    {
                        DatabaseType.SqlServer => 
                            $"CREATE DATABASE [{EscapeSqlServerIdentifier(connectionInfo.Database)}]",
                        
                        DatabaseType.PostgreSQL => 
                            $"CREATE DATABASE \"{EscapePostgresIdentifier(connectionInfo.Database)}\"",
                        
                        _ => throw new NotSupportedException()
                    };
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = createQuery;
                    command.CommandTimeout = _commandTimeoutSeconds;
                    await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "CreateDatabaseAsync.ExecuteCreate");
                    Log($"Database {connectionInfo.Database} created successfully");
                    
                    // For Oracle, after creating the user, assign necessary privileges
                    if (connectionInfo.DatabaseType == DatabaseType.Oracle)
                    {
                        // safeDbName was already validated and sanitized above using ValidateOracleUserIdentifier
                        // Log for security audit trail
                        Log($"Oracle: Granting privileges to validated user identifier: {safeDbName}");
                        
                        var grantQuery = $"GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE PROCEDURE TO {safeDbName}";
                        command.CommandText = grantQuery;
                        await ExecuteWithRetryAsync(() => command.ExecuteNonQueryAsync(), "CreateDatabaseAsync.ExecuteGrant");
                        Log($"Privileges granted to user {safeDbName}");
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
        
        // Password is used with double quotes in CREATE USER statement,
        // so no escaping of special characters is needed
        Log("Oracle: Using provided password");
        return password;
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
        const string allChars = upperChars + lowerChars + digits + specialChars;
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
        
        // Log that a secure password was generated without exposing it in logs
        Log("Oracle: Generated secure password for new user.");
        
        return generatedPassword;
    }

    private static int GetSecureRandomInt(int maxValue)
    {
        if (maxValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");
        }

        // Use cryptographically secure random number generator without modulo bias
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(maxValue);
    }

    public async Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, string tableName, string schema)
    {
        using (var connection = CreateConnection(connectionInfo))
        {
            await ExecuteWithRetryAsync(() => connection.OpenAsync(), "GetTableSchemaAsync.Open");

            string query = connectionInfo.DatabaseType switch
            {
                DatabaseType.SqlServer => GetSqlServerTableSchema(),
                DatabaseType.PostgreSQL => GetPostgresTableSchema(schema, tableName),
                DatabaseType.Oracle => GetOracleTableSchema(schema, tableName),
                _ => throw new NotSupportedException()
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = query;
                command.CommandTimeout = _commandTimeoutSeconds;

                // SQL Server query references @schema and @table as parameters.
                if (connectionInfo.DatabaseType == DatabaseType.SqlServer)
                {
                    var schemaParam = command.CreateParameter();
                    schemaParam.ParameterName = "@schema";
                    schemaParam.Value = schema;
                    command.Parameters.Add(schemaParam);

                    var tableParam = command.CreateParameter();
                    tableParam.ParameterName = "@table";
                    tableParam.Value = tableName;
                    command.Parameters.Add(tableParam);
                }

                var script = new System.Text.StringBuilder();
                script.AppendLine($"-- Tabella: {schema}.{tableName}");

                using (var reader = await ExecuteWithRetryAsync(() => command.ExecuteReaderAsync(), "GetTableSchemaAsync.ExecuteReader"))
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
            await ExecuteWithRetryAsync(() => sourceConn.OpenAsync(), "MigrateTableAsync.SourceOpen");
            await ExecuteWithRetryAsync(() => targetConn.OpenAsync(), "MigrateTableAsync.TargetOpen");

            // Diagnostica: logga lo schema e l'utente per Oracle
            if (target.DatabaseType == DatabaseType.Oracle && targetConn is OracleConnection oracleConn)
            {
                try
                {
                    using (var diagCmd = targetConn.CreateCommand())
                    {
                        diagCmd.CommandText = "SELECT USER FROM DUAL";
                        var currentUser = await ExecuteWithRetryAsync(() => diagCmd.ExecuteScalarAsync(), "MigrateTableAsync.OracleCurrentUser");
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
                        // Oracle: TRUNCATE is DDL and causes an implicit COMMIT, making rollback impossible.
                        // Use DELETE FROM instead: it is DML and participates in the manual COMMIT/ROLLBACK flow.
                        DatabaseType.Oracle => $"DELETE FROM {FormatTableName(target.DatabaseType, table.Schema, table.TableName)}",
                        _ => throw new NotSupportedException()
                    };
                    
                    using (var truncateCommand = targetConn.CreateCommand())
                    {
                        truncateCommand.CommandText = truncateQuery;
                        if (transaction != null)
                            truncateCommand.Transaction = transaction;
                        truncateCommand.CommandTimeout = _commandTimeoutSeconds;
                        await ExecuteWithRetryAsync(() => truncateCommand.ExecuteNonQueryAsync(), "MigrateTableAsync.Truncate");
                    }
                    Log($"[MigrateTableAsync] Table truncated successfully");
                }
                catch (Exception ex)
                {
                    Log($"[MigrateTableAsync] Warning: Could not truncate table: {ex.Message}");

                    var handler = TruncateFailedHandlerAsync;
                    if (handler != null)
                    {
                        var ctx = new TruncateFailureContext(table.Schema, table.TableName, ex.Message);
                        bool shouldContinue = await handler(ctx);

                        if (!shouldContinue)
                        {
                            throw new InvalidOperationException(
                                $"TRUNCATE fallito per {table.Schema}.{table.TableName} e migrazione annullata dall'utente.",
                                ex);
                        }
                    }

                    // Default behavior (no handler): keep previous semantics and continue.
                }

                // Leggi i dati dalla sorgente in batch
                long totalRows = await GetTableRowCountAsync(source, table.Schema, table.TableName);
                long migratedRows = 0;

                string sourceQuery = $"SELECT * FROM {FormatTableName(source.DatabaseType, table.Schema, table.TableName)}";

                using (var sourceCommand = sourceConn.CreateCommand())
                {
                    sourceCommand.CommandText = sourceQuery;
                    sourceCommand.CommandTimeout = _commandTimeoutSeconds;

                    using (var reader = await ExecuteWithRetryAsync(() => sourceCommand.ExecuteReaderAsync(), "MigrateTableAsync.SourceReader"))
                    {
                        var columnNames = Enumerable.Range(0, reader.FieldCount)
                            .Select(reader.GetName)
                            .ToList();

                        if (columnNames.Count == 0)
                        {
                            progress?.Report(100);
                            await FinalizeTransactionAsync(targetConn, transaction, target.DatabaseType, true);
                            return;
                        }

                        // For SQL Server: check if table has IDENTITY column and enable IDENTITY_INSERT
                        bool hasIdentity = false;
                        string formattedTableName = FormatTableName(target.DatabaseType, table.Schema, table.TableName);
                        
                        if (target.DatabaseType == DatabaseType.SqlServer)
                        {
                            hasIdentity = await HasIdentityColumnAsync(targetConn, table.Schema, table.TableName, transaction);
                            if (hasIdentity)
                            {
                                Log($"[MigrateTableAsync] Enabling IDENTITY_INSERT for {formattedTableName}");
                                using (var identityCmd = targetConn.CreateCommand())
                                {
                                    identityCmd.CommandText = $"SET IDENTITY_INSERT {formattedTableName} ON";
                                    identityCmd.CommandTimeout = _commandTimeoutSeconds;
                                    if (transaction != null)
                                        identityCmd.Transaction = transaction;
                                    await ExecuteWithRetryAsync(() => identityCmd.ExecuteNonQueryAsync(), "MigrateTableAsync.IdentityInsertOn");
                                }
                            }
                        }

                        try
                        {
                            // Inserisci i dati nel target in batch
                            using (var targetCommand = targetConn.CreateCommand())
                            {
                                targetCommand.CommandTimeout = _commandTimeoutSeconds;
                                if (transaction != null)
                                    targetCommand.Transaction = transaction;

                                async Task InsertBatchAsync(List<object?[]> rowsBatch)
                                {
                                    string insertQuery = BuildInsertQuery(
                                        target.DatabaseType,
                                        table.Schema,
                                        table.TableName,
                                        columnNames,
                                        rowsBatch);

                                    // For Oracle: execute each INSERT individually
                                    if (target.DatabaseType == DatabaseType.Oracle)
                                    {
                                        // Split queries using robust parser that respects string literals
                                        var queries = SplitSqlStatements(insertQuery);
                                        Log($"[MigrateTableAsync] Oracle batch: {rowsBatch.Count} rows, {queries.Count} INSERT statements");

                                        int rowsInserted = 0;
                                        foreach (var cleanQuery in queries.Select(q => q.Trim()).Where(q => !string.IsNullOrEmpty(q)))
                                        {
                                            targetCommand.CommandText = cleanQuery;
                                            var rowsAffected = await targetCommand.ExecuteNonQueryAsync();
                                            rowsInserted += rowsAffected;
                                        }

                                        Log($"[MigrateTableAsync] Oracle batch completed ({rowsInserted} rows inserted)");
                                    }
                                    else
                                    {
                                        // For SQL Server and PostgreSQL: execute the batch
                                        targetCommand.CommandText = insertQuery;
                                        var rowsAffected = await targetCommand.ExecuteNonQueryAsync();
                                        Log($"[MigrateTableAsync] Batch INSERT executed, rows affected: {rowsAffected}");
                                    }
                                }

                                var batchRows = new List<object?[]>(_batchSize);
                                while (await reader.ReadAsync())
                                {
                                    var rowValues = new object[reader.FieldCount];
                                    reader.GetValues(rowValues);
                                    batchRows.Add(rowValues);

                                    if (batchRows.Count >= _batchSize)
                                    {
                                        await InsertBatchAsync(batchRows);
                                        migratedRows += batchRows.Count;
                                        int percentage = totalRows > 0
                                            ? (int)((migratedRows / (double)totalRows) * 100)
                                            : 100;
                                        progress?.Report(Math.Min(percentage, 100));
                                        batchRows.Clear();
                                    }
                                }

                                if (batchRows.Count > 0)
                                {
                                    await InsertBatchAsync(batchRows);
                                    migratedRows += batchRows.Count;
                                    int percentage = totalRows > 0
                                        ? (int)((migratedRows / (double)totalRows) * 100)
                                        : 100;
                                    progress?.Report(Math.Min(percentage, 100));
                                }

                                if (migratedRows == 0)
                                {
                                    progress?.Report(100);
                                }
                            }
                        }
                        finally
                        {
                            // For SQL Server: disable IDENTITY_INSERT after all inserts
                            // This must always execute to prevent leaving IDENTITY_INSERT enabled
                            if (target.DatabaseType == DatabaseType.SqlServer && hasIdentity)
                            {
                                try
                                {
                                    Log($"[MigrateTableAsync] Disabling IDENTITY_INSERT for {formattedTableName}");
                                    using (var identityCmd = targetConn.CreateCommand())
                                    {
                                        identityCmd.CommandText = $"SET IDENTITY_INSERT {formattedTableName} OFF";
                                        identityCmd.CommandTimeout = _commandTimeoutSeconds;
                                        if (transaction != null)
                                            identityCmd.Transaction = transaction;
                                        await ExecuteWithRetryAsync(() => identityCmd.ExecuteNonQueryAsync(), "MigrateTableAsync.IdentityInsertOff");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"[MigrateTableAsync] Warning: Failed to disable IDENTITY_INSERT for {formattedTableName}: {ex.Message}");
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
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    await ExecuteWithRetryAsync(() => cmd.ExecuteNonQueryAsync(), "FinalizeTransactionAsync.CommitRollback");
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

    /// <summary>
    /// Checks if a SQL Server table has an IDENTITY column.
    /// </summary>
    private async Task<bool> HasIdentityColumnAsync(DbConnection connection, string schema, string tableName, DbTransaction? transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT COUNT(*)
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @Schema
                  AND t.name = @TableName
                  AND c.is_identity = 1";
            command.CommandTimeout = _commandTimeoutSeconds;
            if (transaction != null)
                command.Transaction = transaction;

            var schemaParam = command.CreateParameter();
            schemaParam.ParameterName = "@Schema";
            schemaParam.Value = schema;
            command.Parameters.Add(schemaParam);

            var tableParam = command.CreateParameter();
            tableParam.ParameterName = "@TableName";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);

            var result = await ExecuteWithRetryAsync(() => command.ExecuteScalarAsync(), "HasIdentityColumnAsync.ExecuteScalar");
            var count = Convert.ToInt32(result ?? 0);
            Log($"[HasIdentityColumnAsync] Table {schema}.{tableName} has {count} identity column(s)");
            return count > 0;
        }
    }

    /// <summary>
    /// Gets row counts for all tables reusing a single open connection to avoid per-table
    /// connection overhead (previously opened N connections serially behind a semaphore).
    /// </summary>
    private async Task GetAllTableRowCountsAsync(ConnectionInfo connectionInfo, List<TableInfo> tables)
    {
        if (tables.Count == 0)
            return;

        using var connection = CreateConnection(connectionInfo);
        await ExecuteWithRetryAsync(() => connection.OpenAsync(), "GetAllTableRowCountsAsync.Open");

        foreach (var table in tables)
        {
            try
            {
                string query = $"SELECT COUNT(*) FROM {FormatTableName(connectionInfo.DatabaseType, table.Schema, table.TableName)}";
                using var command = connection.CreateCommand();
                command.CommandText = query;
                command.CommandTimeout = _commandTimeoutSeconds;
                var result = await ExecuteWithRetryAsync(() => command.ExecuteScalarAsync(), "GetAllTableRowCountsAsync.Count");
                table.RowCount = Convert.ToInt64(result ?? 0);
            }
            catch (Exception ex)
            {
                Log($"Error getting row count for {table.Schema}.{table.TableName}: {ex.Message}");
                table.RowCount = 0;
            }
        }
    }

    private async Task<long> GetTableRowCountAsync(ConnectionInfo connectionInfo, string schema, string tableName)
    {
        try
        {
            using (var connection = CreateConnection(connectionInfo))
            {
                await ExecuteWithRetryAsync(() => connection.OpenAsync(), "GetTableRowCountAsync.Open");

                string query = $"SELECT COUNT(*) FROM {FormatTableName(connectionInfo.DatabaseType, schema, tableName)}";

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = _commandTimeoutSeconds;
                    var result = await ExecuteWithRetryAsync(() => command.ExecuteScalarAsync(), "GetTableRowCountAsync.ExecuteScalar");
                    return Convert.ToInt64(result ?? 0);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"GetTableRowCountAsync error for {schema}.{tableName}: {ex.Message}");
            return 0;
        }
    }

    private string BuildInsertQuery(
        DatabaseType dbType,
        string schema,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows)
    {
        var sb = new System.Text.StringBuilder();
        string tableRef = FormatTableName(dbType, schema, tableName);

        // Costruisci colonne con case appropriato per ogni database
        var columnNames = new List<string>();
        for (int i = 0; i < columns.Count; i++)
        {
            string colName = columns[i];
            columnNames.Add(dbType switch
            {
                DatabaseType.SqlServer => $"[{EscapeSqlServerIdentifier(colName)}]",
                DatabaseType.PostgreSQL => $"\"{EscapePostgresIdentifier(colName.ToLowerInvariant())}\"",
                DatabaseType.Oracle => colName.ToUpperInvariant(),
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
            bool b => dbType switch
            {
                DatabaseType.PostgreSQL => b ? "TRUE" : "FALSE",
                _ => b ? "1" : "0"
            },
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
                // Use TO_TIMESTAMP to preserve sub-second precision; TO_DATE truncates to seconds.
                DatabaseType.Oracle => $"TO_TIMESTAMP('{dt:yyyy-MM-dd HH:mm:ss.fff}','YYYY-MM-DD HH24:MI:SS.FF3')",
                _ => $"'{dt:yyyy-MM-dd HH:mm:ss}'"
            },
            DateTimeOffset dto => dbType switch
            {
                DatabaseType.SqlServer => $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'",
                DatabaseType.PostgreSQL => $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'",
                DatabaseType.Oracle => $"TO_TIMESTAMP_TZ('{dto:yyyy-MM-dd HH:mm:ss.fff zzz}','YYYY-MM-DD HH24:MI:SS.FF3 TZH:TZM')",
                _ => $"'{dto:yyyy-MM-dd HH:mm:ss zzz}'"
            },
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Guid g => dbType == DatabaseType.Oracle
                ? $"hextoraw('{g.ToString("N").ToUpperInvariant()}')"
                : $"'{g:D}'",
            TimeSpan ts => dbType == DatabaseType.Oracle
                ? $"TO_TIMESTAMP('1970-01-01 {(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}', 'YYYY-MM-DD HH24:MI:SS.FF3')"
                : $"'{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}'",
            _ => value.ToString() ?? "NULL"
        };
    }

    private string EscapeSqlString(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Validates and sanitizes an Oracle identifier intended for unquoted use in user/schema DDL
    /// (CREATE USER, GRANT). Throws if the identifier contains characters not allowed by Oracle.
    /// </summary>
    private string ValidateOracleUserIdentifier(string identifier)
    {
        return ValidateAndSanitizeOracleIdentifier(identifier, "database/user");
    }

    /// <summary>
    /// Splits SQL statements by semicolon while respecting string literals and comments.
    /// This handles cases where semicolons appear inside quoted strings or comments.
    /// </summary>
    private static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sql))
            return statements;

        var current = new System.Text.StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inSingleLineComment = false;
        bool inMultiLineComment = false;

        for (int idx = 0; idx < sql.Length; idx++)
        {
            char ch = sql[idx];

            // Handle single-line comments (-- comment)
            if (!inSingleQuote && !inDoubleQuote && !inMultiLineComment && 
                ch == '-' && idx + 1 < sql.Length && sql[idx + 1] == '-')
            {
                inSingleLineComment = true;
                current.Append(ch);
                continue;
            }

            // End single-line comment on newline
            if (inSingleLineComment && (ch == '\n' || ch == '\r'))
            {
                inSingleLineComment = false;
                current.Append(ch);
                continue;
            }

            // Handle multi-line comments (/* comment */)
            if (!inSingleQuote && !inDoubleQuote && !inSingleLineComment &&
                ch == '/' && idx + 1 < sql.Length && sql[idx + 1] == '*')
            {
                inMultiLineComment = true;
                current.Append(ch);
                current.Append(sql[idx + 1]);
                idx++;
                continue;
            }

            // End multi-line comment
            if (inMultiLineComment && ch == '*' && idx + 1 < sql.Length && sql[idx + 1] == '/')
            {
                inMultiLineComment = false;
                current.Append(ch);
                current.Append(sql[idx + 1]);
                idx++;
                continue;
            }

            // Skip processing quotes and semicolons if inside comments
            if (inSingleLineComment || inMultiLineComment)
            {
                current.Append(ch);
                continue;
            }

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
                if (inDoubleQuote && idx + 1 < sql.Length && sql[idx + 1] == '"')
                {
                    // Escaped double quote inside identifier
                    current.Append(ch);
                    current.Append(sql[idx + 1]);
                    idx++;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
                current.Append(ch);
                continue;
            }

            // Treat semicolon as statement terminator only when not inside any quotes or comments
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
    /// Validates and sanitizes an Oracle identifier to prevent SQL injection.
    /// Since Oracle GRANT statements do not support parameterized queries, this method
    /// enforces strict validation rules to ensure the identifier is safe for use in
    /// dynamically constructed SQL statements.
    /// 
    /// Oracle identifier rules:
    /// - Must start with a letter (A-Z, a-z)
    /// - Can contain only letters, digits, underscore (_), dollar sign ($), and hash (#)
    /// - Maximum length is 30 characters (Oracle 12.1 and earlier) or 128 characters (Oracle 12.2+)
    /// - Reserved words are not validated here as they would cause Oracle errors
    /// </summary>
    /// <param name="identifier">The identifier to validate and sanitize</param>
    /// <param name="identifierType">Description of what the identifier represents (for error messages)</param>
    /// <returns>The validated and sanitized identifier in uppercase</returns>
    /// <exception cref="ArgumentException">Thrown if the identifier fails validation</exception>
    private string ValidateAndSanitizeOracleIdentifier(string identifier, string identifierType)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException($"Oracle {identifierType} identifier cannot be null or empty", nameof(identifier));
        
        // Normalize to uppercase first so any subsequent use of the identifier value is consistent
        var upperIdentifier = identifier.ToUpperInvariant();
        
        // Oracle identifiers: only allow alphanumeric, underscore, dollar sign, and hash
        // Detect, rather than silently drop, invalid characters
        var sanitized = new System.Text.StringBuilder();
        var invalidCharsSet = new System.Collections.Generic.HashSet<char>();
        
        foreach (char c in upperIdentifier)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#')
            {
                sanitized.Append(c);
            }
            else
            {
                invalidCharsSet.Add(c);
            }
        }
        
        var result = sanitized.ToString();
        
        if (invalidCharsSet.Count > 0)
        {
            var invalidChars = new string(invalidCharsSet.ToArray());
            throw new ArgumentException(
                $"Oracle {identifierType} identifier '{identifier}' contains invalid characters: '{invalidChars}'. " +
                "Only alphanumeric characters, underscore (_), dollar sign ($), and hash (#) are allowed.", 
                nameof(identifier));
        }
        
        // Oracle identifiers cannot start with a digit
        if (result.Length > 0 && char.IsDigit(result[0]))
            result = "_" + result;
        
        return result;
    }
    // Schema and table are passed as @schema / @table SQL parameters from GetTableSchemaAsync.
    private string GetSqlServerTableSchema()
    {
        return @"
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
        // Fix: use EscapeSqlString consistently for all embedded literals, and correct the
        // spacing/quoting around the dot separator (was: '"" ."" tableName ""').
        return $@"
            SELECT 'CREATE TABLE ""{EscapeSqlString(schema)}"".""{EscapeSqlString(tableName)}"" (' ||
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
        // Fix: use EscapeSqlString for the schema/tableName inside the DDL string literal too,
        // not just in the WHERE clause (previously schema and tableName were interpolated raw).
        return $@"
            SELECT 'CREATE TABLE {EscapeSqlString(schema)}.{EscapeSqlString(tableName)} (' ||
                   LISTAGG(COLUMN_NAME || ' ' || DATA_TYPE ||
                           CASE WHEN NULLABLE = 'N' THEN ' NOT NULL' ELSE '' END, ', ')
                   WITHIN GROUP (ORDER BY COLUMN_ID) || ')'
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = '{EscapeSqlString(schema)}'
            AND TABLE_NAME = '{EscapeSqlString(tableName)}'";
    }

}
