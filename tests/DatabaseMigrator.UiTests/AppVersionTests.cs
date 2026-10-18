namespace DatabaseMigrator.UiTests;

/// <summary>
/// The window title shows the version only, without the commit id; the build stamps X.Y.Z
/// (from the release workflow, or from the latest git tag for local builds).
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void TheCommitIsDropped()
    {
        Assert.Equal("1.0.77", AppVersion.Format("1.0.77+5bb2f8d1234567890abcdef1234567890abcdef"));
    }

    [Theory]
    [InlineData("1.0.77", "1.0.77")]              // no commit in it: left alone
    [InlineData("1.0.78-rc.1", "1.0.78-rc.1")]    // prerelease: left alone
    [InlineData("10.20.300", "10.20.300")]
    [InlineData("1.0.77+abc", "1.0.77")]          // short commit: dropped too
    [InlineData("1.0.77+1234567", "1.0.77")]
    [InlineData("1.0.77+", "1.0.77")]            // empty commit: dropped
    [InlineData("1.0.78-rc.1+deadbeefcafe", "1.0.78-rc.1")]
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
    // (at least 1.0.0), and "unknown" would mean the attribute is missing from the assembly.
    [Fact]
    public void TheRunningAssemblyHasAVersion()
    {
        Assert.NotEqual("unknown", AppVersion.Current);
    }
}
