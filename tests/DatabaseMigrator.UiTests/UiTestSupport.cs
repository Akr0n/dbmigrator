using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using DatabaseMigrator.ViewModels;
using DatabaseMigrator.Views;
using ReactiveUI;

namespace DatabaseMigrator.UiTests;

/// <summary>A database service with no database behind it: every call returns what the test configured.</summary>
internal sealed class FakeDatabaseService : IDatabaseService
{
    public Func<ConnectionInfo, Task<bool>> TestConnection { get; set; } = _ => Task.FromResult(true);

    public Func<ConnectionInfo, Task<List<TableInfo>>> GetTables { get; set; } = _ => Task.FromResult(new List<TableInfo>());

    public int TestConnectionCalls { get; private set; }

    public int DatabaseExistsCalls { get; private set; }

    public Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo)
    {
        TestConnectionCalls++;
        return TestConnection(connectionInfo);
    }

    public Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo) => GetTables(connectionInfo);

    public Task<bool> DatabaseExistsAsync(ConnectionInfo connectionInfo)
    {
        DatabaseExistsCalls++;
        return Task.FromResult(true);
    }

    public Task<string?> CreateDatabaseAsync(ConnectionInfo connectionInfo) => throw new NotSupportedException();

    public Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, string tableName, string schema) => throw new NotSupportedException();

    public Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, TableInfo table, IProgress<int> progress,
        IEnumerable<TableInfo>? tablesLoadedLater = null) => throw new NotSupportedException();
}

internal static class Ui
{
    /// <summary>Waits (letting the UI thread run) until <paramref name="read"/> returns something other than null.</summary>
    public static async Task<T> WaitForAsync<T>(Func<T?> read, string what, int timeoutMs = 5000) where T : class
    {
        for (int waited = 0; waited <= timeoutMs; waited += 20)
        {
            if (read() is { } value)
                return value;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {what}");
    }

    /// <summary>Waits (letting the UI thread run) until the condition holds.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        for (int waited = 0; waited <= timeoutMs; waited += 20)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {what}");
    }

    // The task itself is kept, failure included: if the window is slow to start and the first test times out, every later
    // test fails with that same error at once instead of building a second window, which would hang the whole test host.
    private static Task<(MainWindow Window, MainWindowViewModel ViewModel)>? _shared;

    /// <summary>
    /// The real main window, opened once and waited for until it has created its view model. A second MainWindow cannot be
    /// built in the same headless process (its constructor never returns) and the application only ever has one, so every
    /// test shares this one and puts it back with <see cref="Reset"/> when it is done.
    /// </summary>
    public static Task<(MainWindow Window, MainWindowViewModel ViewModel)> OpenWindowAsync() => _shared ??= CreateWindowAsync();

    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> CreateWindowAsync()
    {
        var window = new MainWindow();
        window.Show();
        // Generous: a loaded one-core CI runner needs several seconds to start the window.
        var viewModel = await WaitForAsync(() => window.DataContext as MainWindowViewModel, "the window's view model", timeoutMs: 60000);
        return (window, viewModel);
    }

    /// <summary>Puts the shared window back as a test found it: no dialog open, idle, disconnected, fields empty.</summary>
    public static void Reset(MainWindow window, MainWindowViewModel viewModel)
    {
        foreach (var owned in window.OwnedWindows.ToList())
            owned.Close();

        viewModel.IsMigrating = false;
        viewModel.IsConnected = false;
        window.FindControl<TextBox>("SourceServerTextBox")!.Text = "";
        window.FindControl<ComboBox>("SourceTypeCombo")!.SelectedIndex = 0;
        window.FindControl<TextBlock>("ErrorTextBlock")!.Text = "";
    }

    /// <summary>Runs a command the way a click does, and waits for it to finish.</summary>
    public static async Task RunAsync(ReactiveCommand<Unit, Unit> command) => await command.Execute();

    /// <summary>Raises the click on a button even when it is disabled, as a stray event or a script could.</summary>
    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}

/// <summary>Builds view models wired to a <see cref="FakeDatabaseService"/>.</summary>
internal static class Vm
{
    /// <summary>Tables from "schema.name" strings.</summary>
    public static List<TableInfo> Tables(params string[] qualifiedNames) =>
        qualifiedNames.Select(name =>
        {
            var parts = name.Split('.');
            return new TableInfo { Schema = parts[0], TableName = parts[1] };
        }).ToList();

    /// <summary>A view model whose connection fields are filled in, but which has not connected yet.</summary>
    public static MainWindowViewModel Create(FakeDatabaseService service)
    {
        var viewModel = new MainWindowViewModel(service, null);
        viewModel.SourceConnection!.Server = "source-host";
        viewModel.SourceConnection.Database = "source_db";
        viewModel.TargetConnection!.Server = "target-host";
        viewModel.TargetConnection.Database = "target_db";
        return viewModel;
    }

    /// <summary>A view model that has gone through a successful Connect against the fake service.</summary>
    public static async Task<MainWindowViewModel> ConnectedAsync(FakeDatabaseService service)
    {
        var viewModel = Create(service);
        await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
        await Ui.WaitUntilAsync(() => viewModel.IsConnected && !viewModel.IsMigrating, "the connection");
        return viewModel;
    }

    /// <summary>The selected tables as "schema.name", sorted.</summary>
    public static string[] Selected(MainWindowViewModel viewModel) =>
        viewModel.Tables.Where(t => t.IsSelected).Select(t => $"{t.Schema}.{t.TableName}").Order().ToArray();
}
