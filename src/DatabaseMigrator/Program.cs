using Avalonia;
using System;
using System.IO;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Log error using centralized logger
            LoggerService.LogError("FATAL ERROR in Main", ex);
            
            // Show error in console for debugging
            Console.WriteLine($"FATAL ERROR: {ex}");
            Console.Error.WriteLine($"Error logged to: {LoggerService.GetErrorLogPath()}");
            
            // Re-throw to allow proper exception handling and stack trace reporting
            // This enables proper cleanup via finally blocks and provides better diagnostics
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
