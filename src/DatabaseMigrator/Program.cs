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
            
            // Exit gracefully with error code instead of re-throwing
            // This prevents ugly crash dialogs while still indicating an error occurred
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
