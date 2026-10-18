using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using DatabaseMigrator.Views;

namespace DatabaseMigrator.UiTests;

/// <summary>Menu Modifica -&gt; Tema: Sistema / Chiaro / Scuro.</summary>
public class ThemeMenuTests
{
    private static (MenuItem Sistema, MenuItem Chiaro, MenuItem Scuro) ThemeItems(MainWindow window)
    {
        var modifica = window.FindControl<MenuItem>("ModificaMenuItem");
        Assert.NotNull(modifica);
        Assert.Equal("Modifica", modifica.Header);
        var tema = modifica.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TemaMenuItem");
        Assert.NotNull(tema);
        Assert.Equal("Tema", tema.Header);
        MenuItem Get(string name)
        {
            var item = tema.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == name);
            Assert.NotNull(item);
            return item;
        }
        return (Get("ThemeSystemMenuItem"), Get("ThemeLightMenuItem"), Get("ThemeDarkMenuItem"));
    }

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    // Point the storage at a temp file: the click handlers persist the choice, and the suite
    // must never touch the developer's real %LOCALAPPDATA%\DatabaseMigrator\theme.txt.
    private static string UseTempThemeFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"dbmigrator-theme-{Guid.NewGuid():N}.txt");
        Environment.SetEnvironmentVariable("DBMIGRATOR_THEME_FILE", temp);
        return temp;
    }

    private static void ReleaseTempThemeFile(string temp)
    {
        Environment.SetEnvironmentVariable("DBMIGRATOR_THEME_FILE", null);
        if (File.Exists(temp))
            File.Delete(temp);
    }

    [AvaloniaFact]
    public async Task ThemeMenu_OffersSystemLightAndDarkChoices()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var (sistema, chiaro, scuro) = ThemeItems(window);
            Assert.Equal("Sistema", sistema.Header);
            Assert.Equal("Chiaro", chiaro.Header);
            Assert.Equal("Scuro", scuro.Header);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task SelectingLightTheme_AppliesLightVariant()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        var temp = UseTempThemeFile();
        try
        {
            var (_, chiaro, _) = ThemeItems(window);
            Click(chiaro);
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            Assert.True(chiaro.IsChecked);
            AssertSingleChecked(window);
            Assert.Equal(ThemePreference.Light, ThemeManager.Load());
        }
        finally
        {
            ReleaseTempThemeFile(temp);
            app.RequestedThemeVariant = before;
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task SelectingDarkTheme_AppliesDarkVariant()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        var temp = UseTempThemeFile();
        try
        {
            var (_, _, scuro) = ThemeItems(window);
            Click(scuro);
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            Assert.True(scuro.IsChecked);
            AssertSingleChecked(window);
            Assert.Equal(ThemePreference.Dark, ThemeManager.Load());
        }
        finally
        {
            ReleaseTempThemeFile(temp);
            app.RequestedThemeVariant = before;
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task SelectingSystemTheme_FollowsSystemDefault()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        var temp = UseTempThemeFile();
        try
        {
            var (sistema, chiaro, _) = ThemeItems(window);
            Click(chiaro);
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            Click(sistema);
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
            Assert.True(sistema.IsChecked);
            AssertSingleChecked(window);
            Assert.Equal(ThemePreference.System, ThemeManager.Load());
        }
        finally
        {
            ReleaseTempThemeFile(temp);
            app.RequestedThemeVariant = before;
            Ui.Reset(window, viewModel);
        }
    }

    // The radio group must never leave zero or two options checked.
    private static void AssertSingleChecked(MainWindow window)
    {
        var items = ThemeItems(window);
        Assert.Equal(1, new[] { items.Sistema, items.Chiaro, items.Scuro }.Count(m => m.IsChecked));
    }

    [Fact]
    public void ThemePreference_SurvivesSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbmigrator-theme-{Guid.NewGuid():N}.txt");
        try
        {
            ThemeManager.Save(ThemePreference.Dark, path);
            Assert.Equal(ThemePreference.Dark, ThemeManager.Load(path));
            ThemeManager.Save(ThemePreference.Light, path);
            Assert.Equal(ThemePreference.Light, ThemeManager.Load(path));
            ThemeManager.Save(ThemePreference.System, path);
            Assert.Equal(ThemePreference.System, ThemeManager.Load(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ThemePreference_DefaultsToSystemWhenNothingSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbmigrator-theme-{Guid.NewGuid():N}.txt");
        Assert.Equal(ThemePreference.System, ThemeManager.Load(path));
    }

    [Fact]
    public void ThemeFileEnvOverride_RedirectsStorage()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"dbmigrator-theme-{Guid.NewGuid():N}.txt");
        var previous = Environment.GetEnvironmentVariable("DBMIGRATOR_THEME_FILE");
        Environment.SetEnvironmentVariable("DBMIGRATOR_THEME_FILE", temp);
        try
        {
            Assert.Equal(temp, ThemeManager.StoragePath);
            ThemeManager.Save(ThemePreference.Dark);
            Assert.Equal(ThemePreference.Dark, ThemeManager.Load());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBMIGRATOR_THEME_FILE", previous);
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // Proves the two variants actually look different: the themed brushes backing the custom
    // styles resolve to the light palette under Light and the dark palette under Dark.
    // (Asserted at application level on purpose: on headless, a window stops tracking later
    // variant changes after the first one, while the app itself always follows. End-to-end
    // rendering of both themes was verified separately with screenshots.)
    [AvaloniaFact]
    public async Task LightAndDark_ResolveDifferentBrushes()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        var temp = UseTempThemeFile();
        try
        {
            var (_, chiaro, scuro) = ThemeItems(window);
            Click(chiaro);
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            Assert.True(app.TryGetResource("AppPageBackground", ThemeVariant.Light, out var lightPage));
            Assert.Equal(Colors.White, ((ISolidColorBrush)lightPage!).Color);
            Assert.True(app.TryGetResource("AppSurfaceBackground", ThemeVariant.Light, out var lightSurface));
            Assert.Equal(Color.Parse("#f6f8fa"), ((ISolidColorBrush)lightSurface!).Color);
            Assert.True(app.TryGetResource("AppLogInfo", ThemeVariant.Light, out var lightLog));
            Assert.Equal(Color.Parse("#1f2328"), ((ISolidColorBrush)lightLog!).Color);

            Click(scuro);
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            Assert.True(app.TryGetResource("AppPageBackground", ThemeVariant.Dark, out var darkPage));
            Assert.Equal(Color.Parse("#0d1117"), ((ISolidColorBrush)darkPage!).Color);
            Assert.True(app.TryGetResource("AppSurfaceBackground", ThemeVariant.Dark, out var darkSurface));
            Assert.Equal(Color.Parse("#161b22"), ((ISolidColorBrush)darkSurface!).Color);
            Assert.True(app.TryGetResource("AppLogInfo", ThemeVariant.Dark, out var darkLog));
            Assert.Equal(Color.Parse("#e0e0e0"), ((ISolidColorBrush)darkLog!).Color);
        }
        finally
        {
            ReleaseTempThemeFile(temp);
            app.RequestedThemeVariant = before;
            Ui.Reset(window, viewModel);
        }
    }
}
