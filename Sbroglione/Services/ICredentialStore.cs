using System;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Accesso al keyring del sistema operativo per le password dei profili.
/// Chiave logica: servizio "Sbroglione" + Guid del profilo.
/// </summary>
public interface ICredentialStore
{
    /// <summary>False se sul sistema non c'è un keyring utilizzabile.</summary>
    bool IsAvailable { get; }

    /// <summary>Password salvata per il profilo, o null se assente.</summary>
    Task<string?> GetPasswordAsync(Guid profileId);

    Task SetPasswordAsync(Guid profileId, string password);

    Task DeletePasswordAsync(Guid profileId);
}
