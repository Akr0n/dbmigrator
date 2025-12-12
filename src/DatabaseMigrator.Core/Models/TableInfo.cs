using ReactiveUI;

namespace DatabaseMigrator.Core.Models;

public class TableInfo : ReactiveObject
{
    private string _tableName = string.Empty;
    private string _schema = string.Empty;
    private long _rowCount;
    private bool _isSelected;

    public string TableName
    {
        get => _tableName;
        set => this.RaiseAndSetIfChanged(ref _tableName, value);
    }

    public string Schema
    {
        get => _schema;
        set => this.RaiseAndSetIfChanged(ref _schema, value);
    }

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
}
