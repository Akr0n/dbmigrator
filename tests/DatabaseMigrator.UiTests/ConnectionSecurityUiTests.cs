using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The connection's security choices ("accept the server certificate", "require encryption") must start off, must travel from the
/// boxes to the connections that are actually opened, must come back into the boxes when a configuration is loaded (a box that
/// shows "off" while the loaded setting is "on" would silently open an unencrypted connection) and, when a connection fails
/// because of them, the message must say so.
/// </summary>
public class ConnectionSecurityUiTests
{
    [AvaloniaFact]
    public async Task TheCertificateAndEncryptionBoxes_StartUnchecked()
    {
        await Ui.OpenWindowAsync(); // the states are read when the window is first built

        // "Accept the certificate" used to start checked, which switched the verification of every SQL Server connection off.
        foreach (string name in Ui.SecurityCheckBoxes)
            Assert.False(Ui.InitialCheckBoxStates[name], name);
    }

    [AvaloniaFact]
    public async Task Connect_CopiesTheCertificateAndEncryptionBoxesIntoTheConnections()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            foreach (string name in Ui.SecurityCheckBoxes)
                window.FindControl<CheckBox>(name)!.IsChecked = true;

            // The source server is empty, so Connect stops at its validation: no connection is tried, but the fields were read.
            Ui.Click(window.FindControl<Button>("ConnectButton")!);
            await Ui.WaitUntilAsync(() => viewModel.ErrorMessage.Contains("Server"), "the validation message");

            Assert.True(viewModel.SourceConnection!.TrustServerCertificate);
            Assert.True(viewModel.SourceConnection.RequireEncryption);
            Assert.True(viewModel.TargetConnection!.TrustServerCertificate);
            Assert.True(viewModel.TargetConnection.RequireEncryption);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task ALoadedConfiguration_PutsItsChoicesBackIntoTheBoxes()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            viewModel.SourceConnection!.SelectedDatabaseType = DatabaseType.PostgreSQL;
            viewModel.SourceConnection.RequireEncryption = true;
            viewModel.SourceConnection.TrustServerCertificate = false;
            viewModel.TargetConnection!.SelectedDatabaseType = DatabaseType.Oracle;
            viewModel.TargetConnection.RequireEncryption = true;
            viewModel.TargetConnection.TrustServerCertificate = true;
            viewModel.SourceConnection.Server = "s";
            viewModel.SourceConnection.Database = "d";
            viewModel.TargetConnection.Server = "t";
            viewModel.TargetConnection.Database = "d";

            // What the window does once LoadConfigurationAsync has replaced the connections (the file picker cannot be driven here).
            typeof(Views.MainWindow).GetMethod("ShowConnectionsInFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);

            Assert.True(window.FindControl<CheckBox>("SourceRequireEncryptionCheckBox")!.IsChecked);
            Assert.False(window.FindControl<CheckBox>("SourceTrustServerCertificateCheckBox")!.IsChecked);
            Assert.True(window.FindControl<CheckBox>("TargetRequireEncryptionCheckBox")!.IsChecked);
            Assert.True(window.FindControl<CheckBox>("TargetTrustServerCertificateCheckBox")!.IsChecked);
            Assert.Equal("s", window.FindControl<TextBox>("SourceServerTextBox")!.Text);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task AFailedSqlServerConnection_SuggestsTheCertificateBox_UnlessItIsAlreadyChecked()
    {
        var service = new FakeDatabaseService { TestConnection = _ => Task.FromResult(false) };
        var viewModel = Vm.Create(service);

        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
        Assert.Contains("Accetta certificato server", viewModel.ErrorMessage);

        viewModel.SourceConnection!.TrustServerCertificate = true;
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
        Assert.DoesNotContain("Accetta certificato server", viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task AFailedConnectionThatRequiresEncryption_SaysTheServerMustOfferTls()
    {
        var service = new FakeDatabaseService { TestConnection = _ => Task.FromResult(false) };
        var viewModel = Vm.Create(service);
        viewModel.SourceConnection!.SelectedDatabaseType = DatabaseType.PostgreSQL;
        viewModel.SourceConnection.RequireEncryption = true;

        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

        Assert.Contains("TLS", viewModel.ErrorMessage);
        Assert.Contains("Accetta certificato server", viewModel.ErrorMessage); // verified by default: a self-signed one is refused

        viewModel.SourceConnection.TrustServerCertificate = true; // encrypted but not verified: only TLS support remains to blame
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
        Assert.Contains("TLS", viewModel.ErrorMessage);
        Assert.DoesNotContain("Accetta certificato server", viewModel.ErrorMessage);
    }
}
