using System;

namespace Sbroglione.Models;

/// <summary>Esito del controllo di una nuova versione su GitHub Releases.</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Error
}

/// <summary>
/// Dati sulla nuova versione trovata. <see cref="AssetDownloadUrl"/> è null quando non esiste
/// un asset compatibile con la piattaforma corrente (RID non Windows/Linux, o release senza
/// l'asset atteso): in quel caso il self-update non è tentato, resta solo il link alla release.
/// </summary>
public sealed record UpdateInfo(Version Version, string ReleaseUrl, string? AssetDownloadUrl, string? AssetFileName);

/// <summary>Risultato di <see cref="Services.UpdateCheckService.CheckAsync"/>.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info, string? ErrorMessage);
