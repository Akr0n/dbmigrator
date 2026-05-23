using ReactiveUI;

namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Rappresenta un singolo oggetto di database selezionabile per la generazione dello script
/// (tabella, vista, stored procedure, funzione, trigger, sequenza o indice).
/// </summary>
public class DatabaseObject : ReactiveObject
{
    private DatabaseObjectType _objectType;
    private string _schema = string.Empty;
    private string _name = string.Empty;
    private string _parentName = string.Empty;
    private long _rowCount;
    private bool _isSelected;

    public DatabaseObjectType ObjectType
    {
        get => _objectType;
        set => this.RaiseAndSetIfChanged(ref _objectType, value);
    }

    public string Schema
    {
        get => _schema;
        set => this.RaiseAndSetIfChanged(ref _schema, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>
    /// Nome dell'oggetto contenitore quando applicabile (tabella di appartenenza per indici e trigger).
    /// Vuoto per gli oggetti che non hanno un contenitore.
    /// </summary>
    public string ParentName
    {
        get => _parentName;
        set => this.RaiseAndSetIfChanged(ref _parentName, value);
    }

    /// <summary>Numero di righe; valorizzato solo per le tabelle, 0 per gli altri oggetti.</summary>
    public long RowCount
    {
        get => _rowCount;
        set => this.RaiseAndSetIfChanged(ref _rowCount, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>Nome qualificato schema.nome per visualizzazione e log.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";

    /// <summary>Vero solo per le tabelle: indica se mostrare il conteggio righe nell'interfaccia.</summary>
    public bool ShowRowCount => ObjectType == DatabaseObjectType.Table;

    /// <summary>Etichetta in italiano del tipo di oggetto, per l'interfaccia.</summary>
    public string DisplayType => ObjectType switch
    {
        DatabaseObjectType.Table => "Tabella",
        DatabaseObjectType.View => "Vista",
        DatabaseObjectType.StoredProcedure => "Stored Procedure",
        DatabaseObjectType.Function => "Funzione",
        DatabaseObjectType.Trigger => "Trigger",
        DatabaseObjectType.Sequence => "Sequenza",
        DatabaseObjectType.Index => "Indice",
        _ => ObjectType.ToString()
    };
}
