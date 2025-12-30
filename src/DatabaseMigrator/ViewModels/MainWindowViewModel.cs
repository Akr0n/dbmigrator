using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reactive.Linq;

namespace DatabaseMigrator.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly SchemaMigrationService _schemaMigrationService;

    private static void Log(string message) => LoggerService.Log(message);

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
    private bool _canStartMigration;
    private MigrationMode _selectedMigrationMode = MigrationMode.SchemaAndData;
    private string _tableSearchFilter = "";
    private ObservableCollection<TableInfo> _filteredTables = new();
    private ObservableCollection<TableInfo> _filteredTargetTables = new();
    private ObservableCollection<TableInfo> _selectedTablesForMigration = new();

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

    public ObservableCollection<TableInfo> FilteredTables
    {
        get => _filteredTables;
        set => this.RaiseAndSetIfChanged(ref _filteredTables, value);
    }

    public ObservableCollection<TableInfo> FilteredTargetTables
    {
        get => _filteredTargetTables;
        set => this.RaiseAndSetIfChanged(ref _filteredTargetTables, value);
    }

    public ObservableCollection<TableInfo> SelectedTablesForMigration
    {
        get => _selectedTablesForMigration;
        set => this.RaiseAndSetIfChanged(ref _selectedTablesForMigration, value);
    }

    public string TableSearchFilter
    {
        get => _tableSearchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _tableSearchFilter, value);
            ApplyTableFilter();
        }
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

    public bool CanStartMigration
    {
        get => _canStartMigration;
        set => this.RaiseAndSetIfChanged(ref _canStartMigration, value);
    }

    public MigrationMode SelectedMigrationMode
    {
        get => _selectedMigrationMode;
        set => this.RaiseAndSetIfChanged(ref _selectedMigrationMode, value);
    }

    public IObservable<bool> CanStartMigrationObservable { get; }

    public ReactiveCommand<Unit, Unit> ConnectDatabasesCommand { get; }
    public ReactiveCommand<Unit, Unit> StartMigrationCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> DeselectAllTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshTablesCommand { get; }

    public MainWindowViewModel()
    {
        _databaseService = new DatabaseService();
        _schemaMigrationService = new SchemaMigrationService();

        SourceConnection = new ConnectionViewModel();
        TargetConnection = new ConnectionViewModel();

        // Inizializza CanStartMigration al valore corretto
        CanStartMigration = IsConnected && !IsMigrating;

        // Observable per CanStartMigration
        CanStartMigrationObservable = this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
            (connected, migrating) => connected && !migrating)
            .Do(canStart => CanStartMigration = canStart);

        ConnectDatabasesCommand = ReactiveCommand.CreateFromTask(ConnectDatabasesAsync);
        StartMigrationCommand = ReactiveCommand.CreateFromTask(StartMigrationAsync, 
            this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating, 
                (connected, migrating) => connected && !migrating));
        SelectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => Task.Run(() => SelectAllTablesDirectly()));
        DeselectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => Task.Run(() => DeselectAllTablesDirectly()));
        RefreshTablesCommand = ReactiveCommand.CreateFromTask(RefreshTablesAsync,
            this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
                (connected, migrating) => connected && !migrating));
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

            // Inizializza le collezioni filtrate
            ApplyTableFilter();
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
                var usedPassword = await _databaseService.CreateDatabaseAsync(TargetConnection.ConnectionInfo);
                
                // Se è Oracle, aggiorna sia username che password per la nuova connessione
                if (TargetConnection.ConnectionInfo.DatabaseType == DatabaseType.Oracle)
                {
                    // Lo username diventa lo schema/database che abbiamo creato
                    var schemaName = TargetConnection.ConnectionInfo.Database;
                    TargetConnection.ConnectionInfo.Username = schemaName;
                    
                    // Aggiorna anche la password se è stata escappata
                    if (!string.IsNullOrWhiteSpace(usedPassword))
                    {
                        TargetConnection.ConnectionInfo.Password = usedPassword;
                    }
                    
                    Log($"[StartMigrationAsync] Updated target connection for Oracle: Username={schemaName}, Password updated");
                }
                
                Log($"[StartMigrationAsync] Database created successfully");
                StatusMessage = "Database target creato";
            }

            ProgressPercentage = 10;

            // Migrate schema if needed
            if (SelectedMigrationMode == MigrationMode.SchemaAndData || SelectedMigrationMode == MigrationMode.SchemaOnly)
            {
                Log($"[StartMigrationAsync] Starting schema migration (Mode: {SelectedMigrationMode})...");
                StatusMessage = "Migrazione schema...";
                await _schemaMigrationService.MigrateSchemaAsync(
                    SourceConnection.ConnectionInfo,
                    TargetConnection.ConnectionInfo,
                    tablesToMigrate);
                Log($"[StartMigrationAsync] Schema migration completed");
            }
            else
            {
                Log($"[StartMigrationAsync] Skipping schema migration (Mode: {SelectedMigrationMode})");
            }

            // For SchemaOnly mode, schema represents 100% of the work
            // For other modes, schema represents 50% (data migration is the other 50%)
            if (SelectedMigrationMode == MigrationMode.SchemaOnly)
            {
                ProgressPercentage = 100;
                ProgressText = "100% - Migrazione schema completata";
            }
            else
            {
                ProgressPercentage = 50;
            }

            // Migrate data if needed
            if (SelectedMigrationMode == MigrationMode.SchemaAndData || SelectedMigrationMode == MigrationMode.DataOnly)
            {
                // For DataOnly mode: validate that all tables exist in the target database before starting
                if (SelectedMigrationMode == MigrationMode.DataOnly)
                {
                    Log($"[StartMigrationAsync] Validating table existence in target database (DataOnly mode)...");
                    StatusMessage = "Verifica esistenza tabelle nel database di destinazione...";
                    
                    var missingTables = new System.Collections.Concurrent.ConcurrentBag<string>();
                    // Limit the number of concurrent table existence checks to avoid overloading the database
                    using (var semaphore = new System.Threading.SemaphoreSlim(10))
                    {
                        var validationTasks = tablesToMigrate.Select(async table =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                bool exists = await _schemaMigrationService.CheckTableExistsAsync(
                                    TargetConnection.ConnectionInfo, table.Schema, table.TableName);

                                if (!exists)
                                {
                                    missingTables.Add($"{table.Schema}.{table.TableName}");
                                    Log($"[StartMigrationAsync] Table {table.Schema}.{table.TableName} does not exist in target database");
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        await System.Threading.Tasks.Task.WhenAll(validationTasks);
                    }
                    
                    if (missingTables.Count > 0)
                    {
                        var missingTablesList = missingTables.ToList();
                        string missingList = string.Join(", ", missingTablesList.Take(5));
                        if (missingTablesList.Count > 5)
                            missingList += $" e altre {missingTablesList.Count - 5} tabelle";
                        
                        throw new InvalidOperationException(
                            $"Modalità 'Solo Dati' selezionata ma {missingTablesList.Count} tabella/e non esistono nel database di destinazione: {missingList}. " +
                            "Usare 'Schema + Dati' o 'Solo Schema' per creare prima le tabelle.");
                    }
                    
                    Log($"[StartMigrationAsync] All {tablesToMigrate.Count} tables exist in target database");
                }

                Log($"[StartMigrationAsync] Starting data migration (Mode: {SelectedMigrationMode})...");
                StatusMessage = "Migrazione dati...";
                int tablesProcessed = 0;

                foreach (var table in tablesToMigrate)
                {
                    Log($"[StartMigrationAsync] Migrating table {table.Schema}.{table.TableName}...");
                    StatusMessage = $"Migrazione dati: {table.Schema}.{table.TableName}...";
                    
                    var progress = new Progress<int>(percent =>
                    {
                        // Progress is calculated based on tablesProcessed
                    });

                    await _databaseService.MigrateTableAsync(
                        SourceConnection.ConnectionInfo,
                        TargetConnection.ConnectionInfo,
                        table,
                        progress);

                    Log($"[StartMigrationAsync] Table {table.Schema}.{table.TableName} migration completed");
                    tablesProcessed++;
                    int progressPercent = SelectedMigrationMode == MigrationMode.DataOnly
                        ? 10 + (tablesProcessed * 90 / tablesToMigrate.Count)
                        : 50 + (tablesProcessed * 50 / tablesToMigrate.Count);
                    ProgressPercentage = progressPercent;
                    ProgressText = $"{progressPercent}% - {table.TableName}";
                }
                
                // Set final progress to 100% with generic text for modes that include data migration
                ProgressPercentage = 100;
                ProgressText = "100%";
            }
            else
            {
                Log($"[StartMigrationAsync] Skipping data migration (Mode: {SelectedMigrationMode})");
            }

            Log($"[StartMigrationAsync] Migration completed successfully!");
            ErrorMessage = "";
            
            string modeDescription = SelectedMigrationMode switch
            {
                MigrationMode.SchemaOnly => "schema",
                MigrationMode.DataOnly => "dati",
                _ => "schema e dati"
            };
            StatusMessage = $"✅ Migrazione completata! {tablesToMigrate.Count} tabelle ({modeDescription})";
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

    public void SelectAllTablesDirectly()
    {
        SetAllTablesSelection(true);
    }

    public void DeselectAllTablesDirectly()
    {
        SetAllTablesSelection(false);
    }

    /// <summary>
    /// Sets the selection state for all tables.
    /// </summary>
    /// <param name="isSelected">True to select all, false to deselect all.</param>
    private void SetAllTablesSelection(bool isSelected)
    {
        string operation = isSelected ? "SelectAll" : "DeselectAll";
        Log($"[{operation}TablesDirectly] Starting... Total tables: {Tables.Count}");
        
        foreach (var table in Tables.ToList())
        {
            Log($"[{operation}TablesDirectly] Setting {table.Schema}.{table.TableName} to IsSelected={isSelected}");
            table.IsSelected = isSelected;
        }
        
        // Force UI refresh by reassigning the collection
        Log($"[{operation}TablesDirectly] Forcing collection refresh...");
        var newCollection = new ObservableCollection<TableInfo>(Tables);
        Tables = newCollection;
        
        // Aggiorna anche le collezioni filtrate
        ApplyTableFilter();
        UpdateTableStatistics();
        Log($"[{operation}TablesDirectly] Completed. SelectedTablesCount={SelectedTablesCount}");
    }

    private void UpdateTableStatistics()
    {
        SelectedTablesCount = Tables.Count(t => t.IsSelected);
        TotalRowsToMigrate = Tables.Where(t => t.IsSelected).Sum(t => t.RowCount);
        
        // Aggiorna la lista delle tabelle selezionate per la tab Migrazione (senza filtro)
        SelectedTablesForMigration = new ObservableCollection<TableInfo>(Tables.Where(t => t.IsSelected));
        
        // Aggiorna FilteredTargetTables per mostrare solo le tabelle selezionate (con filtro applicato)
        if (string.IsNullOrWhiteSpace(TableSearchFilter))
        {
            FilteredTargetTables = new ObservableCollection<TableInfo>(Tables.Where(t => t.IsSelected));
        }
        else
        {
            var filter = TableSearchFilter.ToLowerInvariant();
            FilteredTargetTables = new ObservableCollection<TableInfo>(
                Tables.Where(t => t.IsSelected && 
                    (t.TableName.ToLowerInvariant().Contains(filter) ||
                     t.Schema.ToLowerInvariant().Contains(filter) ||
                     $"{t.Schema}.{t.TableName}".ToLowerInvariant().Contains(filter))));
        }
        
        Log($"[UpdateTableStatistics] SelectedCount={SelectedTablesCount}, TotalRows={TotalRowsToMigrate}");
    }

    /// <summary>
    /// Applica il filtro di ricerca alle tabelle
    /// </summary>
    private void ApplyTableFilter()
    {
        Log($"[ApplyTableFilter] Applying filter: '{TableSearchFilter}'");
        
        if (string.IsNullOrWhiteSpace(TableSearchFilter))
        {
            FilteredTables = new ObservableCollection<TableInfo>(Tables);
            FilteredTargetTables = new ObservableCollection<TableInfo>(Tables.Where(t => t.IsSelected));
        }
        else
        {
            var filter = TableSearchFilter.ToLowerInvariant();
            var filtered = Tables.Where(t => 
                t.TableName.ToLowerInvariant().Contains(filter) ||
                t.Schema.ToLowerInvariant().Contains(filter) ||
                $"{t.Schema}.{t.TableName}".ToLowerInvariant().Contains(filter));
            
            FilteredTables = new ObservableCollection<TableInfo>(filtered);
            FilteredTargetTables = new ObservableCollection<TableInfo>(filtered.Where(t => t.IsSelected));
        }
        
        Log($"[ApplyTableFilter] Filtered tables count: {FilteredTables.Count}");
    }

    /// <summary>
    /// Ricarica le tabelle dal database sorgente
    /// </summary>
    public async Task RefreshTablesAsync()
    {
        try
        {
            Log("[RefreshTablesAsync] Starting tables refresh...");
            StatusMessage = "Ricaricamento tabelle...";
            
            if (SourceConnection?.ConnectionInfo == null)
            {
                ErrorMessage = "Errore: Connessione sorgente non valida";
                StatusMessage = "Errore: connessione non valida";
                return;
            }

            // Salva le selezioni correnti
            var selectedTables = Tables
                .Where(t => t.IsSelected)
                .Select(t => $"{t.Schema}.{t.TableName}")
                .ToHashSet();
            
            Log($"[RefreshTablesAsync] Preserving {selectedTables.Count} selected tables");

            // Ricarica le tabelle dal database
            var tables = await _databaseService.GetTablesAsync(SourceConnection.ConnectionInfo);
            
            Tables.Clear();
            foreach (var table in tables)
            {
                // Ripristina la selezione se la tabella era selezionata
                if (selectedTables.Contains($"{table.Schema}.{table.TableName}"))
                {
                    table.IsSelected = true;
                }
                Tables.Add(table);
                SubscribeToTableChanges(table);
            }

            // Applica il filtro corrente
            ApplyTableFilter();
            UpdateTableStatistics();

            StatusMessage = $"✅ Tabelle ricaricate! Trovate {tables.Count} tabelle";
            Log($"[RefreshTablesAsync] Refresh completed. {tables.Count} tables loaded");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"❌ Errore nel ricaricamento: {ex.Message}";
            StatusMessage = "Errore durante il ricaricamento";
            Log($"[RefreshTablesAsync] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Metodo pubblico per ricaricare le tabelle (chiamabile dalla UI)
    /// </summary>
    public void RefreshTablesDirectly()
    {
        _ = RefreshTablesAsync();
    }

    /// <summary>
    /// Salva la configurazione corrente in un file JSON
    /// </summary>
    public async Task<bool> SaveConfigurationAsync(string filePath)
    {
        try
        {
            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                ErrorMessage = "Errore: configurazioni di connessione non complete";
                Log("[SaveConfigurationAsync] Errore: configurazioni incomplete");
                return false;
            }

            var config = new ConnectionConfig
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Source = DatabaseConnectionData.FromConnectionInfo(SourceConnection.ConnectionInfo),
                Target = DatabaseConnectionData.FromConnectionInfo(TargetConnection.ConnectionInfo),
                Timestamp = DateTime.Now
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(filePath, json);

            StatusMessage = $"Configurazione salvata: {Path.GetFileName(filePath)}";
            Log($"[SaveConfigurationAsync] Configurazione salvata in {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Errore nel salvataggio: {ex.Message}";
            Log($"[SaveConfigurationAsync] Errore: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Carica una configurazione da file JSON
    /// </summary>
    public async Task<bool> LoadConfigurationAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                ErrorMessage = $"File non trovato: {filePath}";
                Log("[LoadConfigurationAsync] File non trovato");
                return false;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = JsonSerializer.Deserialize<ConnectionConfig>(json, options);
            if (config?.Source == null || config.Target == null)
            {
                ErrorMessage = "Errore: configurazione non valida";
                Log("[LoadConfigurationAsync] Errore: configurazione non valida");
                return false;
            }

            // Carica source connection
            var sourceInfo = config.Source.ToConnectionInfo();
            SourceConnection = new ConnectionViewModel
            {
                Server = sourceInfo.Server,
                Port = sourceInfo.Port,
                Database = sourceInfo.Database,
                Username = sourceInfo.Username,
                Password = sourceInfo.Password,
                SelectedDatabaseType = sourceInfo.DatabaseType
            };

            // Carica target connection
            var targetInfo = config.Target.ToConnectionInfo();
            TargetConnection = new ConnectionViewModel
            {
                Server = targetInfo.Server,
                Port = targetInfo.Port,
                Database = targetInfo.Database,
                Username = targetInfo.Username,
                Password = targetInfo.Password,
                SelectedDatabaseType = targetInfo.DatabaseType
            };

            StatusMessage = $"Configurazione caricata: {Path.GetFileName(filePath)}";
            Log($"[LoadConfigurationAsync] Configurazione caricata da {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Errore nel caricamento: {ex.Message}";
            Log($"[LoadConfigurationAsync] Errore: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ottiene la directory predefinita per i file di configurazione
    /// </summary>
    public static string GetConfigDirectory()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseMigrator", "Configurations");
        
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);
        
        return configDir;
    }
}
