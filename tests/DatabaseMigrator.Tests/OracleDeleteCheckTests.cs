using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using Oracle.ManagedDataAccess.Client;

namespace DatabaseMigrator.Tests;

/// <summary>
/// The check that runs before an Oracle table is emptied with DELETE (does an ON DELETE CASCADE key reach tables the migration
/// does not load?) is a safeguard: when it cannot do its job it must not let the DELETE go ahead, and it must not make a reload
/// of thousands of populated tables wait for a dictionary read per table. These tests run it against an in-memory connection.
/// </summary>
public class OracleDeleteCheckTests
{
    private static readonly TableInfo Head = new() { Schema = "APP", TableName = "HEAD" };

    // ── a failed catalog query is not "no cascade keys" ──────────────────────────

    [Fact]
    public async Task AFailureWhileReadingTheKeys_IsNotSwallowed_SoTheDeleteIsNeverReachedUnchecked()
    {
        // A catalog query can fail for reasons that say nothing about the keys (ORA-02393, a call-limit profile; a timeout; a
        // cancelled statement). Reading that as "no keys" let the DELETE cascade into tables nobody had looked at.
        // The grants query works, so an implementation that reads a failed key query as "no keys" would answer instead of failing.
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("all_tab_privs") ? Names().CreateDataReader() : throw OracleError(2393));

        await Assert.ThrowsAsync<OracleException>(() =>
            new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP"));
    }

    [Fact]
    public async Task AFailedQueryForTheGrants_IsNotSwallowedEither()
    {
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("all_tab_privs") ? throw OracleError(2393)
                : command.Contains("dba_constraints") ? throw OracleError(942)
                : Keys().CreateDataReader());

        await Assert.ThrowsAsync<OracleException>(() =>
            new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP"));
    }

    // ── without access to the DBA views the check is as careful as it can be ─────

