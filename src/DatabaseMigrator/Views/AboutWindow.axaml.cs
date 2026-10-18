using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace DatabaseMigrator.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var info = $"Database Migrator {AppVersion.Current}{Environment.NewLine}" +
                   $"{RuntimeInformation.OSDescription}{Environment.NewLine}" +
                   $"{RuntimeInformation.FrameworkDescription}";
        AboutVersionText.Text = $"Versione {AppVersion.Current}";
        AboutSupportText.Text = info;
        AboutCopyButton.Click += async (s, e) =>
        {
            try
            {
                if (Clipboard is not null)
                {
                    await Clipboard.SetTextAsync(info);
                    AboutCopyButton.Content = "Copiato";
                }
            }
            catch
            {
                // Copying is best effort: the text stays selectable in the box.
            }
        };
        AboutLogButton.Click += (s, e) =>
        {
            try
            {
                var dir = Path.GetDirectoryName(Core.Services.LoggerService.GetLogPath());
                if (dir is not null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch
            {
                // Opening the folder is best effort.
            }
        };
        AboutReleasesButton.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Akr0n/dbmigrator/releases") { UseShellExecute = true });
            }
            catch
            {
                // Opening the browser is best effort.
            }
        };
        AboutCloseButton.Click += (s, e) => Close();
    }
}
