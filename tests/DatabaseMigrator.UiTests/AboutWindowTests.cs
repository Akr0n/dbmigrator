using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DatabaseMigrator.Views;

namespace DatabaseMigrator.UiTests;

public class AboutWindowTests
{
    [AvaloniaFact]
    public async Task AboutMenu_OpensDialogShowingTheVersion()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            window.FindControl<MenuItem>("AboutMenuItem")!
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var about = await Ui.WaitForAsync(
                () => window.OwnedWindows.OfType<AboutWindow>().FirstOrDefault(), "the about dialog");
            var version = about.FindControl<TextBlock>("AboutVersionText");
            Assert.NotNull(version);
            Assert.Contains(AppVersion.Current, version.Text);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task AboutDialog_ShowsSupportInfo()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            window.FindControl<MenuItem>("AboutMenuItem")!
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var about = await Ui.WaitForAsync(
                () => window.OwnedWindows.OfType<AboutWindow>().FirstOrDefault(), "the about dialog");
            var support = about.FindControl<TextBox>("AboutSupportText");
            Assert.NotNull(support);
            Assert.Contains(AppVersion.Current, support.Text);
            Assert.Contains(System.Runtime.InteropServices.RuntimeInformation.OSDescription, support.Text);
            Assert.Contains(".NET", support.Text);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task AboutDialog_CloseButtonClosesIt()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            window.FindControl<MenuItem>("AboutMenuItem")!
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var about = await Ui.WaitForAsync(
                () => window.OwnedWindows.OfType<AboutWindow>().FirstOrDefault(), "the about dialog");
            var close = about.FindControl<Button>("AboutCloseButton");
            Assert.NotNull(close);
            Ui.Click(close);
            await Ui.WaitUntilAsync(() => !about.IsVisible, "the about dialog to close");
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }
}
