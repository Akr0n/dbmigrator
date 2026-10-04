using Avalonia;
using Avalonia.Headless;
using DatabaseMigrator.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace DatabaseMigrator.UiTests;

/// <summary>
/// Starts the real <see cref="App"/> (its styles and resources) on a headless platform, with no screen. Text is measured with
/// Skia, as in the real application: with the faked drawing of <c>UseHeadlessDrawing = true</c> every string has a made-up size,
/// so a layout test (a dialog too small for its text) could never fail.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