    [Fact]
    public async Task WithoutAccessToTheDbaViews_TheVisibleKeysAreStillChecked()
    {
        var keys = Keys(("APP", "CHILD", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null, // every table has rows
            command => command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names().CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(["APP.CHILD"], check.Populated);
        Assert.Empty(check.UnverifiableSchemas);
        Assert.Empty(check.NotOwned!);
    }

    [Fact]
    public async Task ASchemaGrantedReferences_IsUnverifiable_EvenWhenOneOfItsKeysIsVisible()
    {
        // G owns two children of HEAD. The user may read one (empty) and sees its key; the other one it cannot see at all. That
        // one key being visible does not make the schema verified: it used to, and the hidden child was emptied by the cascade.
        var keys = Keys(("G", "VISIBLE_CHILD", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("APP.HEAD") ? 1 : null, // HEAD has rows, the visible child is empty
            command => command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names("G").CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(["G su APP.HEAD"], check.UnverifiableSchemas);
        Assert.Empty(check.NotOwned!); // the visible child is empty: nothing to cascade, so its (unreadable) grants need not be asked
        Assert.Empty(check.Populated);
    }

    [Fact]
    public async Task AGrandchildBehindACascadeChild_HasItsGrantsChecked_EvenWhenTheChildIsLoadedLater()
    {
        // HEAD cascades to C1 (owned by the user), and a schema S was granted REFERENCES on C1: S may own a populated table that
        // cascades from C1. Emptying HEAD empties C1 through the cascade and that table with it, hidden from the user, although
        // C1 is itself reloaded later. Only HEAD's grants used to be read.
        var keys = Keys(("APP", "C1", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null, // HEAD and C1 hold rows
            readerWithBinds: (command, binds) =>
                command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names(binds["tab"]!.ToString() == "C1" ? ["S"] : []).CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [new TableInfo { Schema = "APP", TableName = "C1" }], "APP");

        Assert.Equal(["S su APP.C1"], check.UnverifiableSchemas); // and it says on which table the grant is
    }

    [Fact]
    public async Task ASetNullChildWithAGrant_IsNotRefused_BecauseNothingBelowItIsDeleted()
    {
        // HEAD -> C1 is SET NULL: C1 only has a column nulled, so whatever references C1 loses nothing. Asking about C1's grants
        // refused a reload that could not harm anything.
        var keys = Keys(("APP", "C1", "APP", "HEAD", 0));
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            readerWithBinds: (command, binds) =>
                command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names(binds["tab"]!.ToString() == "C1" ? ["S"] : []).CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [new TableInfo { Schema = "APP", TableName = "C1" }], "APP");

        Assert.Empty(check.UnverifiableSchemas);
    }

    [Fact]
    public async Task AnEmptyCascadeChildWithAGrant_IsNotRefused_BecauseItHasNothingToCascade()
    {
        var keys = Keys(("APP", "C1", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("APP.HEAD") ? 1 : null, // only HEAD has rows
            readerWithBinds: (command, binds) =>
                command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names(binds["tab"]!.ToString() == "C1" ? ["S"] : []).CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [new TableInfo { Schema = "APP", TableName = "C1" }], "APP");

        Assert.Empty(check.UnverifiableSchemas);
    }

    [Fact]
    public async Task TheGrantsOfATable_AreReadOnceNotOncePerRootThatReachesIt()
    {
        var keys = Keys(("APP", "C1", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names().CreateDataReader()
                : keys.CreateDataReader());
        var service = new DatabaseService();

        await service.CheckOracleDeleteAsync(connection, Head, [], "APP");
        int afterTheFirst = connection.CountOf("all_tab_privs");
        for (int i = 0; i < 5; i++)
            await service.CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(2, afterTheFirst);                              // HEAD and C1
        Assert.Equal(afterTheFirst, connection.CountOf("all_tab_privs")); // never again within the lifetime of the keys
    }

    [Fact]
    public async Task ATableOfAnotherSchemaInTheCascade_CannotHaveItsGrantsRead()
    {
        var keys = Keys(("OTHER", "CHILD", "APP", "HEAD", 1));
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null, // the other schema's child holds rows
            command => command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_tab_privs") ? Names().CreateDataReader()
                : keys.CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(["OTHER.CHILD"], check.NotOwned);
    }

    [Fact]
    public async Task AGrantOfReferencesOnSomeColumnsOnly_IsReadToo()
    {
        // It is recorded in ALL_COL_PRIVS, not in ALL_TAB_PRIVS: the schema holding it can own a table that cascades from here.
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("dba_constraints") ? throw OracleError(942)
                : command.Contains("all_col_privs") ? Names("S").CreateDataReader()
                : Keys().CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(["S su APP.HEAD"], check.UnverifiableSchemas);
    }

    // ── the keys are kept for a short while, and for the view they were read from ─

    [Fact]
    public async Task TheKeys_AreReadAgainOnceTheirLifetimeIsOver()
    {
        var clock = new StepClock();
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => Keys().CreateDataReader());
        var service = new DatabaseService { Clock = clock };

        await service.CheckOracleDeleteAsync(connection, Head, [], "APP");
        clock.Now += TimeSpan.FromSeconds(10);
        await service.CheckOracleDeleteAsync(connection, Head, [], "APP");
        Assert.Equal(1, connection.CountOf("dba_constraints")); // still fresh

        clock.Now += TimeSpan.FromSeconds(30);
        await service.CheckOracleDeleteAsync(connection, Head, [], "APP");

        Assert.Equal(2, connection.CountOf("dba_constraints")); // it never expired: a key added later would never be seen
    }

    [Fact]
    public async Task TheKeysReadFromTheVisibleView_AreNotTakenForTheCompleteOnes()
    {
        // Without access to DBA_CONSTRAINTS the visible keys are cached. A later check on the same service must still find the
        // DBA view unreadable: answering it from the cache made the incomplete list look complete, and every grant check was skipped.
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("dba_constraints") ? throw OracleError(942) : Keys().CreateDataReader());
        var service = new DatabaseService();

        var first = await service.CheckOracleDeleteAsync(connection, Head, [], "ETL");
        var second = await service.CheckOracleDeleteAsync(connection, Head, [], "ETL");

        Assert.Equal(["APP.HEAD"], first.NotOwned);
        Assert.Equal(["APP.HEAD"], second.NotOwned);
    }

    private sealed class StepClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task AUserThatDoesNotOwnTheTable_CannotSeeWhoIsGrantedReferences()
    {
        // The grants of a table are visible to its owner, the grantor and the grantee only: for anyone else "nobody was
        // granted anything" and "I cannot see" look the same, so the check cannot say the table is safe to empty.
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("dba_constraints") ? throw OracleError(942) : Keys().CreateDataReader());

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "ETL");

        Assert.Equal(["APP.HEAD"], check.NotOwned);
        Assert.Equal(0, connection.CountOf("all_tab_privs"));
    }

    [Fact]
    public async Task WithAccessToTheDbaViews_TheAnswerIsExact_SoNothingIsFlaggedAsUnverifiable()
    {
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => Keys().CreateDataReader()); // dba_constraints readable, no keys

        var check = await new DatabaseService().CheckOracleDeleteAsync(connection, Head, [], "ETL"); // not the owner: does not matter

        Assert.Empty(check.NotOwned!);
        Assert.Empty(check.UnverifiableSchemas);
        Assert.Empty(check.Populated);
    }

    // ── the keys are read once for a run, not once per table ─────────────────────

    [Fact]
    public async Task TheKeys_AreReadOnceForSeveralTablesOfTheSameRun()
    {
        using var connection = new FakeConnection(
            command => command.Contains("ROWNUM = 1") ? 1 : null,
            command => command.Contains("dba_constraints") ? Keys().CreateDataReader() : new DataTable().CreateDataReader());
        var service = new DatabaseService();

        for (int i = 0; i < 5; i++)
            await service.CheckOracleDeleteAsync(connection, new TableInfo { Schema = "APP", TableName = $"T{i}" }, [], "APP");

        // It used to be one read per table: a full scan of DBA_CONSTRAINTS joined to itself, each time.
        Assert.Equal(1, connection.CountOf("dba_constraints"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>The rows the key query returns: child owner, child table, parent owner, parent table, 1 when it cascades.</summary>
    private static DataTable Keys(params (string COwner, string CTable, string POwner, string PTable, int Cascades)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("c_owner", typeof(string));
        table.Columns.Add("c_table", typeof(string));
        table.Columns.Add("p_owner", typeof(string));
        table.Columns.Add("p_table", typeof(string));
        table.Columns.Add("cascades", typeof(int));
        foreach (var row in rows)
            table.Rows.Add(row.COwner, row.CTable, row.POwner, row.PTable, row.Cascades);
        return table;
    }

    private static DataTable Names(params string[] names)
    {
        var table = new DataTable();
        table.Columns.Add("name", typeof(string));
        foreach (var name in names)
            table.Rows.Add(name);
        return table;
    }

    /// <summary>An error as the Oracle driver raises it (its constructors are not public).</summary>
    private static OracleException OracleError(int number) =>
        (OracleException)Activator.CreateInstance(typeof(OracleException),
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null,
            new object[] { number, $"ORA-{number:00000}", "fake", "fake", 0 }, null)!;

    // ── an in-memory connection ─────────────────────────────────────────────────

    private sealed class FakeConnection : DbConnection
    {
        private readonly Func<string, object?> _scalar;
        private readonly Func<string, DbDataReader>? _reader;
        private readonly Func<string, IReadOnlyDictionary<string, object?>, DbDataReader>? _readerWithBinds;
        private readonly List<string> _executed = [];

        public FakeConnection(Func<string, object?> scalar, Func<string, DbDataReader>? reader = null,
            Func<string, IReadOnlyDictionary<string, object?>, DbDataReader>? readerWithBinds = null)
        {
            _scalar = scalar;
            _reader = reader;
            _readerWithBinds = readerWithBinds;
        }

        public int CountOf(string fragment) => _executed.Count(sql => sql.Contains(fragment));

        internal object? Scalar(string sql) { _executed.Add(sql); return _scalar(sql); }

        internal DbDataReader Reader(string sql, IReadOnlyDictionary<string, object?> binds)
        {
            _executed.Add(sql);
            if (_readerWithBinds != null) return _readerWithBinds(sql, binds);
            if (_reader == null) throw new InvalidOperationException("no reader configured for: " + sql);
            return _reader(sql);
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeCommand(this);
    }

    private sealed class FakeCommand : DbCommand
    {
        private readonly FakeConnection _owner;
        public FakeCommand(FakeConnection owner) => _owner = owner;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameters();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => _owner.Scalar(CommandText);
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            _owner.Reader(CommandText, DbParameterCollection.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value));
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = "";
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParameters : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }
}
