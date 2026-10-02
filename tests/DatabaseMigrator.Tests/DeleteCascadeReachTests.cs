using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Oracle empties a target table with DELETE, and an enabled key with ON DELETE CASCADE / SET NULL then removes or
/// modifies rows in tables the user may not have selected. These tests pin down which tables that reaches.
/// </summary>
public class DeleteCascadeReachTests
{
    private static DeleteRuleEdge Cascade(string child, string parent, string schema = "APP") => new(schema, child, schema, parent, true);

    private static DeleteRuleEdge SetNull(string child, string parent, string schema = "APP") => new(schema, child, schema, parent, false);

    private static string[] Reach(string root, params DeleteRuleEdge[] edges) =>
        DeleteCascadeReach.Affected("APP", root, edges).Select(t => $"{t.Schema}.{t.Table}").Order().ToArray();

    [Fact]
    public void WithoutAnyKey_NothingIsReached() => Assert.Empty(Reach("HEAD"));

    [Fact]
    public void ACascadeChild_IsReached() => Assert.Equal(["APP.CHILD"], Reach("HEAD", Cascade("CHILD", "HEAD")));

    [Fact]
    public void ASetNullChild_IsReached_BecauseItsRowsAreModified() => Assert.Equal(["APP.CHILD"], Reach("HEAD", SetNull("CHILD", "HEAD")));

    [Fact]
    public void CascadePropagatesThroughCascadeChildren()
    {
        var reached = Reach("HEAD", Cascade("MID", "HEAD"), Cascade("LEAF", "MID"));

        Assert.Equal(["APP.LEAF", "APP.MID"], reached);
    }

    [Fact]
    public void ASetNullChildOfACascadeChild_IsReached_ButASetNullChildDoesNotPropagate()
    {
        // MID loses its rows, so LEAF (SET NULL on MID) is modified; LEAF is only updated, so DEEP below it is not touched.
        var reached = Reach("HEAD", Cascade("MID", "HEAD"), SetNull("LEAF", "MID"), Cascade("DEEP", "LEAF"));

        Assert.Equal(["APP.LEAF", "APP.MID"], reached);
    }

    [Fact]
    public void ATableThatOnlyReferencesAnotherParent_IsNotReached() =>
        Assert.Equal(["APP.CHILD"], Reach("HEAD", Cascade("CHILD", "HEAD"), Cascade("OTHER_CHILD", "OTHER_PARENT")));

    [Fact]
    public void ASelfReference_IsIgnored() => Assert.Empty(Reach("TREE", Cascade("TREE", "TREE")));

    [Fact]
    public void ACycleBackToTheRoot_DoesNotLoopAndNeverReportsTheRoot()
    {
        var reached = Reach("A", Cascade("B", "A"), Cascade("A", "B"));

        Assert.Equal(["APP.B"], reached);
    }

    [Fact]
    public void ATableReachedTwice_IsReportedOnce()
    {
        var reached = Reach("HEAD", Cascade("MID", "HEAD"), Cascade("LEAF", "HEAD"), Cascade("LEAF", "MID"));

        Assert.Equal(["APP.LEAF", "APP.MID"], reached);
    }

    [Fact]
    public void NamesAreMatchedIgnoringCase()
    {
        var reached = DeleteCascadeReach.Affected("app", "head", [new DeleteRuleEdge("APP", "CHILD", "APP", "HEAD", true)]);

        Assert.Equal([("APP", "CHILD")], reached);
    }

    [Fact]
    public void TheSameTableNameInAnotherSchema_IsADifferentTable()
    {
        var reached = DeleteCascadeReach.Affected("APP", "HEAD", [new DeleteRuleEdge("OTHER", "CHILD", "OTHER", "HEAD", true)]);

        Assert.Empty(reached);
    }

    [Fact]
    public void AChildInAnotherSchema_IsReachedWithItsOwnSchema()
    {
        var reached = DeleteCascadeReach.Affected("APP", "HEAD", [new DeleteRuleEdge("AUDIT", "LOG", "APP", "HEAD", true)]);

        Assert.Equal([("AUDIT", "LOG")], reached);
    }
}
