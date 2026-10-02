using Avalonia.Headless.XUnit;
using DatabaseMigrator.Core.Models;
using DatabaseMigrator.ViewModels;
using Microsoft.Data.SqlClient;

namespace DatabaseMigrator.UiTests;

/// <summary>
/// The customer scenario that started all this, driven through the real view model and the real services: SQL Server to
/// SQL Server, the target already holding the application's tables with their FOREIGN KEYs (Activiti's ACT_* tables), the
/// tables picked with a schema filter and "Seleziona tutto", then "Avvia Migrazione". Needs the SQL Server fixture container
/// (127.0.0.1:1433), so it runs only with DBMIGRATOR_RUN_E2E=true; the Linux E2E matrix cannot build this Windows-only project.
/// </summary>
public class FullRunE2ETests
{
    private static bool ShouldRunE2E() =>
        string.Equals(Environment.GetEnvironmentVariable("DBMIGRATOR_RUN_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static string ConnectionString(string database) =>
        $"Server=127.0.0.1,1433;Database={database};User Id=sa;Password=SqlServer@123;TrustServerCertificate=True;Encrypt=True";

    private static async Task ExecAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Trait("Category", "E2E")]
    [AvaloniaFact]
    public async Task ASchemaPickedWithTheFilter_IsMigratedIntoATargetThatEnforcesItsForeignKeys_AndCanBeRunAgain()
    {
        if (!ShouldRunE2E()) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        string source = $"uie2e_src_{id}", target = $"uie2e_tgt_{id}";
        try
        {
            await ExecAsync("master", $"CREATE DATABASE [{source}]");
            await ExecAsync("master", $"CREATE DATABASE [{target}]");

            // Alphabetically ACT_GE_BYTEARRAY comes before ACT_RE_DEPLOYMENT, the table it points at: loading in list order fails.
            const string schema = "CREATE SCHEMA ASM_DATI_GW;";
            const string columns = @"
                CREATE TABLE ASM_DATI_GW.ACT_RE_DEPLOYMENT (ID_ INT NOT NULL PRIMARY KEY, NAME_ VARCHAR(50) NOT NULL);
                CREATE TABLE ASM_DATI_GW.ACT_GE_BYTEARRAY (ID_ INT NOT NULL PRIMARY KEY, DEPLOYMENT_ID_ INT NULL, NAME_ VARCHAR(50) NOT NULL);
                CREATE TABLE dbo.Unrelated (id INT NOT NULL PRIMARY KEY, v VARCHAR(50) NOT NULL);";
            await ExecAsync(source, schema);
            await ExecAsync(source, columns + @"
                INSERT ASM_DATI_GW.ACT_RE_DEPLOYMENT VALUES (1, 'd1'), (2, 'd2'), (3, 'd3');
                INSERT ASM_DATI_GW.ACT_GE_BYTEARRAY VALUES (1, 1, 'b1'), (2, 1, 'b2'), (3, 2, 'b3'), (4, 3, 'b4'), (5, 3, 'b5');
                INSERT dbo.Unrelated VALUES (1, 'source');");
            await ExecAsync(target, schema);
            await ExecAsync(target, columns + @"
                ALTER TABLE ASM_DATI_GW.ACT_GE_BYTEARRAY ADD CONSTRAINT ACT_FK_BYTEARR_DEPL
                    FOREIGN KEY (DEPLOYMENT_ID_) REFERENCES ASM_DATI_GW.ACT_RE_DEPLOYMENT (ID_);
                INSERT dbo.Unrelated VALUES (1, 'target-keeps-this');");

            var viewModel = new MainWindowViewModel();
            foreach (var (connection, database) in new[] { (viewModel.SourceConnection!, source), (viewModel.TargetConnection!, target) })
            {
                connection.SelectedDatabaseType = DatabaseType.SqlServer;
                connection.Server = "127.0.0.1";
                connection.Port = 1433;
                connection.Database = database;
                connection.Username = "sa";
                connection.Password = "SqlServer@123";
                connection.TrustServerCertificate = true;
            }

            await Ui.RunAsync(viewModel.ConnectDatabasesCommand);
            await Ui.WaitUntilAsync(() => viewModel.IsConnected && !viewModel.IsMigrating, "the connection", timeoutMs: 60000);
            Assert.Equal(3, viewModel.Tables.Count);

            viewModel.TableSearchFilter = "ASM_DATI_GW";
            await viewModel.SelectAllTablesDirectlyAsync();
            Assert.Equal(2, viewModel.SelectedTablesCount); // dbo.Unrelated is not selected
            viewModel.SelectedMigrationMode = MigrationMode.DataOnly;

            for (int run = 1; run <= 2; run++) // the second run goes over a populated target
            {
                await Ui.RunAsync(viewModel.StartMigrationCommand);
                await Ui.WaitUntilAsync(() => !viewModel.IsMigrating, $"run {run} to finish", timeoutMs: 120000);

                Assert.False(viewModel.ErrorMessage.Contains("rrore", StringComparison.Ordinal), $"run {run}: {viewModel.ErrorMessage}");
                Assert.Equal(3, await ScalarAsync(target, "SELECT COUNT(*) FROM ASM_DATI_GW.ACT_RE_DEPLOYMENT"));
                Assert.Equal(5, await ScalarAsync(target, "SELECT COUNT(*) FROM ASM_DATI_GW.ACT_GE_BYTEARRAY"));
                Assert.Equal(1, await ScalarAsync(target, "SELECT COUNT(*) FROM dbo.Unrelated WHERE v = 'target-keeps-this'")); // untouched
                Assert.Equal(0, await ScalarAsync(target, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1"));
            }
        }
        finally
        {
            SqlConnection.ClearAllPools(); // pooled connections would keep the databases from being dropped
            foreach (var database in new[] { source, target })
            {
                try
                {
                    await ExecAsync("master", $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
                }
                catch
                {
                    // Best effort: a leftover scratch database must not mask the real test result.
                }
            }
        }
    }
}
