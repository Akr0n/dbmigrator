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
