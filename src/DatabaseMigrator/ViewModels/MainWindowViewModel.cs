using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using System.IO;

namespace DatabaseMigrator.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly SchemaMigrationService _schemaMigrationService;
    
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatabaseMigrator", "debug.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            File.AppendAllText(LogPath, fullMessage + "\n");
            System.Diagnostics.Debug.WriteLine(fullMessage);
        }
        catch { }
    }

    private ConnectionViewModel? _sourceConnection;
    private ConnectionViewModel? _targetConnection;
    private ObservableCollection<TableInfo> _tables = new();
    private bool _isConnected;
    private bool _isMigrating;
    private string _statusMessage = "Pronto";
    private int _progressPercentage;
    private string _progressText = "0%";
    private string _errorMessage = "";
    private int _selectedTablesCount;
    private long _totalRowsToMigrate;

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

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public int SelectedTablesCount
    {
        get => _selectedTablesCount;
        set => this.RaiseAndSetIfChanged(ref _selectedTablesCount, value);
    }

    public long TotalRowsToMigrate
    {
        get => _totalRowsToMigrate;
        set => this.RaiseAndSetIfChanged(ref _totalRowsToMigrate, value);
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
        SelectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => SelectAllTablesAsync());
        DeselectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => DeselectAllTablesAsync());
    }

    private void SubscribeToTableChanges(TableInfo table)
    {
        table.WhenAnyValue(t => t.IsSelected)
            .Subscribe(_ => UpdateTableStatistics());
    }

    private async Task ConnectDatabasesAsync()
    {
        try
        {
            IsMigrating = true;
            ErrorMessage = "";
            StatusMessage = "Connessione ai database...";
            ProgressPercentage = 0;

            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                ErrorMessage = "Errore: Compilare Server e Database";
                StatusMessage = "Errore di validazione";
                System.Diagnostics.Debug.WriteLine($"Source ConnectionInfo: {SourceConnection?.ConnectionInfo}");
                System.Diagnostics.Debug.WriteLine($"Target ConnectionInfo: {TargetConnection?.ConnectionInfo}");
                System.Diagnostics.Debug.WriteLine($"Source Connection - Server: {SourceConnection?.Server}, DB: {SourceConnection?.Database}");
                System.Diagnostics.Debug.WriteLine($"Target Connection - Server: {TargetConnection?.Server}, DB: {TargetConnection?.Database}");
                return;
            }

            // Test connessione sorgente
            StatusMessage = "Test connessione sorgente...";
            ProgressPercentage = 20;
            System.Diagnostics.Debug.WriteLine($"Testing source connection: {SourceConnection.ConnectionInfo.GetConnectionString()}");
            bool sourceOk = await _databaseService.TestConnectionAsync(SourceConnection.ConnectionInfo);
            if (!sourceOk)
            {
                ErrorMessage = "Errore: Impossibile connettersi al database sorgente. Verifica server, porta e credenziali.";
                StatusMessage = "Connessione sorgente fallita";
                return;
            }

            ProgressPercentage = 40;

            // Test connessione target
            StatusMessage = "Test connessione target...";
            System.Diagnostics.Debug.WriteLine($"Testing target connection: {TargetConnection.ConnectionInfo.GetConnectionString()}");
            bool targetOk = await _databaseService.TestConnectionAsync(TargetConnection.ConnectionInfo);
            if (!targetOk)
            {
                ErrorMessage = "Errore: Impossibile connettersi al database target. Verifica server, porta e credenziali.";
                StatusMessage = "Connessione target fallita";
                return;
            }

            ProgressPercentage = 60;

            // Recupera tabelle dalla sorgente
            StatusMessage = "Recupero tabelle dalla sorgente...";
            var tables = await _databaseService.GetTablesAsync(SourceConnection.ConnectionInfo);
            
            Tables.Clear();
            foreach (var table in tables)
            {
                Tables.Add(table);
                SubscribeToTableChanges(table);
            }

            UpdateTableStatistics();

            ProgressPercentage = 100;
            IsConnected = true;
            ErrorMessage = "";
            StatusMessage = $"✅ Connesso! Trovate {tables.Count} tabelle";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"❌ Errore: {ex.Message}";
            StatusMessage = "Errore durante la connessione";
            IsConnected = false;
            ProgressPercentage = 0;
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
            Log($"[StartMigrationAsync] Starting migration...");
            IsMigrating = true;
            ErrorMessage = "";
            ProgressPercentage = 0;
            ProgressText = "0%";

            var tablesToMigrate = Tables.Where(t => t.IsSelected).ToList();
            Log($"[StartMigrationAsync] Tables to migrate: {tablesToMigrate.Count}");
            
            if (tablesToMigrate.Count == 0)
            {
                ErrorMessage = "Seleziona almeno una tabella da migrare";
                StatusMessage = "Nessuna tabella selezionata";
                return;
            }

            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                ErrorMessage = "Errore: Connessioni non valide";
                StatusMessage = "Connessioni invalide";
                return;
            }

            // Verifica se database target esiste
            Log($"[StartMigrationAsync] Checking if target database exists...");
            StatusMessage = "Verifica database target...";
            bool dbExists = await _databaseService.DatabaseExistsAsync(TargetConnection.ConnectionInfo);
            Log($"[StartMigrationAsync] Database exists: {dbExists}");

            if (!dbExists)
            {
                Log($"[StartMigrationAsync] Creating target database...");
                StatusMessage = "Creazione database target...";
                await _databaseService.CreateDatabaseAsync(TargetConnection.ConnectionInfo);
                Log($"[StartMigrationAsync] Database created successfully");
                StatusMessage = "Database target creato";
            }

            ProgressPercentage = 10;

            // Migra lo schema
            Log($"[StartMigrationAsync] Starting schema migration...");
            StatusMessage = "Migrazione schema...";
            await _schemaMigrationService.MigrateSchemaAsync(
                SourceConnection.ConnectionInfo,
                TargetConnection.ConnectionInfo,
                tablesToMigrate);
            Log($"[StartMigrationAsync] Schema migration completed");

            ProgressPercentage = 50;

            // Migra i dati
            Log($"[StartMigrationAsync] Starting data migration...");
            StatusMessage = "Migrazione dati...";
            int tablesProcessed = 0;

            foreach (var table in tablesToMigrate)
            {
                Log($"[StartMigrationAsync] Migrating table {table.Schema}.{table.TableName}...");
                StatusMessage = $"Migrazione dati: {table.Schema}.{table.TableName}...";
                
                var progress = new Progress<int>(percent =>
                {
                    // Ignoriamo i report interno e usiamo il calcolo basato su tablesProcessed
                    // Il 50% proviene dalla schema migration, il restante 50% dalla data migration
                });

                await _databaseService.MigrateTableAsync(
                    SourceConnection.ConnectionInfo,
                    TargetConnection.ConnectionInfo,
                    table,
                    progress);

                Log($"[StartMigrationAsync] Table {table.Schema}.{table.TableName} migration completed");
                tablesProcessed++;
                int progressPercent = 50 + (tablesProcessed * 50 / tablesToMigrate.Count);
                ProgressPercentage = progressPercent;
                ProgressText = $"{progressPercent}% - {table.TableName}";
            }

            Log($"[StartMigrationAsync] Migration completed successfully!");
            ProgressPercentage = 100;
            ProgressText = "100%";
            ErrorMessage = "";
            StatusMessage = $"✅ Migrazione completata! {tablesToMigrate.Count} tabelle migrate";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            Log($"[StartMigrationAsync] ERROR: {ex.Message}");
            Log($"[StartMigrationAsync] Stack trace: {ex.StackTrace}");
            ErrorMessage = $"❌ Errore migrazione: {ex.Message}";
            StatusMessage = "Migrazione fallita";
            ProgressPercentage = 0;
        }
        finally
        {
            IsMigrating = false;
        }
    }

    private async Task SelectAllTablesAsync()
    {
        Log($"[SelectAllTables] Starting... Total tables: {Tables.Count}");
        foreach (var table in Tables)
        {
            Log($"[SelectAllTables] Setting {table.Schema}.{table.TableName} to IsSelected=true");
            table.IsSelected = true;
            Log($"[SelectAllTables] After setting: IsSelected={table.IsSelected}");
        }
        
        // Force UI refresh by reassigning the collection
        Log($"[SelectAllTables] Forcing collection refresh...");
        var newCollection = new ObservableCollection<TableInfo>(Tables);
        Tables = newCollection;
        
        UpdateTableStatistics();
        Log($"[SelectAllTables] Completed. SelectedTablesCount={SelectedTablesCount}");
    }

    private async Task DeselectAllTablesAsync()
    {
        Log($"[DeselectAllTables] Starting... Total tables: {Tables.Count}");
        foreach (var table in Tables)
        {
            Log($"[DeselectAllTables] Setting {table.Schema}.{table.TableName} to IsSelected=false");
            table.IsSelected = false;
            Log($"[DeselectAllTables] After setting: IsSelected={table.IsSelected}");
        }
        
        // Force UI refresh by reassigning the collection
        Log($"[DeselectAllTables] Forcing collection refresh...");
        var newCollection = new ObservableCollection<TableInfo>(Tables);
        Tables = newCollection;
        
        UpdateTableStatistics();
        Log($"[DeselectAllTables] Completed. SelectedTablesCount={SelectedTablesCount}");
    }

    public void SelectAllTablesDirectly()
    {
        Log($"[SelectAllTablesDirectly] Starting... Total tables: {Tables.Count}");
        foreach (var table in Tables.ToList())
        {
            Log($"[SelectAllTablesDirectly] Setting {table.Schema}.{table.TableName} to IsSelected=true");
            table.IsSelected = true;
        }
        
        // Force UI refresh by reassigning the collection
        Log($"[SelectAllTablesDirectly] Forcing collection refresh...");
        var newCollection = new ObservableCollection<TableInfo>(Tables);
        Tables = newCollection;
        
        UpdateTableStatistics();
        Log($"[SelectAllTablesDirectly] Completed. SelectedTablesCount={SelectedTablesCount}");
    }

    public void DeselectAllTablesDirectly()
    {
        Log($"[DeselectAllTablesDirectly] Starting... Total tables: {Tables.Count}");
        foreach (var table in Tables.ToList())
        {
            Log($"[DeselectAllTablesDirectly] Setting {table.Schema}.{table.TableName} to IsSelected=false");
            table.IsSelected = false;
        }
        
        // Force UI refresh by reassigning the collection
        Log($"[DeselectAllTablesDirectly] Forcing collection refresh...");
        var newCollection = new ObservableCollection<TableInfo>(Tables);
        Tables = newCollection;
        
        UpdateTableStatistics();
        Log($"[DeselectAllTablesDirectly] Completed. SelectedTablesCount={SelectedTablesCount}");
    }

    private void UpdateTableStatistics()
    {
        SelectedTablesCount = Tables.Count(t => t.IsSelected);
        TotalRowsToMigrate = Tables.Where(t => t.IsSelected).Sum(t => t.RowCount);
        Log($"[UpdateTableStatistics] SelectedCount={SelectedTablesCount}, TotalRows={TotalRowsToMigrate}");
    }
}
