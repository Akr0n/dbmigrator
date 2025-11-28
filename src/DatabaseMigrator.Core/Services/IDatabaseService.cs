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
    Task CreateDatabaseAsync(ConnectionInfo connectionInfo);
    Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, string tableName, string schema);
    Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, TableInfo table, IProgress<int> progress);
}
