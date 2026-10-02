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

    private static string[] Lost(string root, params DeleteRuleEdge[] edges) =>
        DeleteCascadeReach.Deleted("APP", root, edges).Select(t => $"{t.Schema}.{t.Table}").Order().ToArray();

    [Fact]
    public void OnlyTheTablesThatLoseRowsAreDeleted_NotThoseThatOnlyHaveAColumnNulled()
    {
        // MID loses its rows (CASCADE) and so does LEAF below it; SET_NULL_CHILD is only updated, and what is below it is not touched.
        var edges = new[]
        {
            Cascade("MID", "HEAD"), Cascade("LEAF", "MID"), SetNull("SET_NULL_CHILD", "HEAD"), Cascade("UNDER_SET_NULL", "SET_NULL_CHILD"),
        };

        Assert.Equal(["APP.LEAF", "APP.MID"], Lost("HEAD", edges));
        Assert.Equal(["APP.LEAF", "APP.MID", "APP.SET_NULL_CHILD"], Reach("HEAD", edges)); // all of them are still "affected"
    }

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
    public void NamesAreMatchedExactly_BecauseOracleKeepsTheCaseOfAQuotedName()
    {
        // The root is the upper-case name the DELETE targets, the keys come with the catalog's own spelling: "Head" is another
        // table than HEAD, and a key to it does not reach HEAD's children.
        Assert.Empty(DeleteCascadeReach.Affected("app", "head", [new DeleteRuleEdge("APP", "CHILD", "APP", "HEAD", true)]));
        Assert.Equal([("APP", "CHILD")], DeleteCascadeReach.Affected("APP", "HEAD", [new DeleteRuleEdge("APP", "CHILD", "APP", "HEAD", true)]));
    }

    [Fact]
    public void ATableThatDiffersFromTheRootOnlyByCase_IsADifferentTable_NotTheRoot()
    {
        // A quoted "Head" can exist next to HEAD in one schema, and the cascade of HEAD reaches it. It was dropped as "the root".
        Assert.Equal(["APP.Head"], Reach("HEAD", Cascade("Head", "HEAD")));
        Assert.Equal(["APP.Head"], Lost("HEAD", Cascade("Head", "HEAD")));
    }

    [Fact]
    public void TwoChildrenThatDifferOnlyByCase_AreBothReached()
    {
        // Merged by an upper-cased key, whichever the catalog listed first hid the other (and an empty one hid a populated one).
        var reached = Reach("HEAD", Cascade("Foo", "HEAD"), Cascade("FOO", "HEAD"));

        Assert.Equal(2, reached.Length);
        Assert.Contains("APP.Foo", reached);
        Assert.Contains("APP.FOO", reached);
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
