using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Tab "selezione tabelle": searching for a schema (e.g. ASM_DATI_GW) correctly listed that schema's tables, but
/// "Seleziona tutto" with the filter still active selected every table in the database.
/// </summary>
public class TableSelectionTests
{
    private static TableInfo T(string schema, string name) => new() { Schema = schema, TableName = name };

    private static List<TableInfo> Database() =>
    [
        T("ASM_DATI_GW", "ACT_RE_DEPLOYMENT"), T("ASM_DATI_GW", "ACT_GE_BYTEARRAY"), T("ASM_DATI_GW", "ACT_HI_PROCINST"),
        T("dbo", "Customers"), T("dbo", "Orders"),
        T("REPORTING", "Sales"),
    ];

    private static string[] Names(IEnumerable<TableInfo> tables) => tables.Select(t => $"{t.Schema}.{t.TableName}").ToArray();

    private static string[] Selected(IEnumerable<TableInfo> tables) => Names(tables.Where(t => t.IsSelected));

    // ── what the search box shows ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Visible_WithoutAFilter_ShowsEveryTable(string? filter)
    {
        var tables = Database();

        Assert.Equal(Names(tables), Names(TableSelection.Visible(tables, filter)));
    }

    [Fact]
    public void Visible_FilterOnASchema_ShowsOnlyThatSchemasTables_InTheirOriginalOrder()
    {
        var visible = TableSelection.Visible(Database(), "ASM_DATI_GW");

        Assert.Equal(["ASM_DATI_GW.ACT_RE_DEPLOYMENT", "ASM_DATI_GW.ACT_GE_BYTEARRAY", "ASM_DATI_GW.ACT_HI_PROCINST"], Names(visible));
    }

    [Fact]
    public void Visible_IgnoresCase()
    {
        Assert.Equal(3, TableSelection.Visible(Database(), "asm_dati_gw").Count);
    }

    [Theory]
    [InlineData("customers", 1)]     // table name
    [InlineData("act_", 3)]          // part of a table name
    [InlineData("dbo.orders", 1)]    // schema.table
    [InlineData("nothing-like-this", 0)]
    public void Visible_MatchesTableNamesAndQualifiedNames(string filter, int expected)
    {
        Assert.Equal(expected, TableSelection.Visible(Database(), filter).Count);
    }

    // ── "Seleziona tutto" ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectVisible_WithAnActiveFilter_SelectsOnlyTheTablesShown()
    {
        var tables = Database();

        TableSelection.SelectVisible(tables, "ASM_DATI_GW");

        Assert.Equal(["ASM_DATI_GW.ACT_RE_DEPLOYMENT", "ASM_DATI_GW.ACT_GE_BYTEARRAY", "ASM_DATI_GW.ACT_HI_PROCINST"], Selected(tables));
    }

