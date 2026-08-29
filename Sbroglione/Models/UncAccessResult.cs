namespace Sbroglione.Models;

/// <summary>Esito del probe di accesso a una radice UNC (\\server\condivisione).</summary>
public enum UncAccessResult
{
    Ok,
    AccessDenied,
    Unavailable
}
