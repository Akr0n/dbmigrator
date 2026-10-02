using System.Text;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// SQL*Plus refuses a line of more than 4999 characters ("SP2-0027: Input is too long ... line ignored") and goes on, so a
/// script row written on one long line is silently not loaded while the script still ends with exit code 0 (and a line over
/// 14999 characters is cut, its tail read as a new input line). A row with multi-line text is one such line: its line breaks
/// are written as CHR(10), not as real line breaks inside the literal. Long values and wide rows are therefore written over
/// several physical lines, always breaking between pieces (after a "||"), never inside a quoted text.
/// </summary>
public class OracleScriptLineLengthTests
{
    // Far under the 4999 SQL*Plus accepts: one physical line is at most one budget of pieces plus one more piece.
    private const int MaxLine = 2600;

    private static string Literal(string value) =>
        new DatabaseService().FormatSqlValue(DatabaseType.Oracle, value, unicodeStringLiterals: true, oracleLineBreaksAsChr: true);

    private static int LongestLine(string text) => text.Split('\n').Max(line => line.TrimEnd('\r').Length);

    /// <summary>Reads a literal back the way Oracle does: pieces joined by ||, '' for a quote, CHR(10)/CHR(13), line breaks between pieces ignored.</summary>
    private static string Decode(string literal)
    {
        var result = new StringBuilder();
        int i = 0;
        while (i < literal.Length)
        {
            char c = literal[i];
            if (c == '\'')
            {
                i++;
                while (true)
                {
                    if (literal[i] == '\'')
                    {
                        if (i + 1 < literal.Length && literal[i + 1] == '\'') { result.Append('\''); i += 2; continue; }
                        i++;
                        break;
                    }
                    result.Append(literal[i++]);
                }
            }
            else if (string.CompareOrdinal(literal, i, "CHR(13)", 0, 7) == 0) { result.Append('\r'); i += 7; }
            else if (string.CompareOrdinal(literal, i, "CHR(10)", 0, 7) == 0) { result.Append('\n'); i += 7; }
            else if (string.CompareOrdinal(literal, i, "||", 0, 2) == 0) i += 2;
            else if (c == '\r' || c == '\n') i++; // a physical line break between two pieces
            else throw new FormatException($"Unexpected '{c}' at {i} in: {literal[..Math.Min(literal.Length, 120)]}");
        }
        return result.ToString();
    }

    [Fact]
    public void ALongMultiLineValue_IsWrittenOverSeveralShortLines_AndMeansTheSameText()
    {
        // 3000 characters in 150 lines of 19: the one-line form is over 5000 characters.
        string value = string.Join("\n", Enumerable.Range(0, 150).Select(i => $"line number {i:000} ok"));

        string literal = Literal(value);

        Assert.True(LongestLine(literal) < MaxLine, $"longest line was {LongestLine(literal)}");
        Assert.Equal(value, Decode(literal));
    }

    [Fact]
    public void ALongValueWithoutLineBreaks_IsWrappedToo()
    {
        string value = new string('x', 4000);

        string literal = Literal(value);

        Assert.True(LongestLine(literal) < MaxLine, $"longest line was {LongestLine(literal)}");
        Assert.Equal(value, Decode(literal));
    }

    [Fact]
    public void AShortValue_IsUnchanged()
    {
        Assert.Equal("'hello world'", Literal("hello world"));
        Assert.Equal("'a'||CHR(10)||'b'", Literal("a\nb"));
    }

    [Fact]
    public void ManyQuotes_AreStillDoubledAndNeverSplitInTheMiddle()
    {
        string value = new string('\'', 3000); // each one is written as '' : a split between them would end the literal early

        string literal = Literal(value);

        Assert.True(LongestLine(literal) < MaxLine, $"longest line was {LongestLine(literal)}");
        Assert.Equal(value, Decode(literal));
    }

    [Fact]
    public void ASurrogatePairAtAPieceBoundary_IsNotSplit()
    {
        // A character outside the BMP is two UTF-16 units: whatever the piece size is, it must not be cut between them.
        string emoji = char.ConvertFromUtf32(0x1F600);
        foreach (int prefix in Enumerable.Range(490, 40))
        {
            string value = new string('a', prefix) + emoji + new string('b', 1200) + emoji;

            string literal = Literal(value);

            Assert.Equal(value, Decode(literal));
            Assert.DoesNotContain("\uD83D'", literal); // a piece ending in a lone high surrogate
        }
    }

    [Fact]
    public void ARowOfManyShortValues_IsWrappedBetweenValues()
    {
        var values = Enumerable.Range(0, 400).Select(i => $"'value {i}'").ToList();

        string row = ScriptGenerationService.JoinForSqlPlus(values);

        Assert.True(LongestLine(row) < MaxLine, $"longest line was {LongestLine(row)}");
        // Same values in the same order: only the white space after a comma differs.
        Assert.Equal(string.Join(",", values), string.Concat(row.Split('\n')).Replace("\r", "").Replace(", ", ",").Replace(",  ", ","));
    }

    [Fact]
    public void ALongValueAfterOthers_StartsOnANewLine_SoItsFirstLineStaysShort()
    {
        var values = new List<string> { "1", "'some text'", Literal(string.Join("\n", Enumerable.Range(0, 300).Select(i => $"line {i}"))) };

        string row = ScriptGenerationService.JoinForSqlPlus(values);

        Assert.True(LongestLine(row) < MaxLine, $"longest line was {LongestLine(row)}");
    }
}
