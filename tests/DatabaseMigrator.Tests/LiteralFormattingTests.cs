using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// How values and names are written into SQL: the INSERTs a live migration runs, and the script the "Genera Script" tab writes.
/// </summary>
public class LiteralFormattingTests
{
    // Hiragana A, Cyrillic Zhe, Greek Omega: outside the code page of a default SQL Server database.
    private static readonly string NonLatin = new(new[] { (char)0x3042, (char)0x0416, (char)0x03A9 });

    private static string Insert(DatabaseType target, object? value) =>
        new DatabaseService().BuildInsertQuery(target, "dbo", "t", ["v"], [[value]]);

    // ── live migration: SQL Server turned every non-Latin character into '?' ──────────────────────

    [Fact]
    public void SqlServerInsert_WritesStringsAsUnicodeLiterals_SoNonLatinTextSurvives() =>
        Assert.Contains($"N'{NonLatin}'", Insert(DatabaseType.SqlServer, NonLatin));

    [Fact]
    public void SqlServerInsert_StillDoublesQuotes() =>
        Assert.Contains("N'it''s'", Insert(DatabaseType.SqlServer, "it's"));

    [Fact]
    public void SqlServerInsert_LeavesNumbersAndNullsAlone()
    {
        Assert.EndsWith("VALUES (5)", Insert(DatabaseType.SqlServer, 5));
        Assert.EndsWith("VALUES (NULL)", Insert(DatabaseType.SqlServer, null));
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.Oracle)]
    public void OtherTargets_DoNotGetTheNPrefix(DatabaseType target)
    {
        string sql = Insert(target, NonLatin);

        Assert.Contains($"'{NonLatin}'", sql);
        Assert.DoesNotContain("N'", sql);
    }

    // ── scripts for Oracle: SQL*Plus ends a statement at a "/" line and, by default, at a blank line ──

    private static string OracleScriptLiteral(string value) =>
        new DatabaseService().FormatSqlValue(DatabaseType.Oracle, value, unicodeStringLiterals: true, oracleLineBreaksAsChr: true);

    [Theory]
    [InlineData("a\nb", "'a'||CHR(10)||'b'")]
    [InlineData("a\r\nb", "'a'||CHR(13)||CHR(10)||'b'")]
    [InlineData("a\n\nb", "'a'||CHR(10)||CHR(10)||'b'")]
    [InlineData("a\n/\nb", "'a'||CHR(10)||'/'||CHR(10)||'b'")]
    [InlineData("\nx", "CHR(10)||'x'")]
    [InlineData("it's\n", "'it''s'||CHR(10)")]
    public void OracleScriptLiteral_WritesLineBreaksAsChr_SoNoLineOfTheScriptIsBlankOrASlash(string value, string expected) =>
        Assert.Equal(expected, OracleScriptLiteral(value));

    [Fact]
    public void OracleScriptLiteral_WithoutLineBreaks_IsAnOrdinaryLiteral() => Assert.Equal("'plain text'", OracleScriptLiteral("plain text"));

    [Fact]
    public void OracleLiteral_KeepsRawLineBreaksUnlessAskedOtherwise() =>
        Assert.Equal("'a\nb'", new DatabaseService().FormatSqlValue(DatabaseType.Oracle, "a\nb")); // the live path is unchanged

    [Theory]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.PostgreSQL)]
    public void LineBreaksInOtherDialects_AreLeftAlone(DatabaseType dialect)
    {
        string literal = new DatabaseService().FormatSqlValue(dialect, "a\nb", unicodeStringLiterals: true, oracleLineBreaksAsChr: true);

        Assert.Contains("a\nb", literal);
        Assert.DoesNotContain("CHR(", literal);
    }

    // ── script header ─────────────────────────────────────────────────────────────────────────────

    private static async Task<string> HeaderAsync(DatabaseType dialect, string database = "db")
    {
        var writer = new StringWriter();
        await ScriptGenerationService.WriteHeaderAsync(writer,
            new ConnectionInfo { DatabaseType = DatabaseType.SqlServer, Server = "srv", Database = database },
            new ScriptGenerationOptions { TargetDialect = dialect }, objectCount: 1);
        return writer.ToString();
    }

    [Fact]
    public async Task OracleScripts_StartByTurningOffSubstitutionAndBlankLineHandling()
    {
        string header = await HeaderAsync(DatabaseType.Oracle);

        Assert.Contains("SET DEFINE OFF", header);        // otherwise "R&D" prompts for a value and splices script lines in
        Assert.Contains("SET SQLBLANKLINES ON", header);  // otherwise a blank line inside a statement ends it
    }

    [Theory]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.PostgreSQL)]
    public async Task OtherScripts_DoNotGetSqlPlusCommands(DatabaseType dialect)
    {
        string header = await HeaderAsync(dialect);

        Assert.DoesNotContain("SET DEFINE", header);
        Assert.DoesNotContain("SQLBLANKLINES", header);
    }

    [Fact]
    public async Task TheDatabaseNameInTheHeader_CannotEndTheCommentLine()
    {
        // A name from a loaded configuration file: the second line would run as a psql / sqlcmd meta-command.
        string header = await HeaderAsync(DatabaseType.PostgreSQL, database: "x\n\\! calc\n:!! calc");

        Assert.All(header.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0),
            line => Assert.StartsWith("--", line));
    }

    // ── constraint names in an Oracle script ──────────────────────────────────────────────────────

    [Fact]
    public void OracleConstraintName_WithLineBreaks_StaysOnOneLine()
    {
        string name = ScriptGenerationService.FormatConstraintName(DatabaseType.Oracle, "pk\n/\nDROP TABLE victim;\n--");

        Assert.DoesNotContain('\n', name);
        Assert.DoesNotContain('\r', name);
    }

    [Fact]
    public void OracleConstraintName_OrdinaryName_IsQuotedAndUpperCased() =>
        Assert.Equal("\"FK_ORDERS\"", ScriptGenerationService.FormatConstraintName(DatabaseType.Oracle, "fk_orders"));
}
