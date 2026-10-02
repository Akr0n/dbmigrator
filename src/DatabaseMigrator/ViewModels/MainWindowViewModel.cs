using System;
using System.Collections.Generic;
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
using Avalonia.Threading;
using Avalonia.Media;

namespace DatabaseMigrator.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly SchemaMigrationService _schemaMigrationService;
    private readonly ForeignKeyService _foreignKeyService;

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
    private bool _isRefreshingTables;  // Guards re-entrancy and UI recomputations during refresh
    private bool _suppressTableSelectionUpdates; // Avoids noisy re-entrancy during bulk selection updates
    private readonly Dictionary<TableInfo, IDisposable> _tableSubscriptions = new();  // Track subscriptions for cleanup

    // Log tab
    private const int MaxLogEntries = 5000;
    private readonly List<LogEntry> _allLogEntries = new();
    private bool _showOnlyErrors;
    private int _logErrorCount;

    // Connection status indicators
    private string _sourceStatusText = "";
    private string _targetStatusText = "";
    private IBrush _sourceStatusBrush = Brushes.Transparent;
    private IBrush _targetStatusBrush = Brushes.Transparent;
    private string _connectionSummary = "";

    /// <summary>ViewModel del tab "Genera Script" (export DDL + dati su file .sql).</summary>
    public ScriptGenerationViewModel ScriptGeneration { get; } = new();

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
        // A running migration reads the mode at every step: changing it mid-run would add or skip steps (say, load and
        // TRUNCATE every selected table in a run started as schema only). The radio buttons are disabled meanwhile too.
        set
        {
            if (!IsMigrating)
                this.RaiseAndSetIfChanged(ref _selectedMigrationMode, value);
        }
    }

    public IObservable<bool> CanStartMigrationObservable { get; }

    public ReactiveCommand<Unit, Unit> ConnectDatabasesCommand { get; }
    public ReactiveCommand<Unit, Unit> StartMigrationCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> DeselectAllTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshTablesCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearLogCommand { get; }

    // Log tab — backed by _allLogEntries (all) and FilteredLogEntries (displayed)
    public ObservableCollection<LogEntry> FilteredLogEntries { get; } = new();

    public bool ShowOnlyErrors
    {
        get => _showOnlyErrors;
        set
        {
            this.RaiseAndSetIfChanged(ref _showOnlyErrors, value);
            RebuildFilteredLog();
        }
    }

    public int LogErrorCount
    {
        get => _logErrorCount;
        set => this.RaiseAndSetIfChanged(ref _logErrorCount, value);
    }

    public string SourceStatusText
    {
        get => _sourceStatusText;
        set => this.RaiseAndSetIfChanged(ref _sourceStatusText, value);
    }

    public string TargetStatusText
    {
        get => _targetStatusText;
        set => this.RaiseAndSetIfChanged(ref _targetStatusText, value);
    }

    public IBrush SourceStatusBrush
    {
        get => _sourceStatusBrush;
        set => this.RaiseAndSetIfChanged(ref _sourceStatusBrush, value);
    }

    public IBrush TargetStatusBrush
    {
        get => _targetStatusBrush;
        set => this.RaiseAndSetIfChanged(ref _targetStatusBrush, value);
    }

    public string ConnectionSummary
    {
        get => _connectionSummary;
        set => this.RaiseAndSetIfChanged(ref _connectionSummary, value);
    }

    public MainWindowViewModel()
        : this(null, null)
    {
    }

    public MainWindowViewModel(IDatabaseService? databaseService, SchemaMigrationService? schemaMigrationService,
        ForeignKeyService? foreignKeyService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
        _schemaMigrationService = schemaMigrationService ?? new SchemaMigrationService();
        _foreignKeyService = foreignKeyService ?? new ForeignKeyService();

        // Wire TRUNCATE-failed prompt handler into the concrete database service (if applicable).
        if (_databaseService is DatabaseService dbService)
        {
            dbService.TruncateFailedHandlerAsync = async ctx =>
            {
                // Ensure dialog callbacks run on UI thread.
                return await Dispatcher.UIThread.InvokeAsync(() =>
                    TruncateFailedPromptHandlerAsync?.Invoke(ctx) ?? Task.FromResult(true)
                );
            };
        }

        SourceConnection = new ConnectionViewModel();
        TargetConnection = new ConnectionViewModel();
        
        // Initialize filtered collections
        _filteredTables = new ObservableCollection<TableInfo>();
        _filteredTargetTables = new ObservableCollection<TableInfo>();
        _selectedTablesForMigration = new ObservableCollection<TableInfo>();

        // Inizializza CanStartMigration al valore corretto
        CanStartMigration = IsConnected && !IsMigrating;

        // Observable per CanStartMigration
        CanStartMigrationObservable = this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
            (connected, migrating) => connected && !migrating)
            .Do(canStart => CanStartMigration = canStart);

        ConnectDatabasesCommand = ReactiveCommand.CreateFromTask(ConnectDatabasesAsync);
        ConnectDatabasesCommand.ThrownExceptions.Subscribe(ex =>
            LoggerService.LogError("ConnectDatabasesCommand unhandled exception", ex));
        StartMigrationCommand = ReactiveCommand.CreateFromTask(StartMigrationAsync,
            this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
                (connected, migrating) => connected && !migrating));
        SelectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => SetAllTablesSelectionAsync(true));
        DeselectAllTablesCommand = ReactiveCommand.CreateFromTask(_ => SetAllTablesSelectionAsync(false));
        RefreshTablesCommand = ReactiveCommand.CreateFromTask(RefreshTablesAsync,
            this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
                (connected, migrating) => connected && !migrating));

        ClearLogCommand = ReactiveCommand.Create(() =>
        {
            _allLogEntries.Clear();
            FilteredLogEntries.Clear();
            LogErrorCount = 0;
        });

        // Subscribe to real-time log events
        LoggerService.MessageLogged += OnLogMessageReceived;
    }

    /// <summary>
    /// Optional handler that shows a UI confirmation when emptying a target table fails or is refused (TRUNCATE / DELETE, or
    /// the PostgreSQL / Oracle pre-check that it would also empty populated tables the run does not load).
    /// Returns true to continue inserting, false to abort migration.
    /// </summary>
    public Func<TruncateFailureContext, Task<bool>>? TruncateFailedPromptHandlerAsync { get; set; }

    /// <summary>
    /// Optional handler asked before a migration starts when some selected tables are hidden by the search filter.
    /// Receives (selected tables, how many of them are hidden, whether their data in the target will be replaced)
    /// and returns true to go ahead or false to cancel.
    /// </summary>
    public Func<int, int, bool, Task<bool>>? ConfirmHiddenTablesAsync { get; set; }

    private void OnLogMessageReceived(LogEntry entry)
    {
        // Called from any thread; dispatch to UI thread for collection updates.
        Dispatcher.UIThread.Post(() =>
        {
            if (_allLogEntries.Count >= MaxLogEntries)
                _allLogEntries.RemoveAt(0);

            _allLogEntries.Add(entry);

            if (entry.Level == LogLevel.Error)
                LogErrorCount++;

            if (!_showOnlyErrors || entry.Level == LogLevel.Error)
                FilteredLogEntries.Add(entry);
        });
    }

    private void RebuildFilteredLog()
    {
        FilteredLogEntries.Clear();
        var source = _showOnlyErrors
            ? _allLogEntries.Where(e => e.Level == LogLevel.Error)
            : (IEnumerable<LogEntry>)_allLogEntries;
        foreach (var entry in source)
            FilteredLogEntries.Add(entry);
    }

    private void SubscribeToTableChanges(TableInfo table)
    {
        // Dispose existing subscription if any
        if (_tableSubscriptions.TryGetValue(table, out var existingSubscription))
        {
            existingSubscription.Dispose();
            _tableSubscriptions.Remove(table);
        }

        // Skip(1) drops the synchronous emission that WhenAnyValue fires on subscribe —
        // without it, each of N subscribed tables queues one RecomputeTableViews on load.
        var subscription = table.WhenAnyValue(t => t.IsSelected)
            .Skip(1)
            .Subscribe(_ =>
            {
                // Synchronous suppression check: bulk operations (load, Select/Deselect All)
                // reset _suppressTableSelectionUpdates before the posted continuation runs,
                // so checking only inside the Post lets every change leak through.
                if (_isRefreshingTables || _suppressTableSelectionUpdates)
                {
                    return;
                }

                // Must run on Avalonia UI thread - RxApp.MainThreadScheduler may not be configured for Avalonia
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_isRefreshingTables && !_suppressTableSelectionUpdates)
                    {
                        RecomputeTableViews();
                    }
                });
            });
        _tableSubscriptions[table] = subscription;
    }
    
    /// <summary>
    /// Disposes all table subscriptions to prevent memory leaks and race conditions during refresh.
    /// </summary>
    private void DisposeAllTableSubscriptions()
    {
        foreach (var subscription in _tableSubscriptions.Values)
        {
            subscription.Dispose();
        }
        _tableSubscriptions.Clear();
    }

    private void RecomputeTableViews(bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("RecomputeTableViews must run on the UI thread.");
        }

        if (_isRefreshingTables && !force)
        {
            return;
        }

        var selectedTables = Tables.Where(t => t.IsSelected).ToList();
        SelectedTablesCount = selectedTables.Count;
        TotalRowsToMigrate = selectedTables.Sum(t => t.RowCount);
        SelectedTablesForMigration = new ObservableCollection<TableInfo>(selectedTables);

        var filteredList = TableSelection.Visible(Tables, TableSearchFilter);
        FilteredTables = new ObservableCollection<TableInfo>(filteredList);
        FilteredTargetTables = new ObservableCollection<TableInfo>(filteredList.Where(t => t.IsSelected));

        Log($"[RecomputeTableViews] Filtered={FilteredTables.Count}, Selected={SelectedTablesCount}, TotalRows={TotalRowsToMigrate}");
    }

    /// <param name="previousTables">
    /// The tables currently on screen, whose selection is carried over to <paramref name="tables"/>. Passed as the live
    /// objects (not as a snapshot of keys) so that anything the user selected while the reload ran is kept.
    /// </param>
    private void ReplaceTablesOnUiThread(IEnumerable<TableInfo> tables, IEnumerable<TableInfo>? previousTables = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("ReplaceTablesOnUiThread must run on the UI thread.");
        }

        var nextTables = new List<TableInfo>();

        _suppressTableSelectionUpdates = true;
        try
        {
            DisposeAllTableSubscriptions();

            nextTables.AddRange(tables);
            if (previousTables != null)
                TableSelection.CarryOver(previousTables, nextTables);

            Tables = new ObservableCollection<TableInfo>(nextTables);
            foreach (var table in nextTables)
            {
                SubscribeToTableChanges(table);
            }
        }
        finally
        {
            _suppressTableSelectionUpdates = false;
        }

        RecomputeTableViews(force: true);
    }

    /// <summary>What to try when a connection fails because of the certificate or the encryption settings (empty when neither applies).</summary>
    private static string FailureHint(ConnectionInfo? connection) => connection?.DatabaseType switch
    {
        DatabaseType.SqlServer when !connection.TrustServerCertificate =>
            " Con un certificato autofirmato (ad esempio SQL Server in un container) spunta «Accetta certificato server (SSL)».",
        DatabaseType.PostgreSQL or DatabaseType.Oracle when connection.RequireEncryption =>
            " La cifratura TLS è richiesta: il server deve offrirla"
            + (connection.TrustServerCertificate ? "." : " con un certificato valido, oppure spunta «Accetta certificato server (SSL)».")
            ,
        _ => ""
    };

    private async Task ConnectDatabasesAsync()
    {
        // A migration or a reload is running with these very connections, and the finally below would clear IsMigrating
        // under it, re-enabling "Avvia Migrazione" while tables are still being emptied and loaded.
        if (IsMigrating)
            return;

        try
        {
            IsMigrating = true;
            // The connection fields were just overwritten with settings that are not validated yet. Until the tests below
            // pass nothing may run with them, so a failed reconnect must not leave the old "connected" state behind.
            IsConnected = false;
            ErrorMessage = "";
            StatusMessage = "Connessione ai database...";
            ProgressPercentage = 0;

            // Reset connection status indicators
            SourceStatusText = "";
            TargetStatusText = "";
            SourceStatusBrush = Brushes.Transparent;
            TargetStatusBrush = Brushes.Transparent;
            ConnectionSummary = "";

            if (SourceConnection?.ConnectionInfo == null || TargetConnection?.ConnectionInfo == null)
            {
                ErrorMessage = "Errore: Compilare Server e Database";
                StatusMessage = "Errore di validazione";
                Log($"[ConnectDatabasesAsync] Validation failed. Source server='{SourceConnection?.Server}', source db='{SourceConnection?.Database}', target server='{TargetConnection?.Server}', target db='{TargetConnection?.Database}'");
                return;
            }

            // Test connessione sorgente
            StatusMessage = "Test connessione sorgente...";
            ProgressPercentage = 20;
            bool sourceOk = await _databaseService.TestConnectionAsync(SourceConnection.ConnectionInfo);
            if (sourceOk)
            {
                SourceStatusText = "● Connesso";
                SourceStatusBrush = new SolidColorBrush(Color.Parse("#43a047"));
            }
            else
            {
                SourceStatusText = "● Errore";
                SourceStatusBrush = new SolidColorBrush(Color.Parse("#ef5350"));
                ErrorMessage = "Errore: Impossibile connettersi al database sorgente. Verifica server, porta e credenziali."
                    + FailureHint(SourceConnection.ConnectionInfo);
                StatusMessage = "Connessione sorgente fallita";
                return;
            }

            ProgressPercentage = 40;

            // Test connessione target
            StatusMessage = "Test connessione target...";
            bool targetOk = await _databaseService.TestConnectionAsync(TargetConnection.ConnectionInfo);
            if (targetOk)
            {
                TargetStatusText = "● Connesso";
                TargetStatusBrush = new SolidColorBrush(Color.Parse("#43a047"));
            }
            else
            {
                TargetStatusText = "● Errore";
                TargetStatusBrush = new SolidColorBrush(Color.Parse("#ef5350"));
                ErrorMessage = "Errore: Impossibile connettersi al database target. Verifica server, porta e credenziali."
                    + FailureHint(TargetConnection.ConnectionInfo);
                StatusMessage = "Connessione target fallita";
                return;
            }

            ProgressPercentage = 60;

            // Recupera tabelle dalla sorgente
            StatusMessage = "Recupero tabelle dalla sorgente...";
            var tables = await _databaseService.GetTablesAsync(SourceConnection.ConnectionInfo);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReplaceTablesOnUiThread(tables);
            });

            ProgressPercentage = 100;
            ErrorMessage = "";
            StatusMessage = $"Connesso! Trovate {tables.Count} tabelle";
            ConnectionSummary = $"{SourceConnection.ConnectionInfo.DatabaseType}@{SourceConnection.ConnectionInfo.Server}  →  {TargetConnection.ConnectionInfo.DatabaseType}@{TargetConnection.ConnectionInfo.Server}";

            // Rende disponibile la connessione sorgente al tab "Genera Script".
            ScriptGeneration.SetSourceConnection(SourceConnection.ConnectionInfo);

            // Defer IsConnected to next UI frame to avoid potential crash when tab becomes visible
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsConnected = true;
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogError("ConnectDatabasesAsync failed", ex);
            ErrorMessage = $"Errore: {ex.Message}";
            StatusMessage = "Errore durante la connessione";
            IsConnected = false;
            ConnectionSummary = "";
            ProgressPercentage = 0;
        }
        finally
        {
            IsMigrating = false;
        }
    }

    private async Task StartMigrationAsync()
    {
        // Track tables created during schema migration for rollback on data migration failure
        var tablesCreatedDuringMigration = new List<TableInfo>();
        var constraintsAddedDuringMigration = new List<ConstraintAddedInfo>();
        // Foreign-key problems found while closing the data load: shown to the user, not only logged.
        var foreignKeyWarnings = new List<string>();
        DataLoadPlan? dataLoadPlan = null;

        try
        {
            Log($"[StartMigrationAsync] Starting migration...");
            // Kept only to put back if the user cancels at the hidden-tables question below: nothing ran in that case.
            var previousError = ErrorMessage;
            var previousProgress = ProgressPercentage;
            var previousProgressText = ProgressText;
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

            // A selected table the search filter hides is still migrated, and its data on the target replaced: say so first.
            int hiddenSelected = TableSelection.CountHidden(Tables, TableSearchFilter);
            if (hiddenSelected > 0 && ConfirmHiddenTablesAsync is { } confirmHidden)
            {
                bool replacesTargetData = SelectedMigrationMode != MigrationMode.SchemaOnly;
                bool proceed = await Dispatcher.UIThread.InvokeAsync(
                    () => confirmHidden(tablesToMigrate.Count, hiddenSelected, replacesTargetData));
                if (!proceed)
                {
                    Log($"[StartMigrationAsync] Cancelled by the user: {hiddenSelected} selected table(s) are hidden by the filter");
                    StatusMessage = "Migrazione annullata";
                    ErrorMessage = previousError;
                    ProgressPercentage = previousProgress;
                    ProgressText = previousProgressText;
                    return;
                }
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
                
                // Se è Oracle, aggiorna credenziali SOLO quando abbiamo creato un nuovo user (usedPassword non null)
                if (TargetConnection.ConnectionInfo.DatabaseType == DatabaseType.Oracle && !string.IsNullOrWhiteSpace(usedPassword))
                {
                    var schemaName = TargetConnection.ConnectionInfo.Database;
                    TargetConnection.ConnectionInfo.Username = schemaName;
                    TargetConnection.ConnectionInfo.Password = usedPassword;
                    Log($"[StartMigrationAsync] Updated target connection for Oracle: Username={schemaName}");
                }
                
                Log($"[StartMigrationAsync] Database created successfully");
                StatusMessage = "Database target creato";
            }

            ProgressPercentage = 10;

            // For SchemaAndData mode: log which tables still need to be created on the target (informational only).
            // Actual creates are recorded in tablesCreatedDuringMigration inside MigrateSchemaAsync for correct rollback.
            if (SelectedMigrationMode == MigrationMode.SchemaAndData)
            {
                Log($"[StartMigrationAsync] SchemaAndData mode: checking which tables need to be created...");
                int plannedCreates = 0;
                foreach (var table in tablesToMigrate)
                {
                    bool exists = await _schemaMigrationService.CheckTableExistsAsync(
                        TargetConnection.ConnectionInfo, table.Schema, table.TableName);
                    if (!exists)
                    {
                        plannedCreates++;
                        Log($"[StartMigrationAsync] Table {table.Schema}.{table.TableName} will be created");
                    }
                }
                Log($"[StartMigrationAsync] {plannedCreates} tables will be created during migration");
            }

            // Migrate schema if needed
            if (SelectedMigrationMode == MigrationMode.SchemaAndData || SelectedMigrationMode == MigrationMode.SchemaOnly)
            {
                Log($"[StartMigrationAsync] Starting schema migration (Mode: {SelectedMigrationMode})...");
                StatusMessage = "Migrazione schema...";
                var schemaResult = await _schemaMigrationService.MigrateSchemaAsync(
                    SourceConnection.ConnectionInfo,
                    TargetConnection.ConnectionInfo,
                    tablesToMigrate,
                    tablesCreatedDuringMigration);
                constraintsAddedDuringMigration = schemaResult.ConstraintsAdded.ToList();
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

                // The target may already enforce FOREIGN KEYs: load parents before children and, on SQL Server,
                // switch the keys off for the load. Walking the tables alphabetically made a real migration fail
                // (ACT_GE_BYTEARRAY loaded before ACT_RE_DEPLOYMENT it references).
                dataLoadPlan = await _foreignKeyService.PrepareDataLoadAsync(TargetConnection.ConnectionInfo, tablesToMigrate);
                tablesToMigrate = dataLoadPlan.OrderedTables.ToList();

                foreach (var table in tablesToMigrate)
                {
                    Log($"[StartMigrationAsync] Migrating table {table.Schema}.{table.TableName}...");
                    StatusMessage = $"Migrazione dati: {table.Schema}.{table.TableName}...";

                    int tableIdx = tablesProcessed;
                    int basePercent = SelectedMigrationMode == MigrationMode.DataOnly ? 10 : 50;
                    int totalRange = SelectedMigrationMode == MigrationMode.DataOnly ? 90 : 50;
                    int tableCount = tablesToMigrate.Count;
                    string capturedTableName = table.TableName;

                    var progress = new Progress<int>(percent =>
                    {
                        int tableBase = basePercent + tableIdx * totalRange / tableCount;
                        int tableAlloc = totalRange / tableCount;
                        int progressPercent = tableBase + percent * tableAlloc / 100;
                        ProgressPercentage = progressPercent;
                        ProgressText = $"{progressPercent}% - {capturedTableName}";
                    });

                    await _databaseService.MigrateTableAsync(
                        SourceConnection.ConnectionInfo,
                        TargetConnection.ConnectionInfo,
                        table,
                        progress,
                        tablesToMigrate.Skip(tableIdx + 1));

                    Log($"[StartMigrationAsync] Table {table.Schema}.{table.TableName} migration completed");
                    tablesProcessed++;
                    int finalPercent = basePercent + tablesProcessed * totalRange / tablesToMigrate.Count;
                    ProgressPercentage = finalPercent;
                    ProgressText = $"{finalPercent}% - {capturedTableName}";
                }

                // Switch the foreign keys back on (validating the loaded rows); a failure is handled in the catch below.
                foreignKeyWarnings.AddRange(await dataLoadPlan.CompleteAsync(succeeded: true));

                // Set final progress to 100% with generic text for modes that include data migration
                ProgressPercentage = 100;
                ProgressText = "100%";
            }
            else
            {
                Log($"[StartMigrationAsync] Skipping data migration (Mode: {SelectedMigrationMode})");
            }

            // Clear the list since migration was successful
            tablesCreatedDuringMigration.Clear();
            constraintsAddedDuringMigration.Clear();

            Log($"[StartMigrationAsync] Migration completed successfully!");
            ErrorMessage = foreignKeyWarnings.Count == 0
                ? ""
                : "Migrazione completata, ma con avvisi sulle chiavi esterne." + DescribeForeignKeyWarnings(foreignKeyWarnings);
            
            string modeDescription = SelectedMigrationMode switch
            {
                MigrationMode.SchemaOnly => "schema",
                MigrationMode.DataOnly => "dati",
                _ => "schema e dati"
            };
            StatusMessage = $"Migrazione completata! {tablesToMigrate.Count} tabelle ({modeDescription})";
        }
        catch (Exception ex)
        {
            Log($"[StartMigrationAsync] ERROR: {ex.Message}");
            Log($"[StartMigrationAsync] Stack trace: {ex.StackTrace}");

            // Never leave the target with its foreign keys switched off. No-op when the load already completed.
            if (dataLoadPlan != null)
                foreignKeyWarnings.AddRange(await dataLoadPlan.CompleteAsync(succeeded: false));

            // Rollback: drop constraints and/or tables that were created during schema migration if data migration fails.
            // In SchemaAndData mode the schema service can add PK/UNIQUE constraints even when the table already existed.
            if (SelectedMigrationMode == MigrationMode.SchemaAndData && 
                (tablesCreatedDuringMigration.Count > 0 || constraintsAddedDuringMigration.Count > 0) &&
                TargetConnection?.ConnectionInfo != null)
            {
                Log($"[StartMigrationAsync] Rolling back schema changes (tablesCreated={tablesCreatedDuringMigration.Count}, constraintsAdded={constraintsAddedDuringMigration.Count})...");
                StatusMessage = "Rolling back schema changes...";

                // 1) Drop constraints that were added during the schema migration.
                foreach (var c in constraintsAddedDuringMigration.AsEnumerable().Reverse())
                {
                    try
                    {
                        await _schemaMigrationService.DropConstraintAsync(
                            TargetConnection.ConnectionInfo,
                            c.Schema,
                            c.TableName,
                            c.ConstraintName,
                            c.ConstraintType);
                    }
                    catch (Exception rollbackConstraintEx)
                    {
                        Log($"[StartMigrationAsync] Failed to rollback constraint {c.ConstraintName} on {c.Schema}.{c.TableName}: {rollbackConstraintEx.Message}");
                    }
                }

                // 2) Drop tables created during schema migration (if any).
                if (tablesCreatedDuringMigration.Count > 0)
                {
                    StatusMessage = "Rolling back created tables...";

                    foreach (var table in tablesCreatedDuringMigration)
                    {
                        try
                        {
                            await _schemaMigrationService.DropTableAsync(
                                TargetConnection.ConnectionInfo, table.Schema, table.TableName);
                            Log($"[StartMigrationAsync] Rolled back table {table.Schema}.{table.TableName}");
                        }
                        catch (Exception rollbackEx)
                        {
                            Log($"[StartMigrationAsync] Failed to rollback table {table.Schema}.{table.TableName}: {rollbackEx.Message}");
                        }
                    }
                }

                Log($"[StartMigrationAsync] Rollback completed");
            }
            
            ErrorMessage = $"Errore migrazione: {ex.Message}" + DescribeForeignKeyWarnings(foreignKeyWarnings);
            StatusMessage = "Migration failed";
            ProgressPercentage = 0;
        }
        finally
        {
            IsMigrating = false;
        }
    }

    private static string DescribeForeignKeyWarnings(IReadOnlyList<string> warnings) =>
        warnings.Count == 0
            ? ""
            : " Chiavi esterne: " + string.Join(" | ", warnings.Take(3)) +
              (warnings.Count > 3 ? $" (e altri {warnings.Count - 3} avvisi, vedi il log)" : "");

    public void SelectAllTablesDirectly()
    {
        _ = SetAllTablesSelectionAsync(true);
    }

    public void DeselectAllTablesDirectly()
    {
        _ = SetAllTablesSelectionAsync(false);
    }

    public Task SelectAllTablesDirectlyAsync()
    {
        return SetAllTablesSelectionAsync(true);
    }

    public Task DeselectAllTablesDirectlyAsync()
    {
        return SetAllTablesSelectionAsync(false);
    }

    /// <summary>
    /// Sets the selection state for all tables.
    /// </summary>
    /// <param name="isSelected">True to select all, false to deselect all.</param>
    private async Task SetAllTablesSelectionAsync(bool isSelected)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            string operation = isSelected ? "SelectAll" : "DeselectAll";
            Log($"[{operation}TablesDirectly] Starting... Total tables: {Tables.Count}");

            _suppressTableSelectionUpdates = true;
            try
            {
                // "Select all" acts on what the search box shows, so filtering a schema and pressing it selects that
                // schema only. "Deselect all" clears everything: a selected table the filter hides would still be
                // migrated (and emptied on the target) without the user seeing it.
                if (isSelected)
                    TableSelection.SelectVisible(Tables, TableSearchFilter);
                else
                    TableSelection.DeselectAll(Tables);
            }
            finally
            {
                _suppressTableSelectionUpdates = false;
            }

            // Forced: during a reload the normal recompute is skipped, which left the counters stale after this click.
            RecomputeTableViews(force: true);
            Log($"[{operation}TablesDirectly] Completed. SelectedTablesCount={SelectedTablesCount}");
        });
    }

    private void UpdateTableStatistics()
    {
        RecomputeTableViews();
    }

    /// <summary>
    /// Applies the search filter to the tables.
    /// </summary>
    private void ApplyTableFilter()
    {
        RecomputeTableViews();
    }

    /// <summary>
    /// Reloads the tables from the source database.
    /// </summary>
    public async Task RefreshTablesAsync()
    {
        if (_isRefreshingTables)
        {
            Log("[RefreshTablesAsync] Refresh already in progress, skipping.");
            return;
        }

        try
        {
            Log("[RefreshTablesAsync] Starting tables refresh...");
            _isRefreshingTables = true;
            IsMigrating = true;  // Disable Refresh/Start buttons on UI thread before any await
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "Ricaricamento tabelle...";
            });
            
            if (SourceConnection?.ConnectionInfo == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ErrorMessage = "Errore: Connessione sorgente non valida";
                    StatusMessage = "Errore: connessione non valida";
                });
                return;
            }

            // Reload tables from source database. The selection to keep is read when the tables are replaced, not
            // before this await: the user can keep selecting while the reload runs, and a snapshot taken up front
            // would silently undo those clicks when it is applied to the reloaded tables.
            var tables = await _databaseService.GetTablesAsync(SourceConnection.ConnectionInfo);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Log($"[RefreshTablesAsync] Preserving {Tables.Count(t => t.IsSelected)} selected tables");
                ReplaceTablesOnUiThread(tables, previousTables: Tables);
                StatusMessage = $"Tabelle ricaricate! Trovate {tables.Count} tabelle";
                ErrorMessage = "";
            });
            
            Log($"[RefreshTablesAsync] Refresh completed. {tables.Count} tables loaded");
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ErrorMessage = $"Errore nel ricaricamento: {ex.Message}";
                StatusMessage = "Errore durante il ricaricamento";
            });
            Log($"[RefreshTablesAsync] Error: {ex.Message}");
        }
        finally
        {
            _isRefreshingTables = false;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsMigrating = false;
                // While reloading, RecomputeTableViews does nothing, so a filter typed or a click made in the meantime
                // (and every failure path, which never replaces the tables) leaves the lists and counters out of date.
                RecomputeTableViews();
            });
        }
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
        // A migration reads SourceConnection/TargetConnection again for every table: swapping them now would send the
        // remaining tables, and a rollback's DROP TABLE, to another database.
        if (IsMigrating)
        {
            Log("[LoadConfigurationAsync] Ignorato: una migrazione o un aggiornamento è in corso");
            return false;
        }

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

            // The file read above can take seconds (network share, OneDrive placeholder): a migration started meanwhile
            // must not have its connections swapped. No await between this check and the assignments below.
            if (IsMigrating)
            {
                Log("[LoadConfigurationAsync] Ignorato: nel frattempo è partita una migrazione o un aggiornamento");
                return false;
            }

            // Both connections are built before either is assigned: a bad target (an unknown database type, a password the
            // current Windows user cannot decrypt) must not leave the source of the new file next to the old target.
            var sourceInfo = config.Source.ToConnectionInfo();
            var targetInfo = config.Target.ToConnectionInfo();
            // SelectedDatabaseType first: its setter resets Port to the type's default, which would undo the loaded port.
            var newSource = new ConnectionViewModel
            {
                SelectedDatabaseType = sourceInfo.DatabaseType,
                Server = sourceInfo.Server,
                Port = sourceInfo.Port,
                Database = sourceInfo.Database,
                Username = sourceInfo.Username,
                Password = sourceInfo.Password,
                TrustServerCertificate = sourceInfo.TrustServerCertificate,
                RequireEncryption = sourceInfo.RequireEncryption
            };
            var newTarget = new ConnectionViewModel
            {
                SelectedDatabaseType = targetInfo.DatabaseType,
                Server = targetInfo.Server,
                Port = targetInfo.Port,
                Database = targetInfo.Database,
                Username = targetInfo.Username,
                Password = targetInfo.Password,
                TrustServerCertificate = targetInfo.TrustServerCertificate,
                RequireEncryption = targetInfo.RequireEncryption
            };

            // The loaded settings are not validated and the table list still belongs to the previous source: connect again.
            // Same reset as the start of a Connect, so the Connections tab does not keep saying "Connesso" in green.
            IsConnected = false;
            ConnectionSummary = "";
            SourceStatusText = "";
            TargetStatusText = "";
            SourceStatusBrush = Brushes.Transparent;
            TargetStatusBrush = Brushes.Transparent;
            ErrorMessage = "";
            ProgressPercentage = 0;
            SourceConnection = newSource;
            TargetConnection = newTarget;

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
