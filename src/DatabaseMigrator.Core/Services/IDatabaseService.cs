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
    /// <param name="tablesLoadedLater">
    /// The tables this migration will still load after <paramref name="table"/>. Emptying <paramref name="table"/> on the
    /// target spreads to other tables (PostgreSQL: TRUNCATE ... CASCADE empties every table that references it; Oracle: DELETE
    /// follows ON DELETE CASCADE / SET NULL keys); only these, or tables that are already empty, may be touched that way.
    /// </param>
    Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, TableInfo table, IProgress<int> progress,
        IEnumerable<TableInfo>? tablesLoadedLater = null);
}
