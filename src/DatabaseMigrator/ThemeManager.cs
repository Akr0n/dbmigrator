using System;
using System.IO;
using Avalonia;
using Avalonia.Styling;

namespace DatabaseMigrator;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public static class ThemeManager
{
    // Next to the saved connection configurations: %LOCALAPPDATA%\DatabaseMigrator\theme.txt.
    // Tests point it at a temp file via DBMIGRATOR_THEME_FILE, so they never touch real user state.
    public static string StoragePath =>
        Environment.GetEnvironmentVariable("DBMIGRATOR_THEME_FILE") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DatabaseMigrator",
                "theme.txt");

    public static ThemePreference Load(string? path = null)
    {
        try
        {
            var file = path ?? StoragePath;
            if (!File.Exists(file))
                return ThemePreference.System;
            return File.ReadAllText(file).Trim() switch
            {
                nameof(ThemePreference.Light) => ThemePreference.Light,
                nameof(ThemePreference.Dark) => ThemePreference.Dark,
                _ => ThemePreference.System
            };
        }
        catch
        {
            return ThemePreference.System;
        }
    }

    public static void Save(ThemePreference preference, string? path = null)
    {
        try
        {
            var file = path ?? StoragePath;
            if (Path.GetDirectoryName(file) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(file, preference.ToString());
        }
        catch
        {
            // A theme choice must never break the app: worst case it is not remembered.
        }
    }

    public static void Apply(ThemePreference preference)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = preference switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default // follows the OS theme
            };
        }
    }
}
