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

    private static bool Matches(TableInfo table, string lowerCaseFilter) =>
        table.TableName.ToLowerInvariant().Contains(lowerCaseFilter) ||
        table.Schema.ToLowerInvariant().Contains(lowerCaseFilter) ||
        $"{table.Schema}.{table.TableName}".ToLowerInvariant().Contains(lowerCaseFilter);
}
