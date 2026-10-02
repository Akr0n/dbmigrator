using System.Collections.Generic;
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
    [InlineData("INTERVAL DAY(2) TO SECOND(6)", null, "[c] varchar(50)")]
    [InlineData("INTERVAL YEAR(2) TO MONTH", null, "[c] varchar(50)")]
    public void OracleTemporalTypes_BecomeTemporalTypesOnSqlServer(string oracleType, int? precision, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.SqlServer, oracleType, precision));

    [Theory]
    [InlineData("TIMESTAMP(6)", 6, "\"c\" timestamp(6)")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", 6, "\"c\" timestamptz(6)")]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)", null, "\"c\" interval")]
    public void OracleTemporalTypes_BecomeTemporalTypesOnPostgres(string oracleType, int? precision, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.PostgreSQL, oracleType, precision));

    [Fact]
    public void OracleToOracle_KeepsTheExactType()
    {
        // The precision is part of the type there: it must not be stripped.
        Assert.Contains("TIMESTAMP(9)", Ddl(DatabaseType.Oracle, "TIMESTAMP(9)", 9));
    }
}
