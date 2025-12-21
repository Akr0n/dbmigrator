using System;
using System.IO;

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

    private static readonly string LogPath = Path.Combine(LogDirectory, "debug.log");
    private static readonly string ErrorLogPath = Path.Combine(LogDirectory, "error.log");

    private static readonly object LockObject = new object();

    /// <summary>
    /// Logs a debug message to the debug log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message)
    {
        try
        {
            EnsureLogDirectoryExists();
            
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}\n";
            
            lock (LockObject)
            {
                File.AppendAllText(LogPath, fullMessage);
            }
            
            System.Diagnostics.Debug.WriteLine(fullMessage.TrimEnd());
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
            
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - ERROR: {message}";
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
                File.AppendAllText(ErrorLogPath, fullMessage);
                File.AppendAllText(LogPath, fullMessage);
            }
            
            System.Diagnostics.Debug.WriteLine(fullMessage.TrimEnd());
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

    private static void EnsureLogDirectoryExists()
    {
        // Directory.CreateDirectory is idempotent and handles the race condition
        // where multiple threads check existence simultaneously.
        // It will succeed if directory exists or create it if not.
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (IOException)
        {
            // Directory may have been created by another thread - this is acceptable
        }
    }
}
