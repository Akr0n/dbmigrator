namespace DatabaseMigrator.UiTests;

/// <summary>
/// The release workflow stamps the exe with X.Y.Z+<full commit sha>; the window title and the log show the sha cut to seven
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
    [InlineData("1.0.77+abc", "1.0.77+abc")]      // already short
    [InlineData("1.0.77+1234567", "1.0.77+1234567")] // exactly seven
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

    [Fact]
    public void TheRunningAssemblyHasAVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
    }
}
