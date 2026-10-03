using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using DatabaseMigrator.Core.Models;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// What it takes to carry on reading a table from the source after the connection to it was lost in the middle of the stream.
/// A table is read in one pass while its rows are inserted into the target in one transaction; the target connection stays
/// alive, so only the source side has to start again, from the row after the last one received.
/// That needs a read that comes back in the same order: the first read is ordered by the primary key, and a later one skips
/// the rows already consumed (OFFSET). A table without a primary key has no such order and cannot be resumed.
/// </summary>
internal static class SourceResume
{
    /// <summary>
    /// <paramref name="selectAll"/> ordered by the (already formatted) key columns, skipping the first
    /// <paramref name="skipRows"/> rows when it is more than 0. Every dialect the tool reads from has OFFSET
    /// (SQL Server 2012, PostgreSQL, Oracle 12c).
    /// </summary>
    internal static string OrderedSelect(DatabaseType dialect, string selectAll, IReadOnlyList<string> keyColumns, long skipRows)
    {
        string sql = $"{selectAll} ORDER BY {string.Join(", ", keyColumns)}";
        if (skipRows <= 0)
            return sql;

        string skip = skipRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return dialect == DatabaseType.PostgreSQL ? $"{sql} OFFSET {skip}" : $"{sql} OFFSET {skip} ROWS";
    }

    /// <summary>
    /// Whether a table is read in key order (and so can resume): when it has at least <paramref name="minRows"/> rows, or when
    /// its size is not known (<paramref name="totalRows"/> negative: the count failed), which is no reason to give up resuming
    /// on what may be the biggest table of the run.
    /// </summary>
    internal static bool ShouldOrderRead(long totalRows, long minRows) => totalRows < 0 || totalRows >= minRows;

    /// <summary>
    /// A column name quoted as the SOURCE catalog spells it, for the ORDER BY. Not the case folding the migration applies to
    /// names it writes to a target (lower case on PostgreSQL, upper case on Oracle): a quoted mixed-case key column such as
    /// "CustomerID" is found by that exact name only, and folding it would make a read that works today fail.
    /// </summary>
    internal static string QuoteColumn(DatabaseType dialect, string column) => dialect == DatabaseType.SqlServer
        ? $"[{column.Replace("]", "]]")}]"
        : $"\"{column.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Whether <paramref name="exception"/>, raised while reading on <paramref name="connection"/>, is the connection having
    /// gone away (the driver itself reports it no longer open) and not a problem with the data or the code, which reading
    /// again would only repeat.
    /// </summary>
    internal static bool IsConnectionLoss(Exception exception, DbConnection connection)
    {
        if (exception is OperationCanceledException)
            return false;

        if (connection.State is ConnectionState.Closed or ConnectionState.Broken)
            return true;

        // A driver does not always change the state of a connection it has just lost: SqlClient on Linux raises SqlException ->
        // IOException -> SocketException "Connection reset by peer" and leaves the state at Open. A failure of the transport
        // itself, an I/O or socket error anywhere in the chain, is a loss too.
        for (var inner = exception; inner != null; inner = inner.InnerException)
        {
            if (inner is IOException or SocketException)
                return true;
        }

        return false;
    }

    /// <summary>The primary key columns of a table, in key order; empty when it has none.</summary>
    internal static async Task<List<string>> GetKeyColumnsAsync(DbConnection connection, DatabaseType dialect, string schema,
        string table, int commandTimeoutSeconds)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = dialect switch
        {
            // Only a key that is enforced and checked gives every row a place of its own: a DISABLEd one, or one enabled without
            // validation (ENABLE NOVALIDATE), lets rows tie, and rows that tie have no fixed order for a resumed read to come back to.
            DatabaseType.Oracle => @"SELECT cc.column_name
                FROM all_constraints c
                JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
                WHERE c.constraint_type = 'P' AND c.status = 'ENABLED' AND c.validated = 'VALIDATED'
                  AND c.owner = :schema AND c.table_name = :tbl
                ORDER BY cc.position",
            // The system catalog, not information_schema: that view lists a table only to the roles that own it or hold a privilege
            // other than SELECT, so a read-only source account found no key at all. A table other tables INHERIT from has no usable
            // key: SELECT * on it also returns their rows, and its key is not unique across them. A PARTITIONED table (relkind 'p') is
            // recorded in pg_inherits too, but its key has to contain the partition key, so it is unique across the partitions.
            DatabaseType.PostgreSQL => @"SELECT a.attname
                FROM pg_catalog.pg_constraint c
                JOIN pg_catalog.pg_class t ON t.oid = c.conrelid
                JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum
                WHERE c.contype = 'p' AND n.nspname = @schema AND t.relname = @tbl
                  AND (t.relkind = 'p' OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhparent = t.oid))
                ORDER BY k.ord",
            _ => @"SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_schema = tc.constraint_schema AND kcu.constraint_name = tc.constraint_name
                 AND kcu.table_schema = tc.table_schema AND kcu.table_name = tc.table_name
                WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = @schema AND tc.table_name = @tbl
                ORDER BY kcu.ordinal_position"
        };
        if (command is OracleCommand oracleCommand)
            oracleCommand.BindByName = true;
        foreach (var (name, value) in new[] { ("schema", schema), ("tbl", table) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns;
    }
}

/// <summary>The connection, command and reader a resumed read opened: closed together, and again before each new attempt.</summary>
internal sealed class ResumedRead : IDisposable
{
    public DbConnection? Connection { get; set; }
    public DbCommand? Command { get; set; }
    public DbDataReader? Reader { get; set; }

    public void Dispose()
    {
        // They belong to a connection that is already gone: closing them must not raise.
        try { Reader?.Dispose(); } catch { }
        try { Command?.Dispose(); } catch { }
        try { Connection?.Dispose(); } catch { }
        Reader = null;
        Command = null;
        Connection = null;
    }
}

/// <summary>
/// How often a lost connection is opened again in a row, without a single new row arriving in between, before the table is
/// given up, and how long to wait each time (doubling, up to a cap). A new row since the last loss starts the count again,
/// so a very long table can be resumed as many times as the network cuts it.
/// </summary>
internal sealed class ResumePolicy(int maxAttemptsWithoutProgress, TimeSpan firstDelay, TimeSpan maxDelay)
{
    private long _rowsAtLastFailure = -1;
    private int _failuresWithoutProgress;

    /// <summary>Records a loss after <paramref name="rowsConsumed"/> rows. Returns the time to wait, or null to give up.</summary>
    public TimeSpan? OnFailure(long rowsConsumed)
    {
        _failuresWithoutProgress = rowsConsumed > _rowsAtLastFailure ? 1 : _failuresWithoutProgress + 1;
        _rowsAtLastFailure = rowsConsumed;
        if (_failuresWithoutProgress > maxAttemptsWithoutProgress)
            return null;

        double milliseconds = firstDelay.TotalMilliseconds * Math.Pow(2, _failuresWithoutProgress - 1);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, maxDelay.TotalMilliseconds));
    }
}
