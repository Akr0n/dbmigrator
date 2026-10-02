using System;
using System.Collections.Generic;
using System.Linq;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// An enabled FOREIGN KEY whose delete rule changes the child table when a parent row is deleted:
/// <see cref="Cascades"/> true for ON DELETE CASCADE (child rows are deleted), false for SET NULL (child rows are updated).
/// </summary>
public sealed record DeleteRuleEdge(string ChildSchema, string ChildTable, string ParentSchema, string ParentTable, bool Cascades);

/// <summary>
/// Which other tables a plain DELETE of every row of a table removes or modifies through ON DELETE CASCADE / SET NULL keys.
/// Oracle has no TRUNCATE the migration can use, so it empties a table with DELETE, and an enabled key with one of these
/// rules reaches tables the user may not have selected.
/// </summary>
public static class DeleteCascadeReach
{
    /// <summary>
    /// The tables other than the root that are removed from or updated by deleting all rows of the root. Children reached
    /// by CASCADE lose their rows and so propagate to their own children; a SET NULL child only has a column nulled, which
    /// propagates nowhere. Self-references are ignored. Names are matched ignoring case.
    /// </summary>
    public static List<(string Schema, string Table)> Affected(string rootSchema, string rootTable, IEnumerable<DeleteRuleEdge> edges)
    {
        static (string, string) Key(string schema, string table) => (schema.ToUpperInvariant(), table.ToUpperInvariant());

        var children = edges
            .Where(edge => Key(edge.ChildSchema, edge.ChildTable) != Key(edge.ParentSchema, edge.ParentTable))
            .ToLookup(edge => Key(edge.ParentSchema, edge.ParentTable));

        var root = Key(rootSchema, rootTable);
        var affected = new Dictionary<(string, string), (string Schema, string Table)>();
        var deleted = new HashSet<(string, string)> { root };
        var pending = new Queue<(string, string)>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            foreach (var edge in children[pending.Dequeue()])
            {
                var child = Key(edge.ChildSchema, edge.ChildTable);
                if (child == root)
                    continue;

                affected.TryAdd(child, (edge.ChildSchema, edge.ChildTable));
                if (edge.Cascades && deleted.Add(child))
                    pending.Enqueue(child);
            }
        }

        return affected.Values.ToList();
    }
}
