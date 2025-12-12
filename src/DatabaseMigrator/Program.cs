using Avalonia;
using System;
using System.IO;

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
            // Scrivi errore in file di log
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "DatabaseMigrator");
            string logPath = Path.Combine(logDir, "error.log");
            
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {ex}\n\n");
            
            // Mostra errore in console
            Console.WriteLine($"FATAL ERROR: {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
