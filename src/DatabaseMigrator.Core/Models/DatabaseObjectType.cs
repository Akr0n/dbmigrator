namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Tipi di oggetto di database che la funzione "Genera Script" è in grado di esportare come DDL.
/// </summary>
public enum DatabaseObjectType
{
    Table,
    View,
    StoredProcedure,
    Function,
    Trigger,
    Sequence,
    Index
}
