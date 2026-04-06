using System;
using System.IO;
using System.Linq;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Core.Services;

/// <summary>
/// Centralized logging service for the entire application.
/// Provides consistent logging across all components.
/// </summary>
public static class LoggerService
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatabaseMigrator");

    private static readonly object LockObject = new object();
    private static LoggingRuntimeOptions LoggingOptions => RuntimeOptionsProvider.Current.Logging;

    private static string LogPath => Path.Combine(LogDirectory, "debug.log");
    private static string ErrorLogPath => Path.Combine(LogDirectory, "error.log");
    private static long MaxFileSizeBytes => LoggingOptions.MaxFileSizeMb * 1024L * 1024L;

    /// <summary>
    /// Raised after each log write. Subscribers must handle cross-thread dispatch themselves.
    /// </summary>
    public static event Action<LogEntry>? MessageLogged;

    /// <summary>
    /// Logs a debug message to the debug log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message)
    {
        try
        {
            EnsureLogDirectoryExists();

            var now = DateTime.Now;
            var fullMessage = $"{now:yyyy-MM-dd HH:mm:ss.fff} - {message}\n";

            lock (LockObject)
            {
                RotateIfNeeded(LogPath);
                File.AppendAllText(LogPath, fullMessage);
            }

            System.Diagnostics.Debug.WriteLine(fullMessage.TrimEnd());

            try { MessageLogged?.Invoke(new LogEntry { Timestamp = now, Level = LogLevel.Info, Message = message }); }
            catch { /* Never let UI subscribers crash the logging pipeline */ }
        }
        catch
        {
            // Silently ignore logging errors to prevent application crashes
        }
    }

    /// <summary>
    /// Logs an error message to the error log file.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    /// <param name="exception">Optional exception to include in the log.</param>
    public static void LogError(string message, Exception? exception = null)
    {
        try
        {
            EnsureLogDirectoryExists();

            var now = DateTime.Now;
            var fullMessage = $"{now:yyyy-MM-dd HH:mm:ss.fff} - ERROR: {message}";
            if (exception != null)
            {
                fullMessage += $"\nException: {exception.GetType().Name}: {exception.Message}";
                fullMessage += $"\nStack Trace: {exception.StackTrace}";
                if (exception.InnerException != null)
                {
                    fullMessage += $"\nInner Exception: {exception.InnerException.Message}";
                }
            }
            fullMessage += "\n\n";

            lock (LockObject)
            {
                RotateIfNeeded(ErrorLogPath);
                RotateIfNeeded(LogPath);
                File.AppendAllText(ErrorLogPath, fullMessage);
                File.AppendAllText(LogPath, fullMessage);
            }

            System.Diagnostics.Debug.WriteLine(fullMessage.TrimEnd());

            // Compact message for UI (no stack trace)
            var uiMessage = exception != null
                ? $"{message} — {exception.GetType().Name}: {exception.Message}"
                : message;
            try { MessageLogged?.Invoke(new LogEntry { Timestamp = now, Level = LogLevel.Error, Message = uiMessage }); }
            catch { /* Never let UI subscribers crash the logging pipeline */ }
        }
        catch
        {
            // Silently ignore logging errors to prevent application crashes
        }
    }

    /// <summary>
    /// Gets the path to the log directory.
    /// </summary>
    public static string GetLogDirectory() => LogDirectory;

    /// <summary>
    /// Gets the path to the debug log file.
    /// </summary>
    public static string GetLogPath() => LogPath;

    /// <summary>
    /// Gets the path to the error log file.
    /// </summary>
    public static string GetErrorLogPath() => ErrorLogPath;

    private static void RotateIfNeeded(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var info = new FileInfo(filePath);
            if (info.Length < MaxFileSizeBytes)
            {
                return;
            }

            string archiveName = $"{Path.GetFileNameWithoutExtension(filePath)}.{DateTime.Now:yyyyMMddHHmmss}.log";
            string archivePath = Path.Combine(LogDirectory, archiveName);
            File.Move(filePath, archivePath, overwrite: true);

            CleanupArchivedLogs(Path.GetFileNameWithoutExtension(filePath));
        }
        catch
        {
            // Ignore rotation failures to avoid impacting application flow.
        }
    }

    private static void CleanupArchivedLogs(string baseName)
    {
        try
        {
            var pattern = $"{baseName}.*.log";
            var archives = Directory
                .GetFiles(LogDirectory, pattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToList();

            var retentionCutoff = DateTime.UtcNow.AddDays(-LoggingOptions.RetentionDays);
            foreach (var archive in archives.Where(file => file.CreationTimeUtc < retentionCutoff))
            {
                archive.Delete();
            }

            archives = Directory
                .GetFiles(LogDirectory, pattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToList();

            for (int idx = LoggingOptions.MaxArchivedFiles; idx < archives.Count; idx++)
            {
                archives[idx].Delete();
            }
        }
        catch
        {
            // Ignore cleanup failures to avoid impacting application flow.
        }
    }

    private static void EnsureLogDirectoryExists()
    {
        // Directory.CreateDirectory is idempotent and handles the race condition
        // where multiple threads check existence simultaneously.
        // It will succeed if directory exists or create it if not.
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            // Insufficient permissions to create or access the directory - logging will be disabled
        }
        catch (PathTooLongException)
        {
            // Log path is too long for the current platform - logging will be disabled
        }
        catch (NotSupportedException)
        {
            // Log path format is not supported - logging will be disabled
        }
        catch (IOException)
        {
            // Directory may have been created by another thread or other IO error - this is acceptable
        }
    }
}
