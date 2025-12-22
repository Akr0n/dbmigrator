namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Specifies what should be migrated from source to target database.
/// </summary>
public enum MigrationMode
{
    /// <summary>
    /// Migrate both schema (DDL) and data.
    /// If the table already exists in target, only data will be migrated.
    /// </summary>
    SchemaAndData = 0,

    /// <summary>
    /// Migrate only the schema (DDL) without any data.
    /// Useful for creating empty tables in the target database.
    /// </summary>
    SchemaOnly = 1,

    /// <summary>
    /// Migrate only the data.
    /// The table must already exist in the target database.
    /// </summary>
    DataOnly = 2
}
