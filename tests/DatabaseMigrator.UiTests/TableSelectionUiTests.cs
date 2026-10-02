using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.Views;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The "selezione tabelle" tab as the user drives it: "Seleziona tutto" with a filter, "Aggiorna" while still clicking, and
/// the question asked before a migration when the filter hides selected tables.
/// </summary>
public class TableSelectionUiTests
{
    private static FakeDatabaseService ServiceWith(params string[] tables) =>
        new() { GetTables = _ => Task.FromResult(Vm.Tables(tables)) };

    [AvaloniaFact]
    public async Task SelectAllWithAFilter_SelectsOnlyTheTablesShown_AndKeepsAccumulating()
    {
        var viewModel = await Vm.ConnectedAsync(ServiceWith("sales.Orders", "sales.Items", "hr.People"));

        viewModel.TableSearchFilter = "sales";
        await viewModel.SelectAllTablesDirectlyAsync();

        Assert.Equal(["sales.Items", "sales.Orders"], Vm.Selected(viewModel));
        Assert.Equal(2, viewModel.SelectedTablesCount);
        Assert.Equal(2, viewModel.FilteredTables.Count);

        viewModel.TableSearchFilter = "hr";
        await viewModel.SelectAllTablesDirectlyAsync();

        Assert.Equal(["hr.People", "sales.Items", "sales.Orders"], Vm.Selected(viewModel));
        Assert.Equal(3, viewModel.SelectedTablesCount);
    }

    [AvaloniaFact]
    public async Task DeselectAll_AlsoClearsTheTablesTheFilterHides()
    {
        var viewModel = await Vm.ConnectedAsync(ServiceWith("sales.Orders", "sales.Items", "hr.People"));
        await viewModel.SelectAllTablesDirectlyAsync();
        viewModel.TableSearchFilter = "hr";

        await viewModel.DeselectAllTablesDirectlyAsync();

        Assert.Empty(Vm.Selected(viewModel));
        Assert.Equal(0, viewModel.SelectedTablesCount);
    }

    [AvaloniaFact]
    public async Task Refresh_KeepsWhatTheUserSelectedWhileTheReloadWasRunning()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var service = new FakeDatabaseService
        {
            GetTables = async _ =>
            {
                if (++calls > 1) // the first call is the connection; the second one is the reload, which waits for the test
                    await gate.Task;
                return Vm.Tables("sales.Orders", "sales.Items", "hr.People"); // always new instances, nothing selected
            }
        };
        var viewModel = await Vm.ConnectedAsync(service);
        viewModel.Tables.Single(t => t.TableName == "Orders").IsSelected = true;

        var refresh = viewModel.RefreshTablesAsync();
        Assert.True(viewModel.IsMigrating);

        // Meanwhile the user keeps working on the tables still on screen: deselects one and selects another.
        viewModel.Tables.Single(t => t.TableName == "Orders").IsSelected = false;
        viewModel.Tables.Single(t => t.TableName == "People").IsSelected = true;
        gate.SetResult();
        await refresh;

