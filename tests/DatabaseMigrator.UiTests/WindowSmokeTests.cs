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
}
