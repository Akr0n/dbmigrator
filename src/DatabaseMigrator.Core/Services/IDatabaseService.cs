using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

public interface IDatabaseService
{
    Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo);
    Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo);
    Task<bool> DatabaseExistsAsync(ConnectionInfo connectionInfo);
    Task<string?> CreateDatabaseAsync(ConnectionInfo connectionInfo);
    Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, string tableName, string schema);
    /// <param name="tablesLoadedLater">
    /// The tables this migration will still load after <paramref name="table"/>. The target tables that reference
    /// <paramref name="table"/> are emptied together with it on PostgreSQL; only these (or already-empty ones) may be.
    /// </param>
    Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, TableInfo table, IProgress<int> progress,
        IEnumerable<TableInfo>? tablesLoadedLater = null);
}
