using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Oracle reports a timestamp or an interval with its precision inside the type name (TIMESTAMP(6), TIMESTAMP(6) WITH TIME ZONE,
/// INTERVAL DAY(2) TO SECOND(6)). The cross-database mapping knows the bare names, so such columns used to fall through to its
/// text fallback: a timestamp column arrived on SQL Server or PostgreSQL as a string.
/// </summary>
public class SchemaMigrationOracleTypeNamesTests
{
    private static readonly SchemaMigrationService Service = new();

    private static string Ddl(DatabaseType target, string oracleType, int? dateTimePrecision = null)
        => Service.BuildCreateTableStatement(target, "dbo", "demo", new List<ColumnDefinition>
        {
            new() { Name = "c", DataType = oracleType, DateTimePrecision = dateTimePrecision, IsNullable = true, SourceDbType = DatabaseType.Oracle }
        });

    [Theory]
    [InlineData("TIMESTAMP(6)", null, "[c] datetime2(6)")]
    [InlineData("TIMESTAMP(3)", 3, "[c] datetime2(3)")]
    [InlineData("TIMESTAMP(9)", 9, "[c] datetime2(7)")]                  // SQL Server stops at 7
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", 6, "[c] datetimeoffset(6)")]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE", 6, "[c] datetimeoffset(6)")]
    public void OracleTimestamps_BecomeTemporalTypesOnSqlServer(string oracleType, int? precision, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.SqlServer, oracleType, precision));

    [Theory]
    [InlineData("TIMESTAMP(6)", 6, "\"c\" timestamp(6)")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", 6, "\"c\" timestamptz(6)")]
    public void OracleTimestamps_BecomeTemporalTypesOnPostgres(string oracleType, int? precision, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.PostgreSQL, oracleType, precision));

    [Theory]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)", DatabaseType.SqlServer, "[c] nvarchar(max)")]
    [InlineData("INTERVAL YEAR(2) TO MONTH", DatabaseType.SqlServer, "[c] nvarchar(max)")]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)", DatabaseType.PostgreSQL, "\"c\" text")]
    [InlineData("INTERVAL YEAR(2) TO MONTH", DatabaseType.PostgreSQL, "\"c\" text")]
    public void OracleIntervals_StayText(string oracleType, DatabaseType target, string expected)
    {
        // The data path writes the driver's value (a month count, a TimeSpan) as a bare number or text that an interval column
        // refuses: mapping them to a real interval type made such a table fail to load, where text loads. Left on the text
        // fallback, as they always were.
        Assert.Contains(expected, Ddl(target, oracleType));
    }

    [Theory]
    [InlineData("TIMESTAMP(9)", 9, "TIMESTAMP(9)")]
    [InlineData("TIMESTAMP(9)", null, "TIMESTAMP(9)")]                             // not "TIMESTAMP(6)": the precision is in the name
    [InlineData("TIMESTAMP(3) WITH TIME ZONE", 3, "TIMESTAMP(3) WITH TIME ZONE")]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)", null, "INTERVAL DAY(2) TO SECOND(6)")]
    public void OracleToOracle_KeepsTheExactType(string oracleType, int? precision, string expected)
    {
        // The precision is part of the type there: stripping it from the name (the cross-database step above) would lose it,
        // and these cases fail if that step is ever applied to a same-database move.
        Assert.Contains(expected, Ddl(DatabaseType.Oracle, oracleType, precision));
    }
}
