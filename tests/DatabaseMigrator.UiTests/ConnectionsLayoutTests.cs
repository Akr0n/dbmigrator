using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The Connections tab must fit a 900-tall window at 1400 width without scrolling:
/// the tab content measured at that width must stay within the viewport budget.
/// </summary>
public class ConnectionsLayoutTests
{
    [AvaloniaFact]
    public async Task ConnectionsTab_FitsWithoutScrolling()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            // The TabControl only presents the selected tab: make sure the Connections tab is on screen.
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 0;
            var scroller = await Ui.WaitForAsync(
                () => window.FindControl<ScrollViewer>("ConnectionsScroll"), "the connections scroll viewer");
            var content = Assert.IsType<Grid>(scroller.Content);
            content.Measure(new Size(1352, double.PositiveInfinity));
            // 900 window − ~150 chrome (OS title, menu, tab headers, status bar) − 32 grid margins.
            Assert.True(content.DesiredSize.Height <= 700,
                $"Connections content needs {content.DesiredSize.Height:0}px, over the 700px budget");
        }
        finally
        {
            window.InvalidateMeasure();
            await Task.Delay(200);
            Ui.Reset(window, viewModel);
        }
    }
}
