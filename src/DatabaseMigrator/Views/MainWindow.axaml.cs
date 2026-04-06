using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Collections.Specialized;
using System.Diagnostics;
using DatabaseMigrator.Core.Services;
using DatabaseMigrator.ViewModels;
using DatabaseMigrator.Core.Models;
using System.Reactive;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using System.Reactive.Linq;

namespace DatabaseMigrator.Views;

    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _vm;
        private bool _allowClose = false;

    private static void Log(string message) => DatabaseMigrator.Core.Services.LoggerService.Log(message);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;
        await Task.Yield(); // cede il controllo → la finestra viene renderizzata prima dell'init
        InitializeViewModel();
    }

    private void InitializeViewModel()
    {
        try
        {
            _vm = new MainWindowViewModel();
            DataContext = _vm;
            _vm.TruncateFailedPromptHandlerAsync = ShowTruncateFailedDialogAsync;
            
            // Bind ViewModel properties to UI
            StatusTextBlock.Bind(TextBlock.TextProperty, new Binding("StatusMessage") { Source = _vm });
            StatusBarTextBlock.Bind(TextBlock.TextProperty, new Binding("StatusMessage") { Source = _vm });
            ErrorTextBlock.Bind(TextBlock.TextProperty, new Binding("ErrorMessage") { Source = _vm });
            ProgressBar.Bind(ProgressBar.ValueProperty, new Binding("ProgressPercentage") { Source = _vm });
            ProgressTextBlock.Bind(TextBlock.TextProperty, new Binding("ProgressText") { Source = _vm });
            
            TablesTab.Bind(IsEnabledProperty, new Binding("IsConnected") { Source = _vm });
            MigrationTab.Bind(IsEnabledProperty, new Binding("IsConnected") { Source = _vm });
            
            // Bind StartMigrationButton IsEnabled based on connection and migration state
            StartMigrationButton.Bind(IsEnabledProperty, 
                new Binding { Source = _vm, Path = "IsConnected" });
            
            // Subscribe to IsMigrating changes to update button state
            _vm.WhenAnyValue(vm => vm.IsMigrating)
                .Subscribe(isMigrating =>
                {
                    StartMigrationButton.IsEnabled = _vm.IsConnected && !isMigrating;
                });
            
            // Bind Tables Lists - use FilteredTables for search functionality
            SourceTablesListBox.Bind(ItemsControl.ItemsSourceProperty, new Binding("FilteredTables") { Source = _vm });
            TargetTablesListBox.Bind(ItemsControl.ItemsSourceProperty, new Binding("FilteredTargetTables") { Source = _vm });
            
            // Bind search box
            TableSearchTextBox.Bind(TextBox.TextProperty, new Binding("TableSearchFilter") { Source = _vm, Mode = BindingMode.TwoWay });
            
            // Wire up search clear button
            ClearSearchButton.Click += (s, e) =>
            {
                _vm.TableSearchFilter = "";
            };
            
            // Wire up refresh button with Click (avoids ReactiveCommand threading crash)
            RefreshSourceButton.IsEnabled = _vm.IsConnected && !_vm.IsMigrating;
            RefreshSourceButton.Click += OnRefreshClicked;
            _vm.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating, (c, m) => c && !m)
                .Subscribe(canRefresh => Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshSourceButton.IsEnabled = canRefresh));
            
            // Bind statistics
            SourceCountTextBlock.Bind(TextBlock.TextProperty, new Binding("Tables.Count") { Source = _vm, StringFormat = "📋 Tabelle sorgente: {0}" });
            SelectedCountTextBlock.Bind(TextBlock.TextProperty, new Binding("SelectedTablesCount") { Source = _vm, StringFormat = "✓ Tabelle selezionate: {0}" });
            TargetCountTextBlock.Bind(TextBlock.TextProperty, new Binding("FilteredTargetTables.Count") { Source = _vm, StringFormat = "📋 Tabelle visualizzate: {0}" });
            TotalRowsTextBlock.Bind(TextBlock.TextProperty, new Binding("TotalRowsToMigrate") { Source = _vm, StringFormat = "📊 Righe totali da migrare: {0}" });
            
            // Wire up button clicks
            ConnectButton.Click += OnConnectClicked;
            StartMigrationButton.Click += (s, e) => 
            {
                if (_vm != null && _vm.IsConnected && !_vm.IsMigrating)
                {
                    _vm.StartMigrationCommand.Execute(Unit.Default).Subscribe();
                }
            };
            
            // Wire up menu items
            SaveConfigMenuItem.Click += OnSaveConfigurationClicked;
            LoadConfigMenuItem.Click += OnLoadConfigurationClicked;
            ExitMenuItem.Click += (s, e) => Close();
            AboutMenuItem.Click += (s, e) => ShowAbout();

            // ── Connection status indicators ─────────────────────────────
            SourceStatusTextBlock.Bind(TextBlock.TextProperty,
                new Binding("SourceStatusText") { Source = _vm });
            SourceStatusTextBlock.Bind(TextBlock.ForegroundProperty,
                new Binding("SourceStatusBrush") { Source = _vm });
            TargetStatusTextBlock.Bind(TextBlock.TextProperty,
                new Binding("TargetStatusText") { Source = _vm });
            TargetStatusTextBlock.Bind(TextBlock.ForegroundProperty,
                new Binding("TargetStatusBrush") { Source = _vm });

            // ── Status bar connection info ────────────────────────────────
            StatusBarConnectionInfo.Bind(TextBlock.TextProperty,
                new Binding("ConnectionSummary") { Source = _vm });

            // ── Error box visibility ──────────────────────────────────────
            ErrorBorder.Bind(IsVisibleProperty,
                new Binding("ErrorMessage") { Source = _vm, Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });

            // ── Window title update on connect ────────────────────────────
            _vm.WhenAnyValue(vm => vm.IsConnected).Subscribe(connected =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (connected
                        && _vm.SourceConnection?.ConnectionInfo != null
                        && _vm.TargetConnection?.ConnectionInfo != null)
                    {
                        var src = _vm.SourceConnection.ConnectionInfo;
                        var tgt = _vm.TargetConnection.ConnectionInfo;
                        Title = $"Database Migrator — {src.DatabaseType}@{src.Server}  →  {tgt.DatabaseType}@{tgt.Server}";
                    }
                    else
                    {
                        Title = "Database Migrator";
                    }
                });
            });

            // ── Password visibility toggles ───────────────────────────────
            SourcePasswordToggle.Click += (s, e) =>
                SourcePasswordTextBox.PasswordChar = SourcePasswordTextBox.PasswordChar == default ? '•' : default;
            TargetPasswordToggle.Click += (s, e) =>
                TargetPasswordTextBox.PasswordChar = TargetPasswordTextBox.PasswordChar == default ? '•' : default;

            // ── Tab badges ────────────────────────────────────────────────
            _vm.WhenAnyValue(vm => vm.LogErrorCount).Subscribe(count =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    LogErrorBadge.IsVisible = count > 0;
                    LogErrorBadgeText.Text = count > 9 ? "9+" : count.ToString();
                });
            });

            _vm.WhenAnyValue(vm => vm.SelectedTablesCount).Subscribe(n =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TablesSelectedBadge.IsVisible = n > 0;
                    TablesSelectedBadge.Text = $"({n})";
                });
            });

            // ── Log tab ──────────────────────────────────────────────────
            LogListBox.Bind(ItemsControl.ItemsSourceProperty,
                new Binding("FilteredLogEntries") { Source = _vm });

            LogCountTextBlock.Bind(TextBlock.TextProperty,
                new Binding("FilteredLogEntries.Count") { Source = _vm, StringFormat = "{0} voci" });

            FilterErrorsCheckBox.Bind(CheckBox.IsCheckedProperty,
                new Binding("ShowOnlyErrors") { Source = _vm, Mode = BindingMode.TwoWay });

            ClearLogButton.Click += (s, e) =>
                _vm?.ClearLogCommand.Execute(System.Reactive.Unit.Default).Subscribe();

            CopyLogButton.Click += async (s, e) =>
            {
                if (_vm == null) return;
                var lines = new System.Text.StringBuilder();
                foreach (var logEntry in _vm.FilteredLogEntries)
                    lines.AppendLine($"{logEntry.FormattedTime} {logEntry.LevelTag} {logEntry.Message}");
                if (Clipboard != null)
                    await Clipboard.SetTextAsync(lines.ToString());
            };

            OpenLogFileButton.Click += (s, e) =>
            {
                var path = LoggerService.GetLogPath();
                if (File.Exists(path))
                {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                    catch (Exception ex) { Log($"[OpenLogFileButton] Failed to open log file: {ex.Message}"); }
                }
            };

            // Auto-scroll: scroll to bottom when new entries arrive
            _vm.FilteredLogEntries.CollectionChanged += OnLogEntriesChanged;
            
            // Initialize migration mode before wiring event handlers to avoid race condition
            if (ModeSchemaAndData.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaAndData);
            }
            else if (ModeSchemaOnly.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaOnly);
            }
            else if (ModeDataOnly.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.DataOnly);
            }
            else
            {
                // Default to SchemaAndData if no mode is checked
                ModeSchemaAndData.IsChecked = true;
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaAndData);
            }

            // Wire up migration mode radio buttons after initial mode is set
            ModeSchemaAndData.IsCheckedChanged += OnMigrationModeChanged;
            ModeSchemaOnly.IsCheckedChanged += OnMigrationModeChanged;
            ModeDataOnly.IsCheckedChanged += OnMigrationModeChanged;
            
            // Wire up window closing event
            Closing += OnWindowClosing;
        }
        catch (Exception ex)
        {
            Log($"Init error: {ex}");
            throw;
        }
    }
    
    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var count = LogListBox.ItemCount;
                if (count > 0)
                    LogListBox.ContainerFromIndex(count - 1)?.BringIntoView();
            }
            catch { /* Ignore scroll errors */ }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose && _vm != null && _vm.IsMigrating)
        {
            Log("[OnWindowClosing] Migration in progress, showing confirmation dialog");
            e.Cancel = true;
            
            var result = await ShowMigrationConfirmationDialog();
            
            if (result)
            {
                Log("[OnWindowClosing] User confirmed to close the app during migration");
                _allowClose = true;
                Close();
            }
            else
            {
                Log("[OnWindowClosing] User cancelled close operation");
            }
        }
    }
    
    private async Task<bool> ShowMigrationConfirmationDialog()
    {
        var dialog = new Window
        {
            Title = "⚠️ Migrazione in Corso",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };
        
        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Spacing = 15
        };
        
        var messageText = new TextBlock
        {
            Text = "Una migrazione è attualmente in corso.\nSei sicuro di voler chiudere l'applicazione?\n\nI dati potrebbero essere corrotti se interrompi il processo.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14
        };
        
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        
        var continueButton = new Button
        {
            Content = "Chiudi",
            Width = 120,
            Padding = new Thickness(10, 5),
            Background = Avalonia.Media.Brushes.Red,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        
        var cancelButton = new Button
        {
            Content = "Annulla",
            Width = 120,
            Padding = new Thickness(10, 5),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        
        bool userConfirmed = false;
        
        continueButton.Click += (s, e) => 
        {
            userConfirmed = true;
            dialog.Close();
        };
        
        cancelButton.Click += (s, e) =>
        {
            dialog.Close();
        };
        
        buttonPanel.Children.Add(continueButton);
        buttonPanel.Children.Add(cancelButton);
        
        stackPanel.Children.Add(messageText);
        stackPanel.Children.Add(buttonPanel);
        
        dialog.Content = stackPanel;
        
        await dialog.ShowDialog(this);
        
        return userConfirmed;
    }

    private async Task<bool> ShowTruncateFailedDialogAsync(TruncateFailureContext ctx)
    {
        if (ctx is null)
            return false;

        var error = ctx.ErrorMessage ?? "";
        if (error.Length > 400)
            error = error.Substring(0, 400) + "...";

        var dialog = new Window
        {
            Title = "⚠️ TRUNCATE fallito",
            Width = 520,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Spacing = 15
        };

        var messageText = new TextBlock
        {
            Text = $"Impossibile eseguire TRUNCATE su {ctx.Schema}.{ctx.TableName}.\n\n" +
                   $"Errore: {error}\n\n" +
                   "Vuoi continuare inserendo comunque i dati?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        bool userConfirmed = false;

        var continueButton = new Button
        {
            Content = "Continua",
            Width = 140,
            Padding = new Thickness(10, 5),
            Background = Avalonia.Media.Brushes.DodgerBlue,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        var cancelButton = new Button
        {
            Content = "Annulla",
            Width = 140,
            Padding = new Thickness(10, 5),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        continueButton.Click += (s, e) =>
        {
            userConfirmed = true;
            dialog.Close();
        };

        cancelButton.Click += (s, e) =>
        {
            dialog.Close();
        };

        buttonPanel.Children.Add(continueButton);
        buttonPanel.Children.Add(cancelButton);

        stackPanel.Children.Add(messageText);
        stackPanel.Children.Add(buttonPanel);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(this);
        return userConfirmed;
    }
    
    private async void SelectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        Log("[SelectAllButton_Click] Button clicked!");
        if (_vm != null)
        {
            Log("[SelectAllButton_Click] Calling SelectAllTables...");
            await _vm.SelectAllTablesDirectlyAsync();
            Log("[SelectAllButton_Click] SelectAllTables executed");
        }
        else
        {
            Log("[SelectAllButton_Click] ViewModel is null!");
        }
    }
    
    private async void DeselectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        Log("[DeselectAllButton_Click] Button clicked!");
        if (_vm != null)
        {
            Log("[DeselectAllButton_Click] Calling DeselectAllTables...");
            await _vm.DeselectAllTablesDirectlyAsync();
            Log("[DeselectAllButton_Click] DeselectAllTables executed");
        }
        else
        {
            Log("[DeselectAllButton_Click] ViewModel is null!");
        }
    }

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || !_vm.IsConnected || _vm.IsMigrating) return;
        try
        {
            await _vm.RefreshTablesAsync();
        }
        catch (Exception ex)
        {
            Log($"[OnRefreshClicked] Error: {ex}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _vm.ErrorMessage = $"Errore: {ex.Message}";
                _vm.StatusMessage = "Errore durante il ricaricamento";
            });
        }
    }

    private bool _isUpdatingMigrationMode;

    private void SetMigrationMode(MigrationMode mode)
    {
        if (_vm == null) return;
        
        _vm.SelectedMigrationMode = mode;
        MigrationModeDescription.Text = mode switch
        {
            MigrationMode.SchemaAndData => "Crea le tabelle nel database di destinazione e copia tutti i dati. Se la tabella esiste già, verranno copiati solo i dati.",
            MigrationMode.SchemaOnly => "Crea solo la struttura delle tabelle nel database di destinazione, senza copiare i dati.",
            MigrationMode.DataOnly => "Copia solo i dati nelle tabelle esistenti. Le tabelle devono già esistere nel database di destinazione.",
            _ => "Crea le tabelle nel database di destinazione e copia tutti i dati. Se la tabella esiste già, verranno copiati solo i dati."
        };
    }

    private void OnMigrationModeChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingMigrationMode)
        {
            return;
        }

        _isUpdatingMigrationMode = true;
        try
        {
            if (_vm == null) return;

            if (ModeSchemaAndData.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaAndData);
                Log("[OnMigrationModeChanged] Mode set to SchemaAndData");
            }
            else if (ModeSchemaOnly.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaOnly);
                Log("[OnMigrationModeChanged] Mode set to SchemaOnly");
            }
            else if (ModeDataOnly.IsChecked is true)
            {
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.DataOnly);
                Log("[OnMigrationModeChanged] Mode set to DataOnly");
            }
            else
            {
                // Fallback in case all radio buttons are unchecked: enforce a safe default
                SetMigrationMode(DatabaseMigrator.Core.Models.MigrationMode.SchemaAndData);
                if (ModeSchemaAndData.IsChecked is not true)
                {
                    ModeSchemaAndData.IsChecked = true;
                }
                Log("[OnMigrationModeChanged] No mode checked; defaulting to SchemaAndData");
            }
        }
        finally
        {
            _isUpdatingMigrationMode = false;
        }
    }

    private void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        
        try
        {
            // Read UI values
            int sourceType = SourceTypeCombo.SelectedIndex;
            int targetType = TargetTypeCombo.SelectedIndex;
            
            // Validate that the selected index corresponds to a valid DatabaseType
            var databaseTypeValues = Enum.GetValues(typeof(DatabaseType));
            int maxDatabaseType = databaseTypeValues.Length - 1;
            if (sourceType < 0 || sourceType > maxDatabaseType)
            {
                Log($"[MainWindow] Invalid source database type index: {sourceType}");
                ErrorTextBlock.Text = "Seleziona un tipo di database sorgente valido";
                return;
            }
            if (targetType < 0 || targetType > maxDatabaseType)
            {
                Log($"[MainWindow] Invalid target database type index: {targetType}");
                ErrorTextBlock.Text = "Seleziona un tipo di database destinazione valido";
                return;
            }
            
            Log($"[MainWindow] Source Type Index: {sourceType}");
            Log($"[MainWindow] Target Type Index: {targetType}");
            Log($"[MainWindow] Source Server: {SourceServerTextBox.Text}");
            Log($"[MainWindow] Source Port: {SourcePortTextBox.Text}");
            Log($"[MainWindow] Source Database: {SourceDatabaseTextBox.Text}");
            Log($"[MainWindow] Source Username: {SourceUsernameTextBox.Text}");
            
            // Mapping to enum: 0=SqlServer, 1=Oracle, 2=PostgreSQL
            _vm.SourceConnection!.SelectedDatabaseType = (DatabaseType)sourceType;
            _vm.SourceConnection.Server = SourceServerTextBox.Text ?? "";
            _vm.SourceConnection.Port = int.TryParse(SourcePortTextBox.Text, out int sp) ? sp : 1433;
            _vm.SourceConnection.Database = SourceDatabaseTextBox.Text ?? "";
            _vm.SourceConnection.Username = SourceUsernameTextBox.Text ?? "";
            _vm.SourceConnection.Password = SourcePasswordTextBox.Text ?? "";
            _vm.SourceConnection.TrustServerCertificate = SourceTrustServerCertificateCheckBox.IsChecked == true;
            
            _vm.TargetConnection!.SelectedDatabaseType = (DatabaseType)targetType;
            _vm.TargetConnection.Server = TargetServerTextBox.Text ?? "";
            _vm.TargetConnection.Port = int.TryParse(TargetPortTextBox.Text, out int tp) ? tp : 5432;
            _vm.TargetConnection.Database = TargetDatabaseTextBox.Text ?? "";
            _vm.TargetConnection.Username = TargetUsernameTextBox.Text ?? "";
            _vm.TargetConnection.Password = TargetPasswordTextBox.Text ?? "";
            _vm.TargetConnection.TrustServerCertificate = TargetTrustServerCertificateCheckBox.IsChecked == true;
            
            Log($"[MainWindow] Executing ConnectDatabasesCommand...");
            _vm.ConnectDatabasesCommand.Execute(Unit.Default);
        }
        catch (Exception ex)
        {
            Log($"[MainWindow] Exception: {ex}");
            ErrorTextBlock.Text = $"Errore: {ex.Message}";
            StatusBarTextBlock.Text = "Errore durante la connessione";
        }
    }

    private async void OnSaveConfigurationClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null)
            {
                Log("[OnSaveConfigurationClicked] StorageProvider not available");
                return;
            }

            var configDir = MainWindowViewModel.GetConfigDirectory();
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
                Log($"[OnSaveConfigurationClicked] Created configuration directory: {configDir}");
            }

            var startLocation = await storageProvider.TryGetFolderFromPathAsync(configDir);
            if (startLocation == null)
            {
                Log($"[OnSaveConfigurationClicked] Unable to resolve start location for path: {configDir}. Using default picker location.");
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva Configurazione",
                SuggestedFileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                SuggestedStartLocation = startLocation,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                var result = file.Path.LocalPath;
                Log($"[OnSaveConfigurationClicked] Salvando in {result}");
                if (await _vm!.SaveConfigurationAsync(result))
                {
                    Log("[OnSaveConfigurationClicked] Configurazione salvata con successo");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[OnSaveConfigurationClicked] Errore: {ex.Message}");
            ErrorTextBlock.Text = $"Errore nel salvataggio: {ex.Message}";
        }
    }

    private async void OnLoadConfigurationClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = StorageProvider;
            if (storageProvider == null)
            {
                Log("[OnLoadConfigurationClicked] StorageProvider not available");
                return;
            }

            var configDir = MainWindowViewModel.GetConfigDirectory();
            var startLocation = await storageProvider.TryGetFolderFromPathAsync(configDir);

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Carica Configurazione",
                SuggestedStartLocation = startLocation,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files != null && files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
                Log($"[OnLoadConfigurationClicked] Caricando da {filePath}");
                if (await _vm!.LoadConfigurationAsync(filePath))
                {
                    Log("[OnLoadConfigurationClicked] Configurazione caricata con successo");
                    
                    // Popola i campi UI con i dati caricati
                    if (_vm.SourceConnection?.ConnectionInfo != null)
                    {
                        SourceTypeCombo.SelectedIndex = (int)_vm.SourceConnection.ConnectionInfo.DatabaseType;
                        SourceServerTextBox.Text = _vm.SourceConnection.ConnectionInfo.Server;
                        SourcePortTextBox.Text = _vm.SourceConnection.ConnectionInfo.Port.ToString();
                        SourceDatabaseTextBox.Text = _vm.SourceConnection.ConnectionInfo.Database;
                        SourceUsernameTextBox.Text = _vm.SourceConnection.ConnectionInfo.Username;
                        SourcePasswordTextBox.Text = _vm.SourceConnection.ConnectionInfo.Password;
                        SourceTrustServerCertificateCheckBox.IsChecked = _vm.SourceConnection.ConnectionInfo.TrustServerCertificate;
                    }

                    if (_vm.TargetConnection?.ConnectionInfo != null)
                    {
                        TargetTypeCombo.SelectedIndex = (int)_vm.TargetConnection.ConnectionInfo.DatabaseType;
                        TargetServerTextBox.Text = _vm.TargetConnection.ConnectionInfo.Server;
                        TargetPortTextBox.Text = _vm.TargetConnection.ConnectionInfo.Port.ToString();
                        TargetDatabaseTextBox.Text = _vm.TargetConnection.ConnectionInfo.Database;
                        TargetUsernameTextBox.Text = _vm.TargetConnection.ConnectionInfo.Username;
                        TargetPasswordTextBox.Text = _vm.TargetConnection.ConnectionInfo.Password;
                        TargetTrustServerCertificateCheckBox.IsChecked = _vm.TargetConnection.ConnectionInfo.TrustServerCertificate;
                    }

                    StatusBarTextBlock.Text = "Configurazione caricata";
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[OnLoadConfigurationClicked] Errore: {ex.Message}");
            ErrorTextBlock.Text = $"Errore nel caricamento: {ex.Message}";
        }
    }

    private void ShowAbout()
    {
        StatusBarTextBlock.Text = "Database Migrator v1.0 - Strumento per migrare dati tra SQL Server, PostgreSQL e Oracle";
    }
}
