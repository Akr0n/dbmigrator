using System.Text;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Object names come from the source database catalog and end up inside SQL that the tool runs or writes into a script.
/// A name containing a quote must stay inside its literal or identifier, whatever the dialect.
/// Each test removes the quoted parts and checks that nothing the name carried is left outside as code.
/// </summary>
public class ScriptEscapingTests
{
    private const string Payload = "x'); DROP TABLE victim; --";

    /// <summary>
    /// What is left of the SQL once every quoted part (single-quoted literal, double-quoted identifier, doubled quotes
    /// inside them) is removed, scanning left to right as a SQL parser would: the text that would run as code.
    /// </summary>
    private static string CodeOutsideQuotes(string sql)
    {
        var code = new StringBuilder();
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (c != '\'' && c != '"')
            {
                code.Append(c);
                continue;
            }

            int j = i + 1;
            while (j < sql.Length)
            {
                if (sql[j] == c)
                {
                    if (j + 1 < sql.Length && sql[j + 1] == c) { j += 2; continue; } // a doubled quote stays inside
                    break;
                }
                j++;
            }

            i = j; // the closing quote
            code.Append(c).Append(c);
        }

        return code.ToString();
    }

    // ── PostgreSQL identity sequence resync ────────────────────────────────────────────────────────

    [Fact]
    public void IdentityResync_APayloadInTheTableName_StaysInsideItsLiteral()
    {
        string sql = new DatabaseService().BuildIdentitySequenceResyncStatement(DatabaseType.PostgreSQL, "public", Payload, "id")!;

        Assert.DoesNotContain("DROP", CodeOutsideQuotes(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdentityResync_APayloadInTheColumnName_StaysInsideItsLiteral()
    {
        string sql = new DatabaseService().BuildIdentitySequenceResyncStatement(DatabaseType.PostgreSQL, "public", "orders", Payload)!;

        Assert.DoesNotContain("DROP", CodeOutsideQuotes(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdentityResync_OrdinaryNames_AreUnchanged()
    {
        string sql = new DatabaseService().BuildIdentitySequenceResyncStatement(DatabaseType.PostgreSQL, "public", "orders", "id")!;

        Assert.Equal(
            "SELECT setval(pg_get_serial_sequence('\"public\".\"orders\"', 'id'), (SELECT MAX(\"id\") FROM \"public\".\"orders\"));", sql);
    }

    // ── SQL Server CREATE SCHEMA in a generated script ─────────────────────────────────────────────

    [Fact]
    public void CreateSchemaForSqlServer_APayloadInTheSchemaName_StaysInsideBothLiterals()
    {
        string sql = ScriptGenerationService.BuildCreateSchema(DatabaseType.SqlServer, Payload);

        Assert.DoesNotContain("DROP", CodeOutsideQuotes(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSchemaForSqlServer_OrdinaryName_IsUnchanged() =>
        Assert.Equal("IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA [sales]')",
            ScriptGenerationService.BuildCreateSchema(DatabaseType.SqlServer, "sales"));

    // ── Oracle DROP CONSTRAINT block in a generated script ─────────────────────────────────────────

    [Fact]
    public void OracleDropConstraint_APayloadInTheConstraintName_StaysInsideTheLiteral()
    {
        string block = ScriptGenerationService.BuildOracleDropConstraintBlock("APP.ORDERS", $"\"{Payload}\"");

        Assert.DoesNotContain("DROP TABLE victim", CodeOutsideQuotes(block), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleDropConstraint_APayloadInTheTableName_StaysInsideTheLiteral()
    {
        string block = ScriptGenerationService.BuildOracleDropConstraintBlock($"APP.{Payload}", "\"FK_1\"");

        Assert.DoesNotContain("DROP TABLE victim", CodeOutsideQuotes(block), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleDropConstraint_OrdinaryNames_AreUnchanged() =>
        Assert.Equal(
            "BEGIN" + Environment.NewLine +
            "  EXECUTE IMMEDIATE 'ALTER TABLE APP.ORDERS DROP CONSTRAINT \"FK_1\"';" + Environment.NewLine +
            "EXCEPTION WHEN OTHERS THEN" + Environment.NewLine +
            "  IF SQLCODE NOT IN (-2443, -2431, -942) THEN RAISE; END IF;" + Environment.NewLine +
            "END;",
            ScriptGenerationService.BuildOracleDropConstraintBlock("APP.ORDERS", "\"FK_1\""));

    // ── PostgreSQL view in a generated script ──────────────────────────────────────────────────────

    [Fact]
    public void CreateViewForPostgreSql_APayloadInTheViewName_StaysInsideItsIdentifier()
    {
        string header = ScriptGenerationService.BuildPostgresViewHeader("sales", "v\" AS SELECT 1; DROP TABLE victim; --");

        Assert.DoesNotContain("DROP", CodeOutsideQuotes(header), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateViewForPostgreSql_APayloadInTheSchemaName_StaysInsideItsIdentifier()
    {
        string header = ScriptGenerationService.BuildPostgresViewHeader("s\"; DROP TABLE victim; --", "v");

        Assert.DoesNotContain("DROP", CodeOutsideQuotes(header), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateViewForPostgreSql_OrdinaryNames_KeepTheirCase() =>
        Assert.Equal("CREATE OR REPLACE VIEW \"Sales\".\"OrderTotals\"", ScriptGenerationService.BuildPostgresViewHeader("Sales", "OrderTotals"));

    // ── Names written into "--" comments ───────────────────────────────────────────────────────────

    // Built from code points: the source cannot carry U+0085, U+2028 or U+2029 as literal characters.
    public static IEnumerable<object[]> LineBreaks => new[] { 0x000A, 0x000D, 0x0085, 0x2028, 0x2029 }
        .Select(code => new object[] { (char)code });

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void SingleLine_RemovesEveryLineBreakSoACommentCannotEnd(char lineBreak)
    {
        string text = ScriptGenerationService.SingleLine($"a{lineBreak}DROP TABLE t;{lineBreak}--");

        Assert.DoesNotContain(lineBreak, text);
    }

    [Fact]
    public void SingleLine_LeavesAnOrdinaryNameAlone() => Assert.Equal("dbo.Orders", ScriptGenerationService.SingleLine("dbo.Orders"));
}
