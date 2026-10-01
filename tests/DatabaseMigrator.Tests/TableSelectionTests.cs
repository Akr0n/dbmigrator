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
