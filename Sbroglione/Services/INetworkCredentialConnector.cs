namespace Sbroglione.Services;

/// <summary>
/// Connessione autenticata a una radice UNC (\\server\condivisione). L'implementazione
/// Windows usa WNetAddConnection2 con CONNECT_UPDATE_PROFILE: le credenziali sono poi
/// gestite da Windows stesso (Credential Manager), non da questa app.
/// </summary>
public interface INetworkCredentialConnector
{
    /// <summary>False fuori da Windows: nessuna chiamata a <see cref="Connect"/> va fatta in quel caso.</summary>
    bool IsSupported { get; }

    /// <returns>0 in caso di successo, altrimenti un codice di errore Win32.</returns>
    int Connect(string uncRoot, string username, string password, bool persist);
}
