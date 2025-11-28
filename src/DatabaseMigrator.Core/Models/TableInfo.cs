namespace DatabaseMigrator.Core.Models;
public class TableInfo
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public bool IsSelected { get; set; }
}
