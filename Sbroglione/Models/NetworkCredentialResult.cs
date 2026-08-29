namespace Sbroglione.Models;

/// <summary>Esito confermato del dialog credenziali di rete.</summary>
public sealed record NetworkCredentialResult(string Username, string Password, bool Remember);
