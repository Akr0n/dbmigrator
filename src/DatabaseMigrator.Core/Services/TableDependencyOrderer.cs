using System;
using System.Collections.Generic;
using System.Linq;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>Outcome of <see cref="TableDependencyOrderer.Order"/>.</summary>
public sealed record TableOrderResult(IReadOnlyList<TableInfo> Tables, bool HadCycles);

/// <summary>
/// Orders tables so that every parent comes before the tables that reference it through a FOREIGN KEY.
/// </summary>
public static class TableDependencyOrderer
{
    /// <summary>
    /// Topological order of <paramref name="tables"/> by <paramref name="foreignKeys"/>. Tables that do not depend on
    /// each other keep their original relative order. Foreign keys that point outside <paramref name="tables"/> are
    /// ignored, and so are self-references. Names are matched ignoring case because the tables come from the source
    /// catalog while the keys come from the target catalog (PostgreSQL lower-cases, Oracle upper-cases).
    /// When keys form a cycle no strict order exists: the cycle is broken at the earliest table in the original order
    /// and <see cref="TableOrderResult.HadCycles"/> is set, so the caller can fall back to switching the keys off.
    /// </summary>
    public static TableOrderResult Order(IReadOnlyList<TableInfo> tables, IEnumerable<ForeignKeyInfo> foreignKeys)
    {
        int count = tables.Count;

        var indexByKey = new Dictionary<(string Schema, string Table), int>();
        for (int i = 0; i < count; i++)
            indexByKey.TryAdd(Key(tables[i].Schema, tables[i].TableName), i);

        var parentsLeft = new int[count];          // distinct selected parents not emitted yet
        var dependents = new List<int>?[count];    // tables waiting on this one
        var parents = new List<int>?[count];       // tables this one waits on
        var seen = new HashSet<(int Child, int Parent)>();
        foreach (var fk in foreignKeys)
        {
            if (!indexByKey.TryGetValue(Key(fk.ChildSchema, fk.ChildTable), out int child) ||
                !indexByKey.TryGetValue(Key(fk.ParentSchema, fk.ParentTable), out int parent))
                continue;
            if (child == parent || !seen.Add((child, parent)))
                continue;

            parentsLeft[child]++;
            (dependents[parent] ??= new List<int>()).Add(child);
            (parents[child] ??= new List<int>()).Add(parent);
        }

        // Always take the ready table that came first originally: unrelated tables are not shuffled.
        var ready = new SortedSet<int>(Enumerable.Range(0, count).Where(i => parentsLeft[i] == 0));
        var emitted = new bool[count];
        var ordered = new List<TableInfo>(count);
        bool hadCycles = false;

        while (ordered.Count < count)
        {
            int next;
            if (ready.Count > 0)
            {
                next = ready.Min;
                ready.Remove(next);
            }
            else
            {
                // Every table left is waiting on another one left, so some of them form a cycle. Break it at a table that
                // is itself on a cycle: one that merely depends on a cycle must still come after its parent.
                hadCycles = true;
                next = FirstTableOnCycle(emitted, parents);
            }

            emitted[next] = true;
            ordered.Add(tables[next]);

            if (dependents[next] == null)
                continue;
            foreach (int dependent in dependents[next]!)
            {
                if (!emitted[dependent] && --parentsLeft[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        return new TableOrderResult(ordered, hadCycles);
    }

    /// <summary>The earliest table not emitted yet that can reach itself by following parents that are not emitted yet.</summary>
    private static int FirstTableOnCycle(bool[] emitted, List<int>?[] parents)
    {
        for (int start = 0; start < emitted.Length; start++)
        {
            if (emitted[start])
                continue;

            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                foreach (int parent in parents[pending.Pop()] ?? new List<int>())
                {
                    if (emitted[parent])
                        continue;
                    if (parent == start)
                        return start;
                    if (visited.Add(parent))
                        pending.Push(parent);
                }
            }
        }

        // Unreachable: when nothing is ready every table left has an unemitted parent, so a cycle exists.
        return Array.IndexOf(emitted, false);
    }

    private static (string Schema, string Table) Key(string schema, string table) =>
        (schema.ToUpperInvariant(), table.ToUpperInvariant());
}
