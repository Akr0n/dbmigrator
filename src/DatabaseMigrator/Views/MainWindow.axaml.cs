using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DatabaseMigrator.ViewModels;
using DatabaseMigrator.Core.Models;
using System.Reactive;
using System;
using System.IO;

namespace DatabaseMigrator.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    
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
            StartMigrationButton.Bind(IsEnabledProperty, new Binding("IsConnected") { Source = _vm });
            
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
            StartMigrationButton.Click += (s, e) => _vm.StartMigrationCommand.Execute(Unit.Default);
        }
        catch (Exception ex)
        {
            Log($"Init error: {ex}");
            throw;
        }
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
}
