using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Tests;

public class RuntimeOptionsProviderTests
{
    [Fact]
    public void RuntimeOptions_AreNormalizedToSafeRanges()
    {
        var options = RuntimeOptionsProvider.Current;

        Assert.NotNull(options);
        Assert.True(options.Database.BatchSize >= 1);
        Assert.True(options.Database.CommandTimeoutSeconds >= 30);
        Assert.InRange(options.Database.RowCountMaxConcurrency, 1, 64);
        Assert.InRange(options.Database.RetryCount, 0, 10);
        Assert.InRange(options.Database.RetryInitialDelayMilliseconds, 50, 10_000);

        Assert.InRange(options.Logging.MaxFileSizeMb, 1, 500);
        Assert.InRange(options.Logging.RetentionDays, 1, 365);
        Assert.InRange(options.Logging.MaxArchivedFiles, 1, 200);
    }
}
