using System.Data;
using System.Data.Common;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Closing the transaction of a table used to log an error and carry on whatever failed. For a rollback that is right (the caller is
/// already handling the error that made it roll back), but a COMMIT that fails means the rows are not saved: the table must not be
/// reported as migrated.
/// </summary>
public class FinalizeTransactionTests
{
    [Fact]
    public async Task AFailedCommit_IsNotSwallowed_SoTheTableIsNotReportedAsMigrated()
    {
        using var transaction = new FailingTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DatabaseService().FinalizeTransactionAsync(null!, transaction, DatabaseType.SqlServer, commit: true));
        Assert.Equal(1, transaction.CommitCalls);
    }

    [Fact]
    public async Task AFailedCommit_NamesTheTableAndKeepsTheCause()
    {
        using var transaction = new FailingTransaction();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DatabaseService().FinalizeTransactionAsync(null!, transaction, DatabaseType.SqlServer, commit: true, tableName: "SUM_DATI_GW.SUP_SUPPLIER"));

        Assert.Contains("SUM_DATI_GW.SUP_SUPPLIER", ex.Message, StringComparison.Ordinal);
        Assert.Contains("COMMIT", ex.Message, StringComparison.Ordinal);
        Assert.Contains("the commit could not be completed", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnOracleTheCommitSavesNothingNew_SoItsFailureDoesNotAbortTheRun()
    {
        // ODP.NET commits every statement as it runs (README: "On Oracle every statement is committed"): the COMMIT sent at the end of
        // a table has nothing left to save, and failing the table over it would drop tables whose rows are already in place.
        using var connection = new ThrowingConnection();

        await new DatabaseService().FinalizeTransactionAsync(connection, null, DatabaseType.Oracle, commit: true, tableName: "APP.T");

        Assert.Equal(1, connection.StatementsRun);
    }

    [Fact]
    public async Task AFailedRollback_IsOnlyLogged_BecauseTheCallerIsAlreadyHandlingTheError()
    {
        using var transaction = new FailingTransaction();

        await new DatabaseService().FinalizeTransactionAsync(null!, transaction, DatabaseType.SqlServer, commit: false);

        Assert.Equal(1, transaction.RollbackCalls);
    }

    [Fact]
    public async Task ACommitThatWorks_CompletesQuietly()
    {
        using var transaction = new FailingTransaction { CommitFails = false };

        await new DatabaseService().FinalizeTransactionAsync(null!, transaction, DatabaseType.PostgreSQL, commit: true);

        Assert.Equal(1, transaction.CommitCalls);
    }

    // ── a transaction the server ended is replaced, not ignored ──────────────────────────────────────────────

    [Fact]
    public void ATransactionThatHasEnded_IsReplacedByANewOne()
    {
        // The driver reports an ended transaction by giving it no connection; a command handed one runs on its own, outside any transaction.
        using var connection = new BeginningConnection();
        using var ended = new FailingTransaction(); // its DbConnection is null: it has ended

        var current = DatabaseService.RestartTransactionIfEnded(connection, ended);

        Assert.NotSame(ended, current);
        Assert.NotNull(current?.Connection);
        Assert.Equal(1, connection.TransactionsBegun);
    }

    [Fact]
    public void ATransactionThatIsStillOpen_IsKept()
    {
        using var connection = new BeginningConnection();
        using var live = new LiveTransaction(connection);

        Assert.Same(live, DatabaseService.RestartTransactionIfEnded(connection, live));
        Assert.Equal(0, connection.TransactionsBegun);
    }

    [Fact]
    public void WithoutATransaction_AsOnOracle_NothingIsBegun()
    {
        using var connection = new BeginningConnection();

        Assert.Null(DatabaseService.RestartTransactionIfEnded(connection, null));
        Assert.Equal(0, connection.TransactionsBegun);
    }

    private sealed class BeginningConnection : DbConnection
    {
        public int TransactionsBegun { get; private set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "stub";
        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "stub";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            TransactionsBegun++;
            return new LiveTransaction(this);
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class LiveTransaction(DbConnection connection) : DbTransaction
    {
        public override void Commit() { }
        public override void Rollback() { }
        protected override DbConnection? DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    }

    /// <summary>A connection whose every statement fails, and that counts how many it was asked to run.</summary>
    private sealed class ThrowingConnection : DbConnection
    {
        public int StatementsRun { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "stub";
        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "stub";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);
    }

    private sealed class ThrowingCommand(ThrowingConnection owner) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            owner.StatementsRun++;
            throw new InvalidOperationException("the server went away");
        }

        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FailingTransaction : DbTransaction
    {
        public bool CommitFails { get; init; } = true;
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public override void Commit()
        {
            CommitCalls++;
            if (CommitFails)
                throw new InvalidOperationException("the commit could not be completed");
        }

        public override void Rollback()
        {
            RollbackCalls++;
            throw new InvalidOperationException("the rollback could not be completed");
        }

        protected override DbConnection? DbConnection => null;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    }
}
