using Avalonia;
using Avalonia.Headless;
using DatabaseMigrator;
using DatabaseMigrator.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace DatabaseMigrator.UiTests;

/// <summary>Starts the real <see cref="App"/> (its styles and resources) on a headless platform, with no screen.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
