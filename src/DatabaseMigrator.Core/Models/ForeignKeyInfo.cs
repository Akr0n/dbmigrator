namespace DatabaseMigrator.Core.Models;

/// <summary>
/// A FOREIGN KEY constraint as read from a database catalog: the child table holds the referencing columns,
/// the parent table is the one it points at. <see cref="IsDisabled"/> is the state at read time.
/// </summary>
public sealed record ForeignKeyInfo(
    string ConstraintName,
    string ChildSchema,
    string ChildTable,
    string ParentSchema,
    string ParentTable,
    bool IsDisabled);
