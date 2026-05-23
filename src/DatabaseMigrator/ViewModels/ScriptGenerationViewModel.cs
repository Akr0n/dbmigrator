using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Core.Services;
using ReactiveUI;

namespace DatabaseMigrator.ViewModels;

/// <summary>
/// ViewModel del tab "Genera Script": elenco degli oggetti del database sorgente,
/// opzioni di esportazione e produzione del file .sql.
/// </summary>
public class ScriptGenerationViewModel : ViewModelBase
{
    private readonly ScriptGenerationService _service = new();
    private readonly List<DatabaseObject> _allObjects = new();
    private CompositeDisposable _selectionSubscriptions = new();

    private ConnectionInfo? _sourceConnection;

    private string _searchFilter = string.Empty;
    private DatabaseObjectType? _typeFilter;
    private DatabaseType _selectedDialect = DatabaseType.SqlServer;
    private bool _includeSchema = true;
    private bool _includeData = true;
    private bool _includeDropStatements;
    private bool _isBusy;
    private int _progressPercentage;
    private string _statusMessage = "Connettiti a un database e premi \"Carica oggetti\".";
    private int _selectedCount;
    private int _totalObjectsCount;

    public ScriptGenerationViewModel()
    {
        Objects = new ObservableCollection<DatabaseObject>();
    }

    /// <summary>Oggetti attualmente visibili (filtrati per ricerca e tipo).</summary>
    public ObservableCollection<DatabaseObject> Objects { get; }

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchFilter, value);
            ApplyFilter();
        }
    }

    public DatabaseType SelectedDialect
    {
        get => _selectedDialect;
        set => this.RaiseAndSetIfChanged(ref _selectedDialect, value);
    }

    public bool IncludeSchema
    {
        get => _includeSchema;
        set => this.RaiseAndSetIfChanged(ref _includeSchema, value);
    }

    public bool IncludeData
    {
        get => _includeData;
        set => this.RaiseAndSetIfChanged(ref _includeData, value);
    }

    public bool IncludeDropStatements
    {
        get => _includeDropStatements;
        set => this.RaiseAndSetIfChanged(ref _includeDropStatements, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        set => this.RaiseAndSetIfChanged(ref _progressPercentage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    public int TotalObjectsCount
    {
        get => _totalObjectsCount;
        private set => this.RaiseAndSetIfChanged(ref _totalObjectsCount, value);
    }

    /// <summary>Imposta la connessione sorgente da cui leggere gli oggetti.</summary>
    public void SetSourceConnection(ConnectionInfo? source)
    {
        _sourceConnection = source;
        if (source != null)
        {
            SelectedDialect = source.DatabaseType;
            StatusMessage = "Premi \"Carica oggetti\" per elencare gli oggetti del database sorgente.";
        }
    }

    /// <summary>Filtro per tipo di oggetto; null mostra tutti i tipi.</summary>
    public void SetTypeFilter(DatabaseObjectType? type)
    {
        _typeFilter = type;
        ApplyFilter();
    }

    /// <summary>Carica dal database sorgente l'elenco completo degli oggetti esportabili.</summary>
    public async Task LoadObjectsAsync()
    {
        if (_sourceConnection == null)
        {
            StatusMessage = "Nessuna connessione sorgente disponibile.";
            return;
        }

        try
        {
            IsBusy = true;
            ProgressPercentage = 0;
            StatusMessage = "Caricamento oggetti dal database sorgente...";
            LoggerService.Log("[ScriptGeneration] Caricamento oggetti dalla sorgente");

            var objects = await _service.GetDatabaseObjectsAsync(_sourceConnection);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _selectionSubscriptions.Dispose();
                _selectionSubscriptions = new CompositeDisposable();
                _allObjects.Clear();
                _allObjects.AddRange(objects);
                foreach (var obj in objects)
                {
                    _selectionSubscriptions.Add(
                        obj.WhenAnyValue(o => o.IsSelected).Subscribe(_ => RecomputeSelection()));
                }
                TotalObjectsCount = _allObjects.Count;
                ApplyFilter();
                RecomputeSelection();
            });

            StatusMessage = $"Caricati {_allObjects.Count} oggetti. Seleziona quelli da esportare.";
            LoggerService.Log($"[ScriptGeneration] Caricati {_allObjects.Count} oggetti");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante il caricamento: {ex.Message}";
            LoggerService.LogError("[ScriptGeneration] LoadObjectsAsync", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Genera lo script .sql per gli oggetti selezionati e lo salva nel percorso indicato.</summary>
    public async Task<bool> GenerateScriptAsync(string filePath)
    {
        if (_sourceConnection == null)
        {
            StatusMessage = "Nessuna connessione sorgente disponibile.";
            return false;
        }

        var selected = _allObjects.Where(o => o.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Seleziona almeno un oggetto da esportare.";
            return false;
        }
        if (!IncludeSchema && !IncludeData)
        {
            StatusMessage = "Abilita almeno una tra \"Includi schema\" e \"Includi dati\".";
            return false;
        }

        var options = new ScriptGenerationOptions
        {
            TargetDialect = SelectedDialect,
            IncludeSchema = IncludeSchema,
            IncludeData = IncludeData,
            IncludeDropStatements = IncludeDropStatements
        };

        var progress = new Progress<ScriptGenerationProgress>(p =>
        {
            ProgressPercentage = p.Percentage;
            StatusMessage = $"Generazione: {p.CurrentObject} " +
                            $"({p.ProcessedObjects}/{p.TotalObjects})";
        });

        try
        {
            IsBusy = true;
            ProgressPercentage = 0;
            StatusMessage = $"Generazione dello script ({selected.Count} oggetti)...";
            LoggerService.Log($"[ScriptGeneration] Generazione script -> {filePath} " +
                              $"({selected.Count} oggetti, dialetto {SelectedDialect})");

            // UTF-8 senza BOM: massima compatibilità con i client SQL dei tre database.
            await using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(false)))
            {
                await _service.GenerateScriptAsync(
                    _sourceConnection, selected, options, writer, progress, CancellationToken.None);
            }

            ProgressPercentage = 100;
            StatusMessage = $"Script generato: {Path.GetFileName(filePath)} ({selected.Count} oggetti).";
            LoggerService.Log($"[ScriptGeneration] Script generato in {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante la generazione: {ex.Message}";
            LoggerService.LogError("[ScriptGeneration] GenerateScriptAsync", ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectAll() => SetSelectionForVisible(true);

    public void DeselectAll() => SetSelectionForVisible(false);

    private void SetSelectionForVisible(bool selected)
    {
        // Agisce sugli oggetti attualmente visibili, così i filtri restringono l'operazione.
        foreach (var obj in Objects)
            obj.IsSelected = selected;
        RecomputeSelection();
    }

    private void RecomputeSelection()
    {
        SelectedCount = _allObjects.Count(o => o.IsSelected);
    }

    private void ApplyFilter()
    {
        IEnumerable<DatabaseObject> query = _allObjects;

        if (_typeFilter.HasValue)
            query = query.Where(o => o.ObjectType == _typeFilter.Value);

        string term = _searchFilter?.Trim() ?? string.Empty;
        if (term.Length > 0)
        {
            query = query.Where(o =>
                o.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.Schema.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList();
        Objects.Clear();
        foreach (var obj in filtered)
            Objects.Add(obj);
    }
}
