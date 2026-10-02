using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.ViewModels;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// A migration (or a reload) reads the connections and the migration mode again at every step. While one runs, nothing that
/// replaces the connections, reconnects or changes the mode may be reachable, and a failed reconnect must not leave the
/// application "connected" with settings that were never validated.
/// </summary>
public class ConnectionGuardTests
{
    [AvaloniaFact]
    public async Task WhileAMigrationRuns_ConnectLoadConfigurationAndTheModeButtonsAreDisabled()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var connect = window.FindControl<Button>("ConnectButton")!;
            var load = window.FindControl<MenuItem>("LoadConfigMenuItem")!;
            var start = window.FindControl<Button>("StartMigrationButton")!;
            var modes = new[] { "ModeSchemaAndData", "ModeSchemaOnly", "ModeDataOnly" }
                .Select(name => window.FindControl<RadioButton>(name)!).ToArray();

            viewModel.IsConnected = true;
            Assert.True(connect.IsEnabled);
            Assert.True(load.IsEnabled);
            Assert.True(start.IsEnabled);
            Assert.All(modes, mode => Assert.True(mode.IsEnabled));

            viewModel.IsMigrating = true;
            Assert.False(connect.IsEnabled);
            Assert.False(load.IsEnabled);
            Assert.False(start.IsEnabled);
            Assert.All(modes, mode => Assert.False(mode.IsEnabled));

            viewModel.IsMigrating = false;
            Assert.True(connect.IsEnabled);
            Assert.True(load.IsEnabled);
            Assert.True(start.IsEnabled);
            Assert.All(modes, mode => Assert.True(mode.IsEnabled));
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task AConnectClickWhileAMigrationRuns_IsIgnored_ButOneWhenIdleIsHandled()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var connect = window.FindControl<Button>("ConnectButton")!;
            var error = window.FindControl<TextBlock>("ErrorTextBlock")!;
            var sourceType = window.FindControl<ComboBox>("SourceTypeCombo")!;
            window.FindControl<TextBox>("SourceServerTextBox")!.Text = "other-host";

            // Running: the click reaches the handler (a disabled button can still be sent one) and must change nothing.
            sourceType.SelectedIndex = 0;
            viewModel.IsMigrating = true;
            Ui.Click(connect);
            Assert.Equal("", viewModel.SourceConnection!.Server);
            Assert.True(viewModel.IsMigrating);

            // Control: the same click when idle does get handled; an invalid type is reported before any connection is tried.
            viewModel.IsMigrating = false;
            sourceType.SelectedIndex = -1;
            Ui.Click(connect);
            Assert.Equal("Seleziona un tipo di database sorgente valido", error.Text);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task TheConnectCommandWhileAMigrationRuns_DoesNothing()
    {
        var service = new FakeDatabaseService();
        var viewModel = Vm.Create(service);
        viewModel.IsMigrating = true;

        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

        Assert.True(viewModel.IsMigrating); // it used to clear the flag in its finally, re-enabling "Avvia Migrazione"
        Assert.Equal(0, service.TestConnectionCalls);
        Assert.False(viewModel.IsConnected);
    }

    [AvaloniaFact]
    public async Task ASuccessfulConnect_ListsTheTablesAndConnects()
    {
        var service = new FakeDatabaseService { GetTables = _ => Task.FromResult(Vm.Tables("sales.Orders", "sales.Items", "hr.People")) };

        var viewModel = await Vm.ConnectedAsync(service);

        Assert.True(viewModel.IsConnected);
        Assert.False(viewModel.IsMigrating);
        Assert.Equal(3, viewModel.Tables.Count);
        Assert.Equal(3, viewModel.FilteredTables.Count);
    }

