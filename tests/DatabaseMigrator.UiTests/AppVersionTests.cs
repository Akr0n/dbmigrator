namespace DatabaseMigrator.UiTests;

/// <summary>
/// The release workflow stamps the exe with X.Y.Z plus the full commit sha; the window title and the log show the sha cut to seven
/// characters, so a debug.log sent from another PC says which build produced it.
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void TheCommitIsCutToSevenCharacters()
    {
        Assert.Equal("1.0.77+5bb2f8d", AppVersion.Format("1.0.77+5bb2f8d1234567890abcdef1234567890abcdef"));
    }

    [Theory]
    [InlineData("1.0.77", "1.0.77")]              // no commit in it: left alone
    [InlineData("1.0.78-rc.1", "1.0.78-rc.1")]    // no commit and longer than seven: must not be cut or throw
    [InlineData("10.20.300", "10.20.300")]
    [InlineData("1.0.77+abc", "1.0.77+abc")]      // already short
    [InlineData("1.0.77+1234567", "1.0.77+1234567")] // exactly seven
    [InlineData("1.0.77+12345678", "1.0.77+1234567")] // eight: the first one cut
    [InlineData("1.0.77+", "1.0.77+")]            // empty commit: the SDK never emits it; the formatter leaves it as is
    [InlineData("1.0.78-rc.1+deadbeefcafe", "1.0.78-rc.1+deadbee")]
    public void OtherShapes(string input, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingStamped_IsShownAsUnknown(string? input)
    {
        Assert.Equal("unknown", AppVersion.Format(input));
    }

    // Format never returns a blank string, so only "unknown" can fail here: the SDK always stamps an informational version
    // (at least 1.0.0 plus the commit), and "unknown" would mean the attribute is missing from the assembly.
    [Fact]
    public void TheRunningAssemblyHasAVersion()
    {
        Assert.NotEqual("unknown", AppVersion.Current);
    }
}
