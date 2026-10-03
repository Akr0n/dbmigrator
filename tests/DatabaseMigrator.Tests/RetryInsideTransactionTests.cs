using System.Data;
using System.Data.Common;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// A statement retried after a transient error (a deadlock, a cut connection) is re-run with the same command. When the statement ran in
/// the table's transaction and the server has ended that transaction (a deadlock victim is rolled back as a whole), the driver quietly
/// drops it and the retry runs on its own, outside any transaction: a DELETE would be committed at once and the table would lose the
/// all-or-nothing behaviour. Such a statement must fail instead of being retried.
/// </summary>
public class RetryInsideTransactionTests
{
    [Fact]
    public async Task AStatementWhoseTransactionHasEnded_IsNotRetried()
    {
        int calls = 0;
        using var ended = new EndedTransaction();

        await Assert.ThrowsAsync<IOException>(() => new Probe().RunAsync(() =>
        {
            calls++;
            if (calls == 1)
                throw new IOException("the connection was cut or the statement was the deadlock victim");
            return Task.FromResult(calls);
        }, ended));

        Assert.Equal(1, calls); // a second run would have been outside any transaction
    }

    [Fact]
    public async Task AStatementWhoseTransactionIsStillOpen_IsRetriedAsBefore()
    {
        int calls = 0;
        using var connection = new StubConnection();
        using var open = new OpenTransaction(connection);

        int result = await new Probe().RunAsync(() =>
        {
            calls++;
            if (calls == 1)
                throw new IOException("a blip");
            return Task.FromResult(calls);
        }, open);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task AStatementWithoutATransaction_IsRetriedAsBefore()
    {
        int calls = 0;

        int result = await new Probe().RunAsync(() =>
        {
            calls++;
            if (calls == 1)
                throw new IOException("a blip");
            return Task.FromResult(calls);
        }, null);

        Assert.Equal(2, result);
    }

    /// <summary>Exposes the protected retry helper.</summary>
    private sealed class Probe : DatabaseService
    {
        public Task<int> RunAsync(Func<Task<int>> operation, DbTransaction? transaction) =>
            ExecuteWithRetryAsync(operation, "test", transaction);
    }

    /// <summary>A transaction the server has ended: the driver reports it by giving it no connection.</summary>
    private sealed class EndedTransaction : DbTransaction
    {
        public override void Commit() { }
        public override void Rollback() { }
        protected override DbConnection? DbConnection => null;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    }

    private sealed class OpenTransaction(DbConnection connection) : DbTransaction
    {
        public override void Commit() { }
        public override void Rollback() { }
        protected override DbConnection? DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    }

    private sealed class StubConnection : DbConnection
    {
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
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