        Assert.Equal(["hr.People"], Vm.Selected(viewModel));
        Assert.Equal(1, viewModel.SelectedTablesCount);
        Assert.False(viewModel.IsMigrating);
    }

    [AvaloniaFact]
    public async Task Refresh_RecomputesTheListAfterAFailedReloadAndAFilterTypedMeanwhile()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var service = new FakeDatabaseService
        {
            GetTables = async _ =>
            {
                if (++calls == 1)
                    return Vm.Tables("sales.Orders", "sales.Items", "hr.People");
                await gate.Task;
                throw new InvalidOperationException("source unreachable");
            }
        };
        var viewModel = await Vm.ConnectedAsync(service);

        var refresh = viewModel.RefreshTablesAsync();
        viewModel.TableSearchFilter = "hr"; // typed during the reload, when the list does not update itself
        gate.SetResult();
        await refresh;

        Assert.Single(viewModel.FilteredTables);
        Assert.Contains("source unreachable", viewModel.ErrorMessage);
        Assert.False(viewModel.IsMigrating);
    }

    [AvaloniaTheory]
    [InlineData(MigrationMode.SchemaAndData, true)]
    [InlineData(MigrationMode.DataOnly, true)]
    [InlineData(MigrationMode.SchemaOnly, false)]
    public async Task StartingWithSelectedTablesHiddenByTheFilter_AsksFirst_AndCancelTouchesNothing(MigrationMode mode, bool replacesData)
    {
        var service = ServiceWith("sales.Orders", "sales.Items", "hr.People");
        var viewModel = await Vm.ConnectedAsync(service);
        var asked = new List<(int Selected, int Hidden, bool ReplacesData)>();
        viewModel.ConfirmHiddenTablesAsync = (selected, hidden, replaces) =>
        {
            asked.Add((selected, hidden, replaces));
            return Task.FromResult(false);
        };
        viewModel.SelectedMigrationMode = mode;
        viewModel.TableSearchFilter = "sales";
        await viewModel.SelectAllTablesDirectlyAsync();
        viewModel.TableSearchFilter = "hr"; // the two selected tables are now hidden
        viewModel.ErrorMessage = "errore precedente";

        await Ui.RunAsync(viewModel.StartMigrationCommand);

        Assert.Equal([(2, 2, replacesData)], asked);
        Assert.Equal("Migrazione annullata", viewModel.StatusMessage);
        Assert.Equal("errore precedente", viewModel.ErrorMessage); // nothing ran: the earlier state is put back
        Assert.False(viewModel.IsMigrating);
        Assert.Equal(0, service.DatabaseExistsCalls); // the target was never touched
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheQuestionAboutHiddenTables_DefaultsToCancelWhenDataIsReplaced(bool replacesData)
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var answer = viewModel.ConfirmHiddenTablesAsync!(3, 1, replacesData);
            var dialog = await Ui.WaitForAsync(() => window.OwnedWindows.FirstOrDefault(), "the question");
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
            var include = buttons.Single(b => (string?)b.Content == "Includi e continua");
            var cancel = buttons.Single(b => (string?)b.Content == "Annulla");

            Assert.Equal(!replacesData, include.IsDefault); // Enter confirms only when nothing is overwritten
            Assert.Equal(replacesData, cancel.IsDefault);   // ...and cancels when the target's data would be replaced
            Assert.True(cancel.IsCancel);

            Ui.Click(cancel);
            Assert.False(await answer);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task TheQuestionAboutHiddenTables_CanBeAskedAgainAfterBeingCancelled()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var answer = viewModel.ConfirmHiddenTablesAsync!(3, 1, true);
                var dialog = await Ui.WaitForAsync(() => window.OwnedWindows.FirstOrDefault(), $"the question, attempt {attempt}");

                Ui.Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == "Annulla"));

                var finished = await Task.WhenAny(answer, Task.Delay(5000));
                Assert.True(finished == answer, $"The question never closed on attempt {attempt}");
                Assert.False(await answer);
            }
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }

    [AvaloniaFact]
    public async Task TheFailedEmptyingDialog_OpensWithBothButtons_AndCancelAbortsTheMigration()
    {
        var (window, _) = await Ui.OpenWindowAsync();
        try
        {
            // The longest refusal the pre-checks write: three table names and the advice about what "Continua" does. The headless
            // fonts are narrower than the real ones, so this cannot prove the text fits (the dialog is 420 px high for that).
            string message = "DELETE FROM MIGRATION_TEST.ACT_RE_DEPLOYMENT cancellerebbe o modificherebbe anche righe di tabelle con dati " +
                "che questa migrazione non carica dopo di essa (chiavi esterne ON DELETE CASCADE / SET NULL): " +
                "ASM_DATI_GW.ACT_GE_BYTEARRAY, ASM_DATI_GW.ACT_HI_ACTINST, ASM_DATI_GW.ACT_HI_PROCINST e altre 7. " +
                "Selezionale per la migrazione oppure svuotale tu. Se continui, i dati vengono aggiunti a quelli già presenti nella " +
                "tabella, senza svuotarla.";
            var show = typeof(MainWindow).GetMethod("ShowTruncateFailedDialogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var answer = (Task<bool>)show.Invoke(window, new object[] { new TruncateFailureContext("MIGRATION_TEST", "ACT_RE_DEPLOYMENT", message) })!;
            var dialog = await Ui.WaitForAsync(() => window.OwnedWindows.FirstOrDefault(), "the dialog");
            dialog.UpdateLayout();

            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);
            foreach (var button in buttons)
            {
                double bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), dialog)!.Value.Y;
                Assert.True(bottom <= dialog.ClientSize.Height, $"The {button.Content} button ends at {bottom} in a {dialog.ClientSize.Height}px dialog");
            }

            Ui.Click(buttons.Single(b => (string?)b.Content == "Annulla"));
            Assert.False(await answer);
        }
        finally
        {
            Ui.Reset(window, (await Ui.OpenWindowAsync()).ViewModel);
        }
    }

    [AvaloniaFact]
    public async Task TheQuestionAboutHiddenTables_ReturnsTrueWhenTheUserIncludesThem()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            var answer = viewModel.ConfirmHiddenTablesAsync!(3, 1, true);
            var dialog = await Ui.WaitForAsync(() => window.OwnedWindows.FirstOrDefault(), "the question");

            Ui.Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == "Includi e continua"));

            Assert.True(await answer);
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }
}
