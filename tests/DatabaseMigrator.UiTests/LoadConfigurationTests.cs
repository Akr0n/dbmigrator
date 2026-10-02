using Avalonia.Headless.XUnit;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.ViewModels;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// Loading a saved configuration replaces both connections. That must never happen under a running migration, and it must
/// keep the saved port and leave the application disconnected: the loaded settings have not been validated.
/// </summary>
public class LoadConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dbmigrator_ui_{Guid.NewGuid():N}");

    public LoadConfigurationTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Saves a configuration for PostgreSQL on port 5433 (not the default) at both ends.</summary>
    private async Task<string> SaveConfigurationAsync()
    {
        var viewModel = Vm.Create(new FakeDatabaseService());
        foreach (var connection in new[] { viewModel.SourceConnection!, viewModel.TargetConnection! })
        {
            connection.SelectedDatabaseType = DatabaseType.PostgreSQL; // first: its setter resets the port to the default
            connection.Port = 5433;
            connection.Username = "pguser";
        }

        string path = Path.Combine(_directory, "config.json");
        Assert.True(await viewModel.SaveConfigurationAsync(path));
        return path;
    }

    [AvaloniaFact]
    public async Task ALoadedConfiguration_KeepsItsPort_AndLeavesTheApplicationDisconnected()
    {
        string path = await SaveConfigurationAsync();
        var viewModel = Vm.Create(new FakeDatabaseService());
        viewModel.IsConnected = true;

        Assert.True(await viewModel.LoadConfigurationAsync(path));

        Assert.Equal(DatabaseType.PostgreSQL, viewModel.SourceConnection!.SelectedDatabaseType);
        Assert.Equal(5433, viewModel.SourceConnection.Port); // it used to come back as the default, 5432
        Assert.Equal(5433, viewModel.TargetConnection!.Port);
        Assert.False(viewModel.IsConnected);                 // the table list belongs to the previous source
    }

    [AvaloniaFact]
    public async Task AConfigurationWithAGoodSourceAndABadTarget_ChangesNothing_AndStaysConnected()
    {
        string path = await SaveConfigurationAsync();
        // The target block is the second one in the file: give it a database type that does not exist.
        string json = File.ReadAllText(path);
        int target = json.LastIndexOf("PostgreSQL", StringComparison.Ordinal);
        Assert.True(target > json.IndexOf("PostgreSQL", StringComparison.Ordinal), "The saved file must hold two PostgreSQL blocks");
        File.WriteAllText(path, json.Remove(target, "PostgreSQL".Length).Insert(target, "NoSuchDatabase"));

        var viewModel = Vm.Create(new FakeDatabaseService());
        viewModel.IsConnected = true;
        ConnectionViewModel source = viewModel.SourceConnection!, target0 = viewModel.TargetConnection!;

        Assert.False(await viewModel.LoadConfigurationAsync(path));

        Assert.Same(source, viewModel.SourceConnection); // the good source of the file was not applied on its own
        Assert.Same(target0, viewModel.TargetConnection);
        Assert.Equal("source-host", viewModel.SourceConnection!.Server);
        Assert.True(viewModel.IsConnected);              // nothing changed, so the old, validated connection is still the one in use
        Assert.NotEmpty(viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task ALoadedConfiguration_ClearsTheConnectedLabelsAndTheOldError()
    {
        string path = await SaveConfigurationAsync();
        var viewModel = await Vm.ConnectedAsync(new FakeDatabaseService());
        Assert.Equal("● Connesso", viewModel.SourceStatusText);
        viewModel.ErrorMessage = "an old error";

        Assert.True(await viewModel.LoadConfigurationAsync(path));

        Assert.Equal("", viewModel.SourceStatusText); // it used to keep saying "Connesso" for settings nobody had tested
        Assert.Equal("", viewModel.TargetStatusText);
        Assert.Equal("", viewModel.ErrorMessage);
        Assert.False(viewModel.IsConnected);
    }

    [AvaloniaFact]
    public async Task LoadingWhileAMigrationRuns_ChangesNothing()
    {
        string path = await SaveConfigurationAsync();
        var viewModel = Vm.Create(new FakeDatabaseService());
        ConnectionViewModel source = viewModel.SourceConnection!, target = viewModel.TargetConnection!;
        viewModel.IsMigrating = true;

        Assert.False(await viewModel.LoadConfigurationAsync(path));

        Assert.Same(source, viewModel.SourceConnection);
        Assert.Same(target, viewModel.TargetConnection);
        Assert.Equal(DatabaseType.SqlServer, viewModel.SourceConnection!.SelectedDatabaseType);
    }

    [AvaloniaFact]
    public async Task ALoadThatIsReadingTheFileWhenAMigrationStarts_ChangesNothing()
    {
        string path = await SaveConfigurationAsync();
        File.AppendAllText(path, new string(' ', 4_000_000)); // trailing blanks: slow enough to read that the read does not finish at once
        var viewModel = Vm.Create(new FakeDatabaseService());
        ConnectionViewModel source = viewModel.SourceConnection!, target = viewModel.TargetConnection!;

        var load = viewModel.LoadConfigurationAsync(path); // passes the "is a migration running" check, then waits for the file
        Assert.False(load.IsCompleted, "The file was read synchronously: the test cannot reproduce the window it is about");
        viewModel.IsMigrating = true;                      // the user starts a migration in that window

        Assert.False(await load);
        Assert.Same(source, viewModel.SourceConnection);
        Assert.Same(target, viewModel.TargetConnection);
    }
}
