using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
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
        
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseMigrator", "debug.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            File.AppendAllText(LogPath, fullMessage + "\n");
            System.Diagnostics.Debug.WriteLine(fullMessage);
        }
        catch { }
    }

    public MainWindow() 
    { 
        InitializeComponent();
        
        try
        {
            _vm = new MainWindowViewModel();
            DataContext = _vm;
            
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
            
            // Bind Tables Lists
            SourceTablesListBox.Bind(ItemsControl.ItemsSourceProperty, new Binding("Tables") { Source = _vm });
            TargetTablesListBox.Bind(ItemsControl.ItemsSourceProperty, new Binding("Tables") { Source = _vm });
            
            // Bind statistics
            SourceCountTextBlock.Bind(TextBlock.TextProperty, new Binding("Tables.Count") { Source = _vm, StringFormat = "📋 Tabelle sorgente: {0}" });
            SelectedCountTextBlock.Bind(TextBlock.TextProperty, new Binding("SelectedTablesCount") { Source = _vm, StringFormat = "✓ Tabelle selezionate: {0}" });
            TargetCountTextBlock.Bind(TextBlock.TextProperty, new Binding("Tables.Count") { Source = _vm, StringFormat = "📋 Tabelle destinazione: {0}" });
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
            
            // Wire up window closing event
            Closing += OnWindowClosing;
        }
        catch (Exception ex)
        {
            Log($"Init error: {ex}");
            throw;
        }
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
    
    private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        Log("[SelectAllButton_Click] Button clicked!");
        if (_vm != null)
        {
            Log("[SelectAllButton_Click] Calling SelectAllTables directly...");
            // Call the method directly instead of using the command
            _vm.SelectAllTablesDirectly();
            Log("[SelectAllButton_Click] SelectAllTables executed");
        }
        else
        {
            Log("[SelectAllButton_Click] ViewModel is null!");
        }
    }
    
    private void DeselectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        Log("[DeselectAllButton_Click] Button clicked!");
        if (_vm != null)
        {
            Log("[DeselectAllButton_Click] Calling DeselectAllTables directly...");
            // Call the method directly instead of using the command
            _vm.DeselectAllTablesDirectly();
            Log("[DeselectAllButton_Click] DeselectAllTables executed");
        }
        else
        {
            Log("[DeselectAllButton_Click] ViewModel is null!");
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
            
            Log($"[MainWindow] Source Type Index: {sourceType}");
            Log($"[MainWindow] Target Type Index: {targetType}");
            Log($"[MainWindow] Source Server: {SourceServerTextBox.Text}");
            Log($"[MainWindow] Source Port: {SourcePortTextBox.Text}");
            Log($"[MainWindow] Source Database: {SourceDatabaseTextBox.Text}");
            Log($"[MainWindow] Source Username: {SourceUsernameTextBox.Text}");
            
            // Mappatura diretta agli enum: 0=SqlServer, 1=Oracle, 2=PostgreSQL
            _vm.SourceConnection!.SelectedDatabaseType = (DatabaseType)sourceType;
            _vm.SourceConnection.Server = SourceServerTextBox.Text ?? "";
            _vm.SourceConnection.Port = int.TryParse(SourcePortTextBox.Text, out int sp) ? sp : 1433;
            _vm.SourceConnection.Database = SourceDatabaseTextBox.Text ?? "";
            _vm.SourceConnection.Username = SourceUsernameTextBox.Text ?? "";
            _vm.SourceConnection.Password = SourcePasswordTextBox.Text ?? "";
            
            _vm.TargetConnection!.SelectedDatabaseType = (DatabaseType)targetType;
            _vm.TargetConnection.Server = TargetServerTextBox.Text ?? "";
            _vm.TargetConnection.Port = int.TryParse(TargetPortTextBox.Text, out int tp) ? tp : 5432;
            _vm.TargetConnection.Database = TargetDatabaseTextBox.Text ?? "";
            _vm.TargetConnection.Username = TargetUsernameTextBox.Text ?? "";
            _vm.TargetConnection.Password = TargetPasswordTextBox.Text ?? "";
            
            Log($"[MainWindow] Executing ConnectDatabasesCommand...");
            _vm.ConnectDatabasesCommand.Execute(Unit.Default);
        }
        catch (Exception ex)
        {
            Log($"[MainWindow] Exception: {ex}");
            ErrorTextBlock.Text = $"❌ Errore: {ex.Message}";
            StatusBarTextBlock.Text = "Errore durante la connessione";
        }
    }

    private async void OnSaveConfigurationClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salva Configurazione",
                InitialFileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Directory = MainWindowViewModel.GetConfigDirectory(),
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new() { Name = "JSON Files", Extensions = new System.Collections.Generic.List<string> { "json" } },
                    new() { Name = "All Files", Extensions = new System.Collections.Generic.List<string> { "*" } }
                }
            };

            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
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
            ErrorTextBlock.Text = $"❌ Errore nel salvataggio: {ex.Message}";
        }
    }

    private async void OnLoadConfigurationClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Carica Configurazione",
                Directory = MainWindowViewModel.GetConfigDirectory(),
                AllowMultiple = false,
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new() { Name = "JSON Files", Extensions = new System.Collections.Generic.List<string> { "json" } },
                    new() { Name = "All Files", Extensions = new System.Collections.Generic.List<string> { "*" } }
                }
            };

            var results = await dialog.ShowAsync(this);
            if (results != null && results.Length > 0)
            {
                var filePath = results[0];
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
                    }

                    if (_vm.TargetConnection?.ConnectionInfo != null)
                    {
                        TargetTypeCombo.SelectedIndex = (int)_vm.TargetConnection.ConnectionInfo.DatabaseType;
                        TargetServerTextBox.Text = _vm.TargetConnection.ConnectionInfo.Server;
                        TargetPortTextBox.Text = _vm.TargetConnection.ConnectionInfo.Port.ToString();
                        TargetDatabaseTextBox.Text = _vm.TargetConnection.ConnectionInfo.Database;
                        TargetUsernameTextBox.Text = _vm.TargetConnection.ConnectionInfo.Username;
                        TargetPasswordTextBox.Text = _vm.TargetConnection.ConnectionInfo.Password;
                    }

                    StatusBarTextBlock.Text = "✓ Configurazione caricata";
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[OnLoadConfigurationClicked] Errore: {ex.Message}");
            ErrorTextBlock.Text = $"❌ Errore nel caricamento: {ex.Message}";
        }
    }

    private void ShowAbout()
    {
        StatusBarTextBlock.Text = "🗄️ Database Migrator v1.0 - Strumento per migrare dati tra SQL Server, PostgreSQL e Oracle";
    }
}
