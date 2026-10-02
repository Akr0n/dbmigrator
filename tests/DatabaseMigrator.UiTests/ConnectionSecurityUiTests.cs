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
        // It starts as the runtime settings say (unchecked unless DBMIGRATOR_TRUST_SERVER_CERTIFICATE asks otherwise).
        bool trustByDefault = RuntimeOptionsProvider.Current.Security.TrustServerCertificateByDefault;
        foreach (string name in Ui.SecurityCheckBoxes)
            Assert.Equal(name.Contains("Trust") ? trustByDefault : false, Ui.InitialCheckBoxStates[name]);
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
            // Boxes the user had ticked before the load: the loaded "off" must replace them, or the next Connect reads them back
            // and silently turns the verification of the server off.
            window.FindControl<CheckBox>("SourceTrustServerCertificateCheckBox")!.IsChecked = true;
            window.FindControl<CheckBox>("TargetRequireEncryptionCheckBox")!.IsChecked = false;

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
    public async Task ALoadedConfigurationWithABlankDatabase_StillFillsTheBoxes()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            // A hand-edited or older file can leave a server or database blank: that side's ConnectionInfo is then null, and the
            // fields and boxes used to be skipped, so the next Connect read the previous (stale) ones over the loaded settings.
            viewModel.SourceConnection!.SelectedDatabaseType = DatabaseType.PostgreSQL;
            viewModel.SourceConnection.Server = "s";
            viewModel.SourceConnection.Database = "";
            viewModel.SourceConnection.RequireEncryption = true;
            Assert.Null(viewModel.SourceConnection.ConnectionInfo);

            typeof(Views.MainWindow).GetMethod("ShowConnectionsInFields", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);

            Assert.True(window.FindControl<CheckBox>("SourceRequireEncryptionCheckBox")!.IsChecked);
            Assert.Equal("s", window.FindControl<TextBox>("SourceServerTextBox")!.Text);
            Assert.Equal("", window.FindControl<TextBox>("SourceDatabaseTextBox")!.Text);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task SavingTheConfiguration_WritesWhatTheFieldsShow_AndLeavesTheLiveConnectionsAlone()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        string path = Path.Combine(Path.GetTempPath(), $"dbmigrator_save_{Guid.NewGuid():N}.json");
        try
        {
            // Nothing was connected: the view model still holds its initial connections, which are what Save used to write.
            window.FindControl<ComboBox>("SourceTypeCombo")!.SelectedIndex = (int)DatabaseType.PostgreSQL;
            window.FindControl<TextBox>("SourceServerTextBox")!.Text = "source-host";
            window.FindControl<TextBox>("SourceDatabaseTextBox")!.Text = "source_db";
            window.FindControl<ComboBox>("TargetTypeCombo")!.SelectedIndex = (int)DatabaseType.PostgreSQL;
            window.FindControl<TextBox>("TargetServerTextBox")!.Text = "target-host";
            window.FindControl<TextBox>("TargetDatabaseTextBox")!.Text = "target_db";
            window.FindControl<CheckBox>("SourceRequireEncryptionCheckBox")!.IsChecked = true;
            window.FindControl<CheckBox>("TargetTrustServerCertificateCheckBox")!.IsChecked = true;
            window.FindControl<CheckBox>("TargetRequireEncryptionCheckBox")!.IsChecked = true;

            var save = typeof(Views.MainWindow).GetMethod("SaveConfigurationToAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True(await (Task<bool>)save.Invoke(window, [path])!);

            var saved = System.Text.Json.JsonSerializer.Deserialize<ConnectionConfig>(File.ReadAllText(path))!;
            var source = saved.Source!;
            var target = saved.Target!;
            Assert.Equal("source-host", source.Server);
            Assert.True(source.RequireEncryption);   // it used to be written as false: the box was ticked after the last Connect
            Assert.False(source.TrustServerCertificate);
            Assert.Equal("target-host", target.Server);
            Assert.True(target.RequireEncryption);
            Assert.True(target.TrustServerCertificate);

            // A save must not make unvalidated settings the ones Start Migration would run with.
            Assert.False(viewModel.SourceConnection!.RequireEncryption);
            Assert.Equal("", viewModel.SourceConnection.Server);
        }
        finally
        {
            File.Delete(path);
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task AFailedSqlServerConnection_SuggestsTheCertificateBox_UnlessItIsAlreadyChecked()
    {
        var service = new FakeDatabaseService { TestConnection = _ => Task.FromResult(false) };
        var viewModel = Vm.Create(service);
        viewModel.SourceConnection!.TrustServerCertificate = false; // whatever the ambient default is

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
        viewModel.SourceConnection.TrustServerCertificate = false; // whatever the ambient default is

        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

        Assert.Contains("TLS", viewModel.ErrorMessage);
        Assert.Contains("Accetta certificato server", viewModel.ErrorMessage); // verified by default: a self-signed one is refused

        viewModel.SourceConnection.TrustServerCertificate = true; // encrypted but not verified: only TLS support remains to blame
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
        Assert.Contains("TLS", viewModel.ErrorMessage);
        Assert.DoesNotContain("Accetta certificato server", viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task AFailedOracleConnectionThatRequiresEncryption_DoesNotPromiseThatTheBoxAcceptsASelfSignedCertificate()
    {
        var service = new FakeDatabaseService { TestConnection = _ => Task.FromResult(false) };
        var viewModel = Vm.Create(service);
        viewModel.SourceConnection!.SelectedDatabaseType = DatabaseType.Oracle;
        viewModel.SourceConnection.RequireEncryption = true;

        foreach (bool trust in new[] { false, true })
        {
            viewModel.SourceConnection.TrustServerCertificate = trust;

            await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

            // For Oracle the box skips only the check of the certificate's name: the issuer is still verified, so ticking it does
            // not make a self-signed certificate work and the message must not say it does.
            Assert.Contains("TLS", viewModel.ErrorMessage);
            Assert.Contains("Windows", viewModel.ErrorMessage);
            Assert.DoesNotContain("oppure spunta", viewModel.ErrorMessage);
        }
    }
}
