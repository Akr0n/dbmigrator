using Avalonia;
using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using DatabaseMigrator.Core.Services;
using ReactiveUI;

namespace DatabaseMigrator;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Catch unhandled .NET exceptions (log before process exits)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LoggerService.LogError("UnhandledException (app domain)", ex);
        };

        // Catch unobserved task exceptions (can cause process exit in some configs)
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LoggerService.LogError("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // Catch unhandled ReactiveUI exceptions
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
            LoggerService.LogError("ReactiveUI unhandled exception", ex));

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