    [Fact]
    public void SelectVisible_KeepsTablesSelectedUnderAnEarlierFilter()
    {
        // Filter one schema, select it; filter another, select it: the selection accumulates.
        var tables = Database();
        TableSelection.SelectVisible(tables, "REPORTING");

        TableSelection.SelectVisible(tables, "ASM_DATI_GW");

        Assert.Equal(4, tables.Count(t => t.IsSelected));
        Assert.Contains("REPORTING.Sales", Selected(tables));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SelectVisible_WithoutAFilter_SelectsEveryTable(string? filter)
    {
        var tables = Database();

        TableSelection.SelectVisible(tables, filter);

        Assert.Equal(tables.Count, tables.Count(t => t.IsSelected));
    }

    [Fact]
    public void SelectVisible_WhenTheFilterMatchesNothing_ChangesNothing()
    {
        var tables = Database();

        TableSelection.SelectVisible(tables, "nothing-like-this");

        Assert.Empty(Selected(tables));
    }

    // ── tables selected but hidden by the filter (warned about before a migration) ───────────────────

    [Fact]
    public void CountHidden_WithoutAFilter_IsZero()
    {
        var tables = Database();
        TableSelection.SelectVisible(tables, "");

        Assert.Equal(0, TableSelection.CountHidden(tables, ""));
    }

    [Fact]
    public void CountHidden_WhenEverySelectedTableIsShown_IsZero()
    {
        var tables = Database();
        TableSelection.SelectVisible(tables, "ASM_DATI_GW");

        Assert.Equal(0, TableSelection.CountHidden(tables, "ASM_DATI_GW"));
    }

    [Fact]
    public void CountHidden_CountsSelectedTablesTheFilterHides()
    {
        // The case behind the warning: select a schema, change the filter, select more, then migrate.
        var tables = Database();
        TableSelection.SelectVisible(tables, "ASM_DATI_GW");
        TableSelection.SelectVisible(tables, "dbo");

        Assert.Equal(3, TableSelection.CountHidden(tables, "dbo"));
        Assert.Equal(2, TableSelection.CountHidden(tables, "ASM_DATI_GW"));
    }

    [Fact]
    public void CountHidden_DoesNotCountTablesThatAreNotSelected()
    {
        Assert.Equal(0, TableSelection.CountHidden(Database(), "dbo"));
    }

    [Fact]
    public void CountHidden_WhenTheFilterMatchesNothing_CountsEverySelectedTable()
    {
        var tables = Database();
        TableSelection.SelectVisible(tables, "");

        Assert.Equal(6, TableSelection.CountHidden(tables, "nothing-like-this"));
    }

    // ── keeping the selection across "Aggiorna" ──────────────────────────────────────────────────────

    [Fact]
    public void CarryOver_SelectsTheReloadedTablesThatWereSelected()
    {
        var shown = Database();
        TableSelection.SelectVisible(shown, "ASM_DATI_GW");
        var reloaded = Database(); // new instances, nothing selected, as after a reload

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(Selected(shown), Selected(reloaded));
    }

    [Fact]
    public void CarryOver_KeepsAnythingTheUserDidWhileTheReloadWasRunning()
    {
        // "Aggiorna" starts with ASM_DATI_GW selected and the reloaded tables arrive later. In between the user selects
        // dbo.Orders and deselects ACT_RE_DEPLOYMENT on the tables still on screen. CarryOver reads the on-screen
        // tables when it is called, so it can only ever see the current selection: there is no earlier snapshot.
        var shown = Database();
        TableSelection.SelectVisible(shown, "ASM_DATI_GW");
        var reloaded = Database();                                   // the reload has finished loading...
        shown.Single(t => t.TableName == "Orders").IsSelected = true;               // ...but the user acted meanwhile
        shown.Single(t => t.TableName == "ACT_RE_DEPLOYMENT").IsSelected = false;

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(["ASM_DATI_GW.ACT_GE_BYTEARRAY", "ASM_DATI_GW.ACT_HI_PROCINST", "dbo.Orders"], Selected(reloaded));
    }

    [Fact]
    public void CarryOver_DoesNotSelectTablesThatWereNotSelected_NorTablesNewInTheSource()
    {
        var shown = Database();
        shown[0].IsSelected = true;
        var reloaded = Database();
        reloaded.Add(T("dbo", "BrandNew"));

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal([Names(shown)[0]], Selected(reloaded));
    }

    [Fact]
    public void CarryOver_IgnoresSelectedTablesThatNoLongerExistInTheSource()
    {
        var shown = Database();
        TableSelection.SelectVisible(shown, "");
        var reloaded = Database().Where(t => t.TableName != "Sales").ToList();

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(5, reloaded.Count(t => t.IsSelected));
    }

    [Fact]
    public void CarryOver_TellsSameNamedTablesInDifferentSchemasApart()
    {
        var shown = new List<TableInfo> { T("dbo", "Orders"), T("archive", "Orders") };
        shown[0].IsSelected = true;
        var reloaded = new List<TableInfo> { T("dbo", "Orders"), T("archive", "Orders") };

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(["dbo.Orders"], Selected(reloaded));
    }

    [Fact]
    public void CarryOver_DoesNotMergeNamesThatDifferOnlyByCase()
    {
        // A case-sensitive SQL Server database can hold both: they are different tables.
        var shown = new List<TableInfo> { T("dbo", "Orders"), T("dbo", "ORDERS") };
        shown[0].IsSelected = true;
        var reloaded = new List<TableInfo> { T("dbo", "Orders"), T("dbo", "ORDERS") };

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(["dbo.Orders"], Selected(reloaded));
    }

    [Fact]
    public void CarryOver_DoesNotMixUpNamesThatContainADot()
    {
        // Schema "a" + table "b.c" and schema "a.b" + table "c" would both read "a.b.c" if joined with a dot.
        var shown = new List<TableInfo> { T("a", "b.c"), T("a.b", "c") };
        shown[0].IsSelected = true;
        var reloaded = new List<TableInfo> { T("a", "b.c"), T("a.b", "c") };

        TableSelection.CarryOver(shown, reloaded);

        Assert.Equal(["a.b.c"], Selected(reloaded));
        Assert.False(reloaded[1].IsSelected);
    }

    // ── "Deseleziona tutto" ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeselectAll_AlsoClearsTablesTheFilterIsHiding()
    {
        // A hidden selected table would still be migrated, and emptied on the target, without the user seeing it.
        var tables = Database();
        TableSelection.SelectVisible(tables, "");

        TableSelection.DeselectAll(tables);

        Assert.Empty(Selected(tables));
    }
}
