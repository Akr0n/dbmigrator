namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Stato di avanzamento riportato durante la generazione dello script.
/// </summary>
public class ScriptGenerationProgress
{
    /// <summary>Descrizione dell'attività corrente (es. "Tabella dbo.Clienti").</summary>
    public string CurrentObject { get; set; } = string.Empty;

    /// <summary>Indice (1-based) dell'oggetto in lavorazione.</summary>
    public int ProcessedObjects { get; set; }

    /// <summary>Numero totale di oggetti da elaborare.</summary>
    public int TotalObjects { get; set; }

    /// <summary>Righe di dati scritte finora (cumulativo).</summary>
    public long RowsWritten { get; set; }

    /// <summary>Percentuale complessiva 0..100.</summary>
    public int Percentage => TotalObjects > 0
        ? (int)System.Math.Min(100, (ProcessedObjects / (double)TotalObjects) * 100)
        : 0;
}
