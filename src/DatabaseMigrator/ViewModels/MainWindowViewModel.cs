using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly SchemaMigrationService _schemaMigrationService;

    private ConnectionViewModel? _sourceConnection;
    private ConnectionViewModel? _targetConnection;
    private ObservableCollection<TableInfo> _tables = new();
    private bool _isConnected;
    private bool _isMigrating;
    private string _statusMessage = "Pronto";
    private int _progressPercentage;
    private string _progressText = "0%";

    public ConnectionViewModel? SourceConnection
    {
        get => _sourceConnection;
        set => this.RaiseAndSetIfChanged(ref _sourceConnection, value);
    }

    public ConnectionViewModel? TargetConnection
    {
        get => _targetConnection;
        set => this.RaiseAndSetIfChanged(ref _targetConnection, value);
    }

    public ObservableCollection<TableInfo> Tables
    {
        get => _tables;
        set => this.RaiseAndSetIfChanged(ref _tables, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public bool IsMigrating
    {
        get => _isMigrating;
        set => this.RaiseAndSetIfChanged(ref _isMigrating, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        set => this.RaiseAndSetIfChanged(ref _progressPercentage, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    public ReactiveCommand<Unit, Unit> ConnectDatabasesCommand { get; }
    public ReactiveCommand<Unit, Unit> StartMigrationCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> DeselectAllTablesCommand { get; }

    public MainWindowViewModel()
    {
        _databaseService = new DatabaseService();
        _schemaMigrationService = new SchemaMigrationService();

        SourceConnection = new ConnectionViewModel();
        TargetConnection = new ConnectionViewModel();

        ConnectDatabasesCommand = ReactiveCommand.CreateFromTask(ConnectDatabasesAsync);
        StartMigrationCommand = ReactiveCommand.CreateFromTask(StartMigrationAsync, 
            this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating, 
                (connected, migrating) => connected && !migrating));
        SelectAllTablesCommand = ReactiveCommand.Create(SelectAllTables);
        DeselectAllTablesCommand = ReactiveCommand.Create(DeselectAllTables);
    }

    private async Task ConnectDatabasesAsync()
    {
        try
        {
            IsMigrating = true;
            StatusMessage = "Connessione ai database...";
            ProgressPercentage = 0;

            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                StatusMessage = "Errore: Compilare tutti i campi di connessione";
                return;
            }

            // Test connessione sorgente
            StatusMessage = "Test connessione sorgente...";
            bool sourceOk = await _databaseService.TestConnectionAsync(SourceConnection.ConnectionInfo);
            if (!sourceOk)
            {
                StatusMessage = "Errore: Impossibile connettersi al database sorgente";
                return;
            }

            ProgressPercentage = 33;

            // Test connessione target
            StatusMessage = "Test connessione target...";
            bool targetOk = await _databaseService.TestConnectionAsync(TargetConnection.ConnectionInfo);
            if (!targetOk)
            {
                StatusMessage = "Errore: Impossibile connettersi al database target";
                return;
            }

            ProgressPercentage = 66;

            // Recupera tabelle dalla sorgente
            StatusMessage = "Recupero tabelle dalla sorgente...";
            var tables = await _databaseService.GetTablesAsync(SourceConnection.ConnectionInfo);
            
            Tables.Clear();
            foreach (var table in tables)
            {
                Tables.Add(table);
            }

            ProgressPercentage = 100;
            IsConnected = true;
            StatusMessage = $"Connesso! Trovate {tables.Count} tabelle";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
        finally
        {
            IsMigrating = false;
        }
    }

    private async Task StartMigrationAsync()
    {
        try
        {
            IsMigrating = true;
            ProgressPercentage = 0;
            ProgressText = "0%";

            var tablesToMigrate = Tables.Where(t => t.IsSelected).ToList();
            if (tablesToMigrate.Count == 0)
            {
                StatusMessage = "Seleziona almeno una tabella da migrare";
                return;
            }

            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                StatusMessage = "Errore: Connessioni non valide";
                return;
            }

            // Verifica se database target esiste
            StatusMessage = "Verifica database target...";
            bool dbExists = await _databaseService.DatabaseExistsAsync(TargetConnection.ConnectionInfo);

            if (!dbExists)
            {
                StatusMessage = "Creazione database target...";
                await _databaseService.CreateDatabaseAsync(TargetConnection.ConnectionInfo);
                StatusMessage = "Database target creato";
            }

            ProgressPercentage = 10;

            // Migra lo schema
            StatusMessage = "Migrazione schema...";
            await _schemaMigrationService.MigrateSchemaAsync(
                SourceConnection.ConnectionInfo,
                TargetConnection.ConnectionInfo,
                tablesToMigrate);

            ProgressPercentage = 50;

            // Migra i dati
            StatusMessage = "Migrazione dati...";
            int tablesProcessed = 0;

            foreach (var table in tablesToMigrate)
            {
                StatusMessage = $"Migrazione dati: {table.Schema}.{table.TableName}...";
                
                var progress = new Progress<int>(percent =>
                {
                    int overall = 50 + (percent / (tablesToMigrate.Count * 2));
                    ProgressPercentage = overall;
                    ProgressText = $"{overall}% - {table.TableName}";
                });

                await _databaseService.MigrateTableAsync(
                    SourceConnection.ConnectionInfo,
                    TargetConnection.ConnectionInfo,
                    table,
                    progress);

                tablesProcessed++;
                ProgressPercentage = 50 + (tablesProcessed * 50 / tablesToMigrate.Count);
            }

            ProgressPercentage = 100;
            ProgressText = "100%";
            StatusMessage = $"Migrazione completata! {tablesToMigrate.Count} tabelle migrate";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore migrazione: {ex.Message}";
            ProgressPercentage = 0;
        }
        finally
        {
            IsMigrating = false;
        }
    }

    private void SelectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = true;
        }
    }

    private void DeselectAllTables()
    {
        foreach (var table in Tables)
        {
            table.IsSelected = false;
        }
    }
}
