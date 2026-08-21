using System;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Backend usato quando nessun keyring è disponibile: la password viene chiesta
/// a ogni connessione e mai salvata (nessun fallback su file, per scelta di design).
/// </summary>
public sealed class NullCredentialStore : ICredentialStore
{
    public bool IsAvailable => false;

    public Task<string?> GetPasswordAsync(Guid profileId) => Task.FromResult<string?>(null);

    public Task SetPasswordAsync(Guid profileId, string password) => Task.CompletedTask;

    public Task DeletePasswordAsync(Guid profileId) => Task.CompletedTask;
}
