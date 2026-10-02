using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Real-world trigger: a data load walks tables alphabetically, so ACT_GE_BYTEARRAY (child) was inserted
/// before ACT_RE_DEPLOYMENT (parent) and the target rejected it with a FOREIGN KEY violation.
/// </summary>
public class TableDependencyOrdererTests
{
    private static TableInfo T(string name, string schema = "dbo") => new() { Schema = schema, TableName = name };

    private static ForeignKeyInfo Fk(string child, string parent, string schema = "dbo") =>
        new($"FK_{child}_{parent}", schema, child, schema, parent, false);

    private static string[] Names(TableOrderResult r) => r.Tables.Select(t => t.TableName).ToArray();

    [Fact]
    public void ChildThatSortsBeforeItsParent_IsMovedAfterIt()
    {
        var result = TableDependencyOrderer.Order(
            [T("ACT_GE_BYTEARRAY"), T("ACT_RE_DEPLOYMENT")],
            [Fk("ACT_GE_BYTEARRAY", "ACT_RE_DEPLOYMENT")]);

        Assert.Equal(["ACT_RE_DEPLOYMENT", "ACT_GE_BYTEARRAY"], Names(result));
        Assert.False(result.HadCycles);
    }

    [Fact]
    public void ThreeLevelChain_LoadsGrandparentThenParentThenChild()
    {
        // Same shape as the fk_* fixtures: alphabetical order is child, grandparent, parent.
        var result = TableDependencyOrderer.Order(
            [T("fk_child"), T("fk_grandparent"), T("fk_parent")],
            [Fk("fk_child", "fk_parent"), Fk("fk_parent", "fk_grandparent")]);

        Assert.Equal(["fk_grandparent", "fk_parent", "fk_child"], Names(result));
    }

    [Fact]
    public void Diamond_PutsSharedParentFirstAndTheDependentLast()
    {
        var result = TableDependencyOrderer.Order(
            [T("a"), T("b"), T("c"), T("d")],
            [Fk("a", "b"), Fk("a", "c"), Fk("b", "d"), Fk("c", "d")]);

        Assert.Equal(["d", "b", "c", "a"], Names(result));
    }

    [Fact]
    public void UnrelatedTables_KeepTheirOriginalOrder()
    {
        var result = TableDependencyOrderer.Order([T("x"), T("m"), T("a")], []);

        Assert.Equal(["x", "m", "a"], Names(result));
    }

    [Fact]
    public void Reordering_DoesNotShuffleTablesThatAreNotInvolved()
    {
        var result = TableDependencyOrderer.Order(
            [T("x"), T("a_child"), T("y"), T("z_parent")],
            [Fk("a_child", "z_parent")]);

        var names = Names(result);
        Assert.True(Array.IndexOf(names, "z_parent") < Array.IndexOf(names, "a_child"));
        Assert.True(Array.IndexOf(names, "x") < Array.IndexOf(names, "y"));
        Assert.Equal(4, names.Length);
    }

    [Fact]
    public void SelfReference_IsNotACycle()
    {
        var result = TableDependencyOrderer.Order([T("category")], [Fk("category", "category")]);

        Assert.Equal(["category"], Names(result));
        Assert.False(result.HadCycles);
    }

    [Fact]
    public void Cycle_KeepsEveryTableAndIsReported()
    {
        var result = TableDependencyOrderer.Order(
            [T("a"), T("b"), T("c")],
            [Fk("a", "b"), Fk("b", "a")]);

        Assert.True(result.HadCycles);
        Assert.Equal(["a", "b", "c"], Names(result).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Cycle_DoesNotPullATableThatIsOnlyDownstreamOfItAheadOfItsParent()
    {
        // y and z reference each other; x merely references y, so x can and must still come after y.
        var result = TableDependencyOrderer.Order(
            [T("x"), T("y"), T("z")],
            [Fk("x", "y"), Fk("y", "z"), Fk("z", "y")]);

        var names = Names(result);
        Assert.True(result.HadCycles);
        Assert.True(Array.IndexOf(names, "y") < Array.IndexOf(names, "x"));
        Assert.Equal(3, names.Length);
    }

    [Fact]
    public void ParentOutsideTheSelection_DoesNotConstrainTheOrder()
    {
        var result = TableDependencyOrderer.Order(
            [T("b"), T("a")],
            [Fk("a", "not_selected"), Fk("b", "not_selected")]);

        Assert.Equal(["b", "a"], Names(result));
    }

    [Fact]
    public void NamesAreMatchedIgnoringCase_BecausePostgresLowercasesAndOracleUppercases()
    {
        // Tables come from the source catalog, foreign keys from the target catalog.
        var result = TableDependencyOrderer.Order(
            [T("Orders", "MIGRATION_TEST"), T("Users", "MIGRATION_TEST")],
            [new ForeignKeyInfo("orders_user_fk", "migration_test", "orders", "migration_test", "users", false)]);

        Assert.Equal(["Users", "Orders"], Names(result));
    }

    [Fact]
    public void SameTableNameInDifferentSchemas_AreDifferentTables()
    {
        var result = TableDependencyOrderer.Order(
            [T("child", "s1"), T("parent", "s2"), T("parent", "s1")],
            [new ForeignKeyInfo("fk", "s1", "child", "s1", "parent", false)]);

        var order = result.Tables.Select(t => $"{t.Schema}.{t.TableName}").ToList();
        Assert.True(order.IndexOf("s1.parent") < order.IndexOf("s1.child"));
    }
}
