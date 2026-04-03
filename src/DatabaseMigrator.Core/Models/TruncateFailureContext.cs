namespace DatabaseMigrator.Core.Models;

public sealed class TruncateFailureContext
{
    public string Schema { get; }
    public string TableName { get; }
    public string ErrorMessage { get; }

    public TruncateFailureContext(string schema, string tableName, string errorMessage)
    {
        Schema = schema;
        TableName = tableName;
        ErrorMessage = errorMessage;
    }
}