    [AvaloniaFact]
    public async Task AFailedReconnect_LeavesTheApplicationDisconnected()
    {
        var service = new FakeDatabaseService { GetTables = _ => Task.FromResult(Vm.Tables("sales.Orders")) };
        var viewModel = await Vm.ConnectedAsync(service);
        Assert.True(viewModel.IsConnected);

        // The target settings are edited and no longer work; Connect is pressed again.
        service.TestConnection = info => Task.FromResult(info.Server != "target-host");
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

        Assert.False(viewModel.IsConnected); // it used to stay connected, so Start ran with settings nobody had validated
        Assert.False(viewModel.IsMigrating);
        Assert.Contains("target", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task AReconnectThatFailsOnTheSource_AlsoLeavesTheApplicationDisconnected()
    {
        var service = new FakeDatabaseService { GetTables = _ => Task.FromResult(Vm.Tables("sales.Orders")) };
        var viewModel = await Vm.ConnectedAsync(service);

        service.TestConnection = info => Task.FromResult(info.Server != "source-host");
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);

        Assert.False(viewModel.IsConnected);
        Assert.Contains("sorgente", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task WhenTheConnectionIsLost_TheWindowReturnsToTheConnectionsTab()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var tabs = window.FindControl<TabControl>("MainTabControl")!;
            viewModel.IsConnected = true;
            tabs.SelectedIndex = 3; // "Genera Script"

            // A disabled TabItem does not disable the content already on screen: the Script tab would keep exporting from the
            // source that is no longer the connected one.
            viewModel.IsConnected = false;

            Assert.Equal(0, tabs.SelectedIndex);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task TheScriptTabButtons_DoNothingWhileTheApplicationIsNotConnected()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var load = window.FindControl<Button>("ScriptLoadObjectsButton")!;
            string before = viewModel.ScriptGeneration.StatusMessage;

            viewModel.IsConnected = false;
            Ui.Click(load);
            await Task.Delay(150); // the click handler is async void
            Assert.Equal(before, viewModel.ScriptGeneration.StatusMessage); // it used to list the previous source's objects

            // Control: when connected the same click does reach the view model (which has no source connection yet and says so).
            viewModel.IsConnected = true;
            Ui.Click(load);
            await Ui.WaitUntilAsync(() => viewModel.ScriptGeneration.StatusMessage != before, "the script tab to answer");
            Assert.Equal("Nessuna connessione sorgente disponibile.", viewModel.ScriptGeneration.StatusMessage);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public void ReconnectingToAnotherSource_DropsTheObjectsListedFromThePreviousOne()
    {
        var viewModel = new ScriptGenerationViewModel();
        viewModel.SetSourceConnection(new ConnectionInfo { DatabaseType = DatabaseType.SqlServer, Server = "a", Database = "a" });
        var listed = new DatabaseObject { Schema = "dbo", Name = "only_in_a", ObjectType = DatabaseObjectType.Table, IsSelected = true };
        var all = (List<DatabaseObject>)typeof(ScriptGenerationViewModel)
            .GetField("_allObjects", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewModel)!;
        all.Add(listed);
        viewModel.Objects.Add(listed);

        viewModel.SetSourceConnection(new ConnectionInfo { DatabaseType = DatabaseType.SqlServer, Server = "b", Database = "b" });

        // Left in place, "Genera script" exported dbo.only_in_a from database b, wrote errors into the file and reported success.
        Assert.Empty(viewModel.Objects);
        Assert.Equal(0, viewModel.TotalObjectsCount);
        Assert.Equal(0, viewModel.SelectedCount);
    }

    [AvaloniaFact]
    public void TheMigrationMode_CannotBeChangedWhileAMigrationRuns()
    {
        var viewModel = Vm.Create(new FakeDatabaseService());
        Assert.Equal(MigrationMode.SchemaAndData, viewModel.SelectedMigrationMode);

        viewModel.IsMigrating = true;
        viewModel.SelectedMigrationMode = MigrationMode.DataOnly; // would add a TRUNCATE and load to a run started otherwise
        Assert.Equal(MigrationMode.SchemaAndData, viewModel.SelectedMigrationMode);

        viewModel.IsMigrating = false;
        viewModel.SelectedMigrationMode = MigrationMode.DataOnly;
        Assert.Equal(MigrationMode.DataOnly, viewModel.SelectedMigrationMode);
    }
}
