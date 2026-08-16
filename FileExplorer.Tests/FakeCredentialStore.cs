using FileExplorer.Services;

namespace FileExplorer.Tests;

/// <summary>
/// Keyring simulato per i test: password in memoria, fallimenti configurabili e
/// tracciamento delle cancellazioni.
/// </summary>
public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<Guid, string> _passwords = new();

    public bool IsAvailable { get; init; } = true;

    /// <summary>Simula un backend che fallisce la scrittura (es. API Windows in errore).</summary>
    public bool ThrowOnSet { get; init; }

    /// <summary>Simula un backend che fallisce la cancellazione.</summary>
    public bool ThrowOnDelete { get; init; }

    /// <summary>Profili per cui è stata richiesta la cancellazione della password.</summary>
    public List<Guid> DeletedProfiles { get; } = new();

    public void Store(Guid profileId, string password) => _passwords[profileId] = password;

    public Task<string?> GetPasswordAsync(Guid profileId) =>
        Task.FromResult(_passwords.TryGetValue(profileId, out string? password) ? password : null);

    public Task SetPasswordAsync(Guid profileId, string password)
    {
        // Eccezione sincrona: è il caso peggiore per un chiamante async void.
        if (ThrowOnSet)
            throw new InvalidOperationException("Scrittura nel keyring fallita (simulata).");

        _passwords[profileId] = password;
        return Task.CompletedTask;
    }

    public Task DeletePasswordAsync(Guid profileId)
    {
        DeletedProfiles.Add(profileId);
        if (ThrowOnDelete)
            throw new InvalidOperationException("Cancellazione dal keyring fallita (simulata).");

        _passwords.Remove(profileId);
        return Task.CompletedTask;
    }
}
