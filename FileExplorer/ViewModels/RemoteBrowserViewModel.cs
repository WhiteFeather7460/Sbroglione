using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Server remoto": connessione FTP/FTPS/SFTP, navigazione e download con filtri.
/// </summary>
public class RemoteBrowserViewModel : ViewModelBase
{
    private readonly Func<ConnectionProfile, IRemoteFileClient> _clientFactory;
    private readonly ICredentialStore _credentialStore;
    private readonly string _profilesFilePath;

    private IRemoteFileClient? _client;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    public ObservableCollection<RemoteEntryViewModel> Items { get; } = new();

    private ConnectionProfile? _selectedProfile;
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private string _currentPath = "/";
    public string CurrentPath
    {
        get => _currentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    private bool _isPasswordPromptVisible;
    public bool IsPasswordPromptVisible
    {
        get => _isPasswordPromptVisible;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordPromptVisible, value);
    }

    private string? _passwordInput;
    public string? PasswordInput
    {
        get => _passwordInput;
        set => this.RaiseAndSetIfChanged(ref _passwordInput, value);
    }

    public bool CanSavePassword => _credentialStore.IsAvailable;

    private bool _savePassword;
    public bool SavePassword
    {
        get => _savePassword;
        set => this.RaiseAndSetIfChanged(ref _savePassword, value);
    }

    private string? _pendingFingerprint;
    public string? PendingFingerprint
    {
        get => _pendingFingerprint;
        private set => this.RaiseAndSetIfChanged(ref _pendingFingerprint, value);
    }

    /// <summary>Costruttore per la view: dipendenze reali.</summary>
    public RemoteBrowserViewModel()
        : this(RemoteClientFactory.Create, CredentialStoreFactory.Create(), ProfileStore.DefaultPath)
    {
    }

    /// <summary>Costruttore testabile con dipendenze iniettate.</summary>
    public RemoteBrowserViewModel(
        Func<ConnectionProfile, IRemoteFileClient> clientFactory,
        ICredentialStore credentialStore,
        string profilesFilePath)
    {
        _clientFactory = clientFactory;
        _credentialStore = credentialStore;
        _profilesFilePath = profilesFilePath;
        _savePassword = credentialStore.IsAvailable;
    }

    /// <summary>Carica i profili salvati (chiamata dalla view all'avvio).</summary>
    public async Task LoadProfilesAsync()
    {
        var profiles = await ProfileStore.LoadAsync(_profilesFilePath);
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public async Task ConnectAsync()
    {
        if (SelectedProfile is null || IsBusy)
            return;

        ErrorMessage = null;
        PendingFingerprint = null;

        string? password = PasswordInput;
        if (string.IsNullOrEmpty(password))
            password = await _credentialStore.GetPasswordAsync(SelectedProfile.Id);

        if (string.IsNullOrEmpty(password))
        {
            IsPasswordPromptVisible = true;
            StatusMessage = _credentialStore.IsAvailable
                ? "Inserire la password."
                : "Keyring di sistema non disponibile: la password va inserita a ogni connessione.";
            return;
        }

        IsBusy = true;
        try
        {
            await DisposeClientAsync();
            var client = _clientFactory(SelectedProfile);
            var error = await client.ConnectAsync(SelectedProfile, password, CancellationToken.None);

            if (error is not null)
            {
                await client.DisposeAsync();
                ErrorMessage = error.Message;
                if (error.Kind == RemoteErrorKind.HostKeyMismatch)
                    PendingFingerprint = error.Fingerprint;
                return;
            }

            _client = client;
            IsConnected = true;
            IsPasswordPromptVisible = false;

            if (!string.IsNullOrEmpty(PasswordInput) && SavePassword && _credentialStore.IsAvailable)
                await _credentialStore.SetPasswordAsync(SelectedProfile.Id, PasswordInput);
            PasswordInput = null;

            CurrentPath = "/";
            await LoadListingAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        await DisposeClientAsync();
        IsConnected = false;
        Items.Clear();
        StatusMessage = "Disconnesso.";
    }

    public async Task OpenDirectoryAsync(RemoteEntryViewModel entry)
    {
        if (!entry.IsDirectory || _client is null)
            return;

        CurrentPath = entry.Item.FullPath;
        await LoadListingAsync();
    }

    public async Task NavigateUpAsync()
    {
        if (_client is null || CurrentPath == "/")
            return;

        int lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadListingAsync();
    }

    public Task RefreshAsync() => _client is null ? Task.CompletedTask : LoadListingAsync();

    public async Task AcceptFingerprintAsync()
    {
        if (SelectedProfile is null || PendingFingerprint is null)
            return;

        SelectedProfile.AcceptedHostKeyFingerprint = PendingFingerprint;
        PendingFingerprint = null;
        await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        await ConnectAsync();
    }

    public void RejectFingerprint()
    {
        PendingFingerprint = null;
        StatusMessage = "Connessione rifiutata: host key non accettata.";
    }

    private async Task LoadListingAsync()
    {
        if (_client is null)
            return;

        IsBusy = true;
        try
        {
            ErrorMessage = null;
            var result = await _client.ListDirectoryAsync(CurrentPath, CancellationToken.None);
            Items.Clear();

            if (result.Error is not null)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            foreach (var item in result.Items
                         .OrderByDescending(i => i.IsDirectory)
                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                Items.Add(new RemoteEntryViewModel(item));
            }

            RefreshLocalStatuses();
            StatusMessage = $"{Items.Count} elementi in {CurrentPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ricalcola la colonna "Su disco". Ridefinita/estesa nel task download.</summary>
    protected virtual void RefreshLocalStatuses()
    {
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    /// <summary>Client corrente (per il task download).</summary>
    protected IRemoteFileClient? Client => _client;

    /// <summary>Percorso del file profili (per il task download/editor).</summary>
    protected string ProfilesFilePath => _profilesFilePath;
}
