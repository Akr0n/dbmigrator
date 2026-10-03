using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace DatabaseMigrator.UiTests;

public class WindowSmokeTests
{
    [AvaloniaFact]
    public async Task TheRealWindowOpensAndCreatesItsViewModel()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            Assert.False(viewModel.IsConnected);
            Assert.False(viewModel.IsMigrating);
            Assert.True(window.FindControl<Button>("ConnectButton")!.IsEnabled);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    // Ui.CreateWindowAsync asks for 3000x2000, more than the headless screen (1920x1280 at 100%) has: on opening, the window must shrink to
    // that screen's work area and sit at its origin. The first version subscribed Opened in InitializeViewModel, after it had already
    // been raised, so the fit never ran, and nothing but this test looks at that wiring. (Headless windows report no frame, so the
    // title bar and borders - frame minus client, 14.4x37.6 units at 125% on a real window - are covered by WindowFitTests, not here.)
    [AvaloniaFact]
    public async Task TheWindowIsFittedToTheScreenItOpensOn()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var area = window.Screens.Primary!.WorkingArea;

            await Ui.WaitUntilAsync(() => window.Width == area.Width && window.Height == area.Height, "the window to be fitted to the screen");
            Assert.Equal(area.Position, window.Position);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    // The title carries the version of the build: a window or a screenshot sent from another PC says which build it is.
    [AvaloniaFact]
    public async Task TheTitleShowsTheVersionOfTheBuild()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        // The constructor sets the title, then the connect handler overwrites it through a queued UI job: flush the queue, so the
        // assertion sees the final title and not the constructor's, which would pass whatever the handler assigns.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            await Ui.WaitUntilAsync(() => window.Title is { } t && t.Contains(AppVersion.Current), "the title to show the version", timeoutMs: 30000);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }
}
