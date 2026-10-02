using System.Collections.Generic;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Test puri sul DDL generato per le colonne di testo quando il target e' SQL Server.
///
/// PostgreSQL e Oracle (con database in UTF-8) conservano testo in qualunque alfabeto; una colonna <c>varchar</c> di
/// SQL Server invece lo converte nella tabella codici della collation, e i caratteri che non vi rientrano (cirillico,
/// greco, cinese, ...) diventano '?' senza alcun errore. I tipi di testo vanno quindi mappati sui tipi Unicode.
/// </summary>
public class SchemaMigrationTextMappingTests
{
    private static readonly SchemaMigrationService Service = new();

    private static string Ddl(DatabaseType source, DatabaseType target, string dataType, int? maxLength = null)
        => Service.BuildCreateTableStatement(target, "dbo", "demo", new List<ColumnDefinition>
        {
            new() { Name = "c", DataType = dataType, MaxLength = maxLength, IsNullable = true, SourceDbType = source }
        });

    // ── PostgreSQL -> SQL Server ─────────────────────────────────────────────────

    [Theory]
    [InlineData("varchar", 50, "[c] nvarchar(50)")]
    [InlineData("character varying", 50, "[c] nvarchar(50)")]
    [InlineData("varchar", null, "[c] nvarchar(max)")]
    [InlineData("text", null, "[c] nvarchar(max)")]
    [InlineData("char", 3, "[c] nvarchar(3)")]          // a fixed-width nchar would double the row size: see below
    [InlineData("character", 3, "[c] nvarchar(3)")]
    [InlineData("citext", null, "[c] nvarchar(max)")]   // tipo senza equivalente diretto: il ripiego e' Unicode
    public void PostgresText_BecomesUnicodeOnSqlServer(string pgType, int? length, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.PostgreSQL, DatabaseType.SqlServer, pgType, length));

    [Theory]
    [InlineData("varchar", 4000, "nvarchar(4000)")]
    [InlineData("varchar", 4001, "nvarchar(max)")]      // nvarchar(n) arriva a 4000: oltre, solo max
    [InlineData("varchar", 10000, "nvarchar(max)")]
    [InlineData("char", 4000, "nvarchar(4000)")]
    [InlineData("char", 4001, "nvarchar(max)")]
    public void PostgresText_LongerThanFourThousand_FallsBackToMax(string pgType, int length, string expected)
        => Assert.Contains($"[c] {expected}", Ddl(DatabaseType.PostgreSQL, DatabaseType.SqlServer, pgType, length));

    // ── Oracle -> SQL Server ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("varchar2", 100, "[c] nvarchar(100)")]
    [InlineData("varchar2", 4000, "[c] nvarchar(4000)")]
    [InlineData("varchar2", 32767, "[c] nvarchar(max)")]  // VARCHAR2 esteso
    [InlineData("char", 10, "[c] nvarchar(10)")]
    [InlineData("clob", null, "[c] nvarchar(max)")]
    [InlineData("long", null, "[c] nvarchar(max)")]
    [InlineData("nvarchar2", 100, "[c] nvarchar(100)")]
    [InlineData("nchar", 10, "[c] nvarchar(10)")]
    [InlineData("nclob", null, "[c] nvarchar(max)")]
    [InlineData("json", null, "[c] nvarchar(max)")]      // Oracle 21c+: no case of its own, and the fallback must stay Unicode
    [InlineData("somethingnew", null, "[c] nvarchar(max)")]
    public void OracleText_BecomesUnicodeOnSqlServer(string oracleType, int? length, string expected)
        => Assert.Contains(expected, Ddl(DatabaseType.Oracle, DatabaseType.SqlServer, oracleType, length));

    [Fact]
    public void ManyFixedWidthColumns_StillFitInARow()
    {
        // SQL Server cannot push a fixed-width column off the row: 12 x nchar(400) = 9600 bytes is over its 8060-byte limit and
        // the table could not be created, where 12 x char(400) used to fit. Variable-width types are only as long as their data.
        var columns = Enumerable.Range(0, 12).Select(i => new ColumnDefinition
        {
            Name = $"c{i}", DataType = "CHAR", MaxLength = 400, IsNullable = true, SourceDbType = DatabaseType.Oracle
        }).ToList();

        string ddl = Service.BuildCreateTableStatement(DatabaseType.SqlServer, "dbo", "demo", columns);

        Assert.DoesNotContain("nchar(", ddl);
        Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(ddl, @"nvarchar\(400\)").Count);
    }

    // ── i casi che NON cambiano ──────────────────────────────────────────────────

    [Fact]
    public void SqlServerToSqlServer_KeepsVarchar()
        => Assert.Contains("[c] varchar(50)", Ddl(DatabaseType.SqlServer, DatabaseType.SqlServer, "varchar", 50));

    [Fact]
    public void SqlServerToPostgres_KeepsItsMapping()
        => Assert.Contains("\"c\" varchar(50)", Ddl(DatabaseType.SqlServer, DatabaseType.PostgreSQL, "varchar", 50));

    [Fact]
    public void PostgresToOracle_IsUnchanged()
        => Assert.DoesNotContain("NVARCHAR", Ddl(DatabaseType.PostgreSQL, DatabaseType.Oracle, "varchar", 50));
}
