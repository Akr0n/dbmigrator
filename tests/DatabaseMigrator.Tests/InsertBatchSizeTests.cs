using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// A real migration stopped on a table of 199 columns: 1000 rows in one INSERT ... VALUES are 199,000 values, and SQL Server gave up
/// after 44 seconds ("The query processor ran out of internal resources and could not produce a query plan", error 8623) without
/// loading a single row. How many rows one INSERT may hold has to depend on how many columns a row has.
/// </summary>
public class InsertBatchSizeTests
{
    [Theory]
    [InlineData(10, 1000, 1000)]    // a narrow table keeps the full batch
    [InlineData(30, 1000, 1000)]    // 30 x 1000 = 30,000 values: exactly the limit
    [InlineData(199, 1000, 150)]    // the table that failed: 150 rows of 199 values
    [InlineData(199, 100, 100)]     // a batch already smaller than the limit is left alone
    [InlineData(2000, 1000, 15)]
    [InlineData(40_000, 1000, 1)]   // never less than one row
    [InlineData(0, 1000, 1000)]     // no columns known: nothing to divide by
    public void RowsPerInsertStatement_KeepsTheValuesOfOneStatementUnderTheLimit(int columns, int batchSize, int expected)
    {
        Assert.Equal(expected, DatabaseService.RowsPerInsertStatement(columns, batchSize));
    }
}
