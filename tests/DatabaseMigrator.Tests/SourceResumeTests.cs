using System.Data;
using System.Data.Common;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// A real migration read a table of millions of rows from the source in one stream, and the connection to the source was cut
/// in the middle of it ("forcibly closed by the remote host"), after 8 minutes and again after 21: the whole table was lost
/// each time. These tests pin down the pieces that let the read pick up where it stopped.
/// </summary>
public class SourceResumeTests
{
    private const string Select = "SELECT * FROM T";

    // ── the query: ordered from the first row on, so a later read can skip what was already read ─────────────

    [Fact]
    public void TheFirstReadIsOrderedByTheKey()
    {
        Assert.Equal("SELECT * FROM T ORDER BY [id]", SourceResume.OrderedSelect(DatabaseType.SqlServer, Select, ["[id]"], 0));
    }

    [Theory]
    [InlineData(DatabaseType.SqlServer, "SELECT * FROM T ORDER BY [a], [b] OFFSET 1146000 ROWS")]
    [InlineData(DatabaseType.PostgreSQL, "SELECT * FROM T ORDER BY [a], [b] OFFSET 1146000")]
    [InlineData(DatabaseType.Oracle, "SELECT * FROM T ORDER BY [a], [b] OFFSET 1146000 ROWS")]
    public void AResumedReadSkipsTheRowsAlreadyConsumed(DatabaseType dialect, string expected)
    {
        Assert.Equal(expected, SourceResume.OrderedSelect(dialect, Select, ["[a]", "[b]"], 1_146_000));
    }

    // ── the key columns are quoted as the SOURCE spells them ─────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.SqlServer, "Id", "[Id]")]
    [InlineData(DatabaseType.SqlServer, "we]ird", "[we]]ird]")]
    [InlineData(DatabaseType.PostgreSQL, "CustomerID", "\"CustomerID\"")] // not lower-cased: that is how the target spells it, not the source
    [InlineData(DatabaseType.PostgreSQL, "we\"ird", "\"we\"\"ird\"")]
    [InlineData(DatabaseType.Oracle, "Id", "\"Id\"")]                     // exact case, quoted: a quoted mixed-case column is found by that name only
    [InlineData(DatabaseType.Oracle, "ID", "\"ID\"")]
    public void AKeyColumnIsQuotedWithTheSpellingTheSourceCatalogGives(DatabaseType dialect, string column, string expected)
    {
        Assert.Equal(expected, SourceResume.QuoteColumn(dialect, column));
    }

    // ── what counts as a lost connection ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConnectionState.Closed)]
    [InlineData(ConnectionState.Broken)]
    public void AnErrorOnAConnectionThatIsNoLongerOpen_IsALoss(ConnectionState state)
    {
        using var connection = new StubConnection(state);

        Assert.True(SourceResume.IsConnectionLoss(new IOException("forcibly closed by the remote host"), connection));
    }

    [Fact]
    public void AnErrorOnAConnectionThatIsStillOpen_IsNotALoss()
    {
        // A cast that fails or a value that does not fit is a defect of the data or of the code: reading again would repeat it.
        using var connection = new StubConnection(ConnectionState.Open);

        Assert.False(SourceResume.IsConnectionLoss(new InvalidCastException(), connection));
    }

    [Fact]
    public void ACancellation_IsNotALoss()
    {
        using var connection = new StubConnection(ConnectionState.Closed);

        Assert.False(SourceResume.IsConnectionLoss(new OperationCanceledException(), connection));
    }

    // ── how often, and how long to wait ──────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public void AConnectionThatKeepsFailingWithoutAnyNewRow_IsGivenUpAfterTheAllowedAttempts()
    {
        var policy = new ResumePolicy(maxAttemptsWithoutProgress: 3, firstDelay: 2 * Second, maxDelay: 30 * Second);

        Assert.Equal(2 * Second, policy.OnFailure(500)); // the first loss
        Assert.Equal(4 * Second, policy.OnFailure(500)); // nothing new arrived since: the wait doubles
        Assert.Equal(8 * Second, policy.OnFailure(500));
        Assert.Null(policy.OnFailure(500));              // a fourth failure in a row, still at row 500: give up
    }

    [Fact]
    public void AnyNewRowStartsTheCountAgain_SoALongTableCanResumeManyTimes()
    {
        var policy = new ResumePolicy(maxAttemptsWithoutProgress: 2, firstDelay: 2 * Second, maxDelay: 30 * Second);

        Assert.Equal(2 * Second, policy.OnFailure(1_000));
        Assert.Equal(4 * Second, policy.OnFailure(1_000));
        Assert.Equal(2 * Second, policy.OnFailure(1_001_000)); // a million rows later: the first loss of a new streak
        Assert.Equal(2 * Second, policy.OnFailure(2_001_000));
    }

    [Fact]
    public void TheWaitStopsGrowingAtTheCap()
    {
        var policy = new ResumePolicy(maxAttemptsWithoutProgress: 10, firstDelay: 5 * Second, maxDelay: 12 * Second);

        var waits = Enumerable.Range(0, 4).Select(_ => policy.OnFailure(7)).ToList();

        Assert.Equal([5 * Second, 10 * Second, 12 * Second, 12 * Second], waits);
    }

    // ── a connection the test can put in any state ───────────────────────────────────────────────────────────

    private sealed class StubConnection(ConnectionState state) : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "stub";
        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "stub";
        public override ConnectionState State => state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
