using ReactiveUI;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.ViewModels;

public class ConnectionViewModel : ViewModelBase
{
    private string _server = "";
    private int _port = 1433;
    private string _database = "";
    private string _username = "";
    private string _password = "";
    private DatabaseType _selectedDatabaseType = DatabaseType.SqlServer;

    public string Server
    {
        get => _server;
        set => this.RaiseAndSetIfChanged(ref _server, value);
    }

    public int Port
    {
        get => _port;
        set => this.RaiseAndSetIfChanged(ref _port, value);
    }

    public string Database
    {
        get => _database;
        set => this.RaiseAndSetIfChanged(ref _database, value);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public DatabaseType SelectedDatabaseType
    {
        get => _selectedDatabaseType;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDatabaseType, value);
            // Aggiorna porta di default in base al tipo
            Port = value switch
            {
                DatabaseType.SqlServer => 1433,
                DatabaseType.Oracle => 1521,
                DatabaseType.PostgreSQL => 5432,
                _ => 1433
            };
        }
    }

    public ConnectionInfo? ConnectionInfo
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Server) || 
                string.IsNullOrWhiteSpace(Database) ||
                string.IsNullOrWhiteSpace(Username))
            {
                return null;
            }

            return new ConnectionInfo
            {
                DatabaseType = SelectedDatabaseType,
                Server = Server,
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password
            };
        }
    }
}
