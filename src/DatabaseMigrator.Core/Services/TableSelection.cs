using System;
using System.Collections.Generic;
using System.Linq;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// Which tables the search box shows, and what "select all" / "deselect all" do to them.
/// </summary>
public static class TableSelection
{
    /// <summary>
    /// The tables the search filter currently shows, in their original order; all of them when the filter is empty.
    /// A table matches when its name, its schema or "schema.name" contains the filter, ignoring case.
    /// This is the single definition of "visible", shared by the list the user sees and by "select all".
    /// </summary>
    public static List<TableInfo> Visible(IEnumerable<TableInfo> tables, string? searchFilter)
    {
        if (string.IsNullOrWhiteSpace(searchFilter))
            return tables.ToList();

        string filter = searchFilter.ToLowerInvariant();
        return tables.Where(table => Matches(table, filter)).ToList();
    }

    /// <summary>
    /// Selects the tables the filter shows. Tables the filter hides are left as they are, so selecting one schema
    /// after another accumulates.
    /// </summary>
    public static void SelectVisible(IEnumerable<TableInfo> tables, string? searchFilter)
    {
        foreach (var table in Visible(tables, searchFilter))
            table.IsSelected = true;
    }

    /// <summary>
    /// Clears the selection of every table, including the ones the filter hides: a hidden selected table would still be
    /// migrated, and emptied on the target, without the user seeing it.
    /// </summary>
    public static void DeselectAll(IEnumerable<TableInfo> tables)
    {
        foreach (var table in tables)
            table.IsSelected = false;
    }

    /// <summary>How many selected tables the filter currently hides.</summary>
    public static int CountHidden(IEnumerable<TableInfo> tables, string? searchFilter)
    {
        var all = tables.ToList();
        var visible = new HashSet<TableInfo>(Visible(all, searchFilter));
        return all.Count(table => table.IsSelected && !visible.Contains(table));
    }

    /// <summary>
    /// After a reload, selects the freshly loaded tables that are selected among the tables currently on screen.
    /// It takes the on-screen tables themselves, not a snapshot of their keys, so it always sees the selection as it
    /// is when it is called: a click made while the reload was running is kept instead of being undone.
    /// Tables are matched by exact (schema, name): names that differ only by case, or that contain a dot, are
    /// different tables.
    /// </summary>
    public static void CarryOver(IEnumerable<TableInfo> current, IEnumerable<TableInfo> reloaded)
    {
        var selected = new HashSet<(string Schema, string Table)>(
            current.Where(table => table.IsSelected).Select(table => (table.Schema, table.TableName)));

        foreach (var table in reloaded)
        {
            if (selected.Contains((table.Schema, table.TableName)))
                table.IsSelected = true;
        }
    }

    private static bool Matches(TableInfo table, string lowerCaseFilter) =>
        table.TableName.ToLowerInvariant().Contains(lowerCaseFilter) ||
        table.Schema.ToLowerInvariant().Contains(lowerCaseFilter) ||
        $"{table.Schema}.{table.TableName}".ToLowerInvariant().Contains(lowerCaseFilter);
}
