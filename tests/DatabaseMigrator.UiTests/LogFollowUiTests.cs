using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DatabaseMigrator.Core.Services;
using DatabaseMigrator.ViewModels;
using DatabaseMigrator.Views;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The Log tab used to jump to the last line on every new entry, always: during a long migration a line a little higher
/// could not be read, because the list moved away from it. The "Segui" button switches that on and off.
/// </summary>
public class LogFollowUiTests
{
    private const int LogTabIndex = 4;
    private const int Lines = 300; // far more than the list shows at once

    [AvaloniaFact]
    public async Task FollowIsOn_WhenTheWindowOpens()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            Assert.True(viewModel.FollowLog);
            Assert.True(window.FindControl<ToggleButton>("FollowLogToggle")!.IsChecked);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task WithFollowOn_TheLogScrollsToTheLastLineAsItGrows()
    {
        var (window, viewModel, scroll) = await ShowLogAsync();
        try
        {
            await AddLinesAsync(viewModel, "follow-on");

            await Ui.WaitUntilAsync(() => AtTheEnd(scroll), "the log to scroll to its last line");
        }
        finally
        {
            await PutBackAsync(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task WithFollowOff_TheLogStaysWhereTheUserLeftIt()
    {
        var (window, viewModel, scroll) = await ShowLogAsync();
        try
        {
            await AddLinesAsync(viewModel, "before");
            await Ui.WaitUntilAsync(() => AtTheEnd(scroll), "the log to reach its end while following");

            viewModel.FollowLog = false;
            scroll.Offset = new Vector(0, 0); // the user scrolls back up to read
            await Ui.WaitUntilAsync(() => scroll.Offset.Y == 0, "the list to be at the top");

            await AddLinesAsync(viewModel, "while-reading");
            await Task.Delay(300); // the scroll, if there were one, is posted at background priority

            Assert.Equal(0, scroll.Offset.Y);
            Assert.False(AtTheEnd(scroll));
        }
        finally
        {
            await PutBackAsync(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task TurningFollowBackOn_JumpsToTheLastLineAtOnce()
    {
        var (window, viewModel, scroll) = await ShowLogAsync();
        try
        {
            viewModel.FollowLog = false;
            await AddLinesAsync(viewModel, "while-off");
            scroll.Offset = new Vector(0, 0);
            await Ui.WaitUntilAsync(() => scroll.Offset.Y == 0, "the list to be at the top");

            window.FindControl<ToggleButton>("FollowLogToggle")!.IsChecked = true; // the click: no new line arrives

            Assert.True(viewModel.FollowLog);
            await Ui.WaitUntilAsync(() => AtTheEnd(scroll), "the log to jump to its last line");
        }
        finally
        {
            await PutBackAsync(window, viewModel);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The shared window with the Log tab open and its list laid out (a tab that is not shown has no scroll viewer).</summary>
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel, ScrollViewer Scroll)> ShowLogAsync()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        await Ui.RunAsync(viewModel.ClearLogCommand);
        window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = LogTabIndex;
        var list = window.FindControl<ListBox>("LogListBox")!;
        var scroll = await Ui.WaitForAsync(() => list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(),
            "the log list's scroll viewer");
        return (window, viewModel, scroll);
    }

    private static async Task AddLinesAsync(MainWindowViewModel viewModel, string tag)
    {
        int before = viewModel.FilteredLogEntries.Count;
        for (int i = 0; i < Lines; i++)
            LoggerService.Log($"{tag} line {i}");
        await Ui.WaitUntilAsync(() => viewModel.FilteredLogEntries.Count >= before + Lines, "the log lines to reach the list");
    }

    private static bool AtTheEnd(ScrollViewer scroll) =>
        scroll.Extent.Height > scroll.Viewport.Height && scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 1;

    private static async Task PutBackAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        viewModel.FollowLog = true;
        window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 0;
        await Ui.RunAsync(viewModel.ClearLogCommand);
        Ui.Reset(window, viewModel);
    }
}
