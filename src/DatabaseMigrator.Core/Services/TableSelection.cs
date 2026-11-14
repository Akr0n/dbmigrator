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
    /// Selects every table the filter shows, leaving other tables untouched: a second "select all" with a different
    /// filter must accumulate, so the user can select schema by schema without losing earlier choices.
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
    /// Tables are matched by exact (schema, name) first; names that differ only by case are treated as different
    /// tables unless there is exactly one selected candidate for that key (cross-DB compatibility).
    /// </summary>
    public static void CarryOver(IEnumerable<TableInfo> current, IEnumerable<TableInfo> reloaded)
    {
        // Set of exact (schema|name) keys that are selected in current.
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in current.Where(t => t.IsSelected))
            selectedKeys.Add($"{table.Schema}|{table.TableName}");

        // Build groups for case-insensitive fallback: lowercase key → list of selected original tables.
        var groups = new Dictionary<string, List<TableInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in current.Where(t => t.IsSelected))
        {
            var key = $"{table.Schema}|{table.TableName}".ToLowerInvariant();
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<TableInfo>();
                groups[key] = list;
            }

            list.Add(table);
        }

        // Pass 1: exact match (schema + case-sensitive name).
        foreach (var table in reloaded)
        {
            if (selectedKeys.Contains($"{table.Schema}|{table.TableName}"))
                table.IsSelected = true;
        }

        // Count how many tables in reloaded share each lowercase key.
        var reloadedKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in reloaded)
        {
            var key = $"{table.Schema}|{table.TableName}".ToLowerInvariant();
            if (!reloadedKeyCounts.TryGetValue(key, out var count))
                count = 0;

            reloadedKeyCounts[key] = count + 1;
        }

        // Pass 2: case-insensitive fallback only when reloaded has exactly one table per key
        // and there is exactly one selected candidate in current.
        foreach (var table in reloaded)
        {
            if (table.IsSelected) continue;

            var lowerKey = $"{table.Schema}|{table.TableName}".ToLowerInvariant();
            if (reloadedKeyCounts.TryGetValue(lowerKey, out var rCount) &&
                rCount == 1 &&
                groups.TryGetValue(lowerKey, out var candidates) &&
                candidates.Count == 1)
            {
                table.IsSelected = true;
            }
        }
    }

    private static bool Matches(TableInfo table, string lowerCaseFilter) =>
        table.TableName.ToLowerInvariant().Contains(lowerCaseFilter) ||
        table.Schema.ToLowerInvariant().Contains(lowerCaseFilter) ||
        $"{table.Schema}.{table.TableName}".ToLowerInvariant().Contains(lowerCaseFilter);
}
