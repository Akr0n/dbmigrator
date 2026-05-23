namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Opzioni che controllano la generazione dello script .sql.
/// </summary>
public class ScriptGenerationOptions
{
    /// <summary>Dialetto SQL in cui produrre lo script (SQL Server, PostgreSQL o Oracle).</summary>
    public DatabaseType TargetDialect { get; set; } = DatabaseType.SqlServer;

    /// <summary>Includere il DDL (CREATE) degli oggetti selezionati.</summary>
    public bool IncludeSchema { get; set; } = true;

    /// <summary>Includere gli statement INSERT con i dati delle tabelle selezionate.</summary>
    public bool IncludeData { get; set; } = true;

    /// <summary>Anteporre statement DROP agli oggetti, per uno script ri-eseguibile.</summary>
    public bool IncludeDropStatements { get; set; }

    /// <summary>Numero massimo di righe per ogni statement INSERT (1..1000).</summary>
    public int RowsPerInsertBatch { get; set; } = 200;
}
