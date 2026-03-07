using System;
using System.Text.Json.Serialization;
using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Core.Models;

/// <summary>
/// Classe per serializzare/deserializzare configurazioni di connessione da file JSON
/// </summary>
public class ConnectionConfig
{
    [JsonPropertyName("source")]
    public DatabaseConnectionData? Source { get; set; }

    [JsonPropertyName("target")]
    public DatabaseConnectionData? Target { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Dati di una singola connessione al database
/// </summary>
public class DatabaseConnectionData
{
    [JsonPropertyName("databaseType")]
    public string DatabaseType { get; set; } = "SqlServer";

    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 1433;

    [JsonPropertyName("database")]
    public string Database { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("passwordProtected")]
    public bool PasswordProtected { get; set; }

    [JsonPropertyName("trustServerCertificate")]
    public bool TrustServerCertificate { get; set; } = RuntimeOptionsProvider.Current.Security.TrustServerCertificateByDefault;

    /// <summary>
    /// Converte in ConnectionInfo per l'uso interno
    /// </summary>
    public ConnectionInfo ToConnectionInfo()
    {
        string resolvedPassword = Password;
        if (PasswordProtected && !string.IsNullOrEmpty(Password))
        {
            if (!CredentialProtectionService.TryUnprotect(Password, out resolvedPassword))
            {
                throw new InvalidOperationException(
                    "Impossibile decifrare la password della configurazione. Verificare che il file sia stato creato dallo stesso utente Windows.");
            }
        }

        return new ConnectionInfo
        {
            DatabaseType = Enum.Parse<DatabaseType>(DatabaseType),
            Server = Server,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = resolvedPassword,
            TrustServerCertificate = TrustServerCertificate
        };
    }

    /// <summary>
    /// Crea un DatabaseConnectionData da un ConnectionInfo
    /// </summary>
    public static DatabaseConnectionData FromConnectionInfo(ConnectionInfo info)
    {
        string serializedPassword = info.Password;
        bool passwordProtected = false;

        if (!string.IsNullOrEmpty(info.Password))
        {
            if (CredentialProtectionService.TryProtect(info.Password, out var protectedPassword))
            {
                serializedPassword = protectedPassword;
                passwordProtected = true;
            }
            else if (!RuntimeOptionsProvider.Current.Security.AllowPlaintextConfigFallback)
            {
                throw new InvalidOperationException(
                    "Impossibile proteggere la password con DPAPI. Abilitare AllowPlaintextConfigFallback solo se strettamente necessario.");
            }
        }

        return new DatabaseConnectionData
        {
            DatabaseType = info.DatabaseType.ToString(),
            Server = info.Server,
            Port = info.Port,
            Database = info.Database,
            Username = info.Username,
            Password = serializedPassword,
            PasswordProtected = passwordProtected,
            TrustServerCertificate = info.TrustServerCertificate
        };
    }
}
