using System;

namespace DatabaseMigrator.Core.Models;

public enum LogLevel { Info, Warning, Error }

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";

    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

    public string LevelTag => Level switch
    {
        LogLevel.Warning => "WARN ",
        LogLevel.Error   => "ERROR",
        _                => "INFO "
    };
}
