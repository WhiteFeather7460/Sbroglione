using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class RemoteBrowserView : UserControl
{
    // CredentialStoreFactory.Create() sonda il keyring di sistema nel costruttore e può
    // bloccare fino a ~3s: va creato una sola volta e condiviso tra la viewmodel e l'editor
    // profili, invece di essere ricreato a ogni apertura di "Gestisci profili"/"Nuovo profilo".
    private readonly ICredentialStore _credentialStore;
    private readonly RemoteBrowserViewModel _viewModel;

    public RemoteBrowserView()
    {
        InitializeComponent();
        _credentialStore = CredentialStoreFactory.Create();
        _viewModel = new RemoteBrowserViewModel(
            RemoteClientFactory.Create, _credentialStore, ProfileStore.DefaultPath);
        DataContext = _viewModel;
        // Loaded riscatta a ogni rientro della view nel visual tree (cambio scheda):
        // LoadProfilesAsync è idempotente, quindi solo la prima esecuzione carica davvero
        // e una connessione attiva non viene mai azzerata da un cambio scheda.
        Loaded += async (_, _) => await _viewModel.LoadProfilesAsync();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConnectAsync();

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DisconnectAsync();

    private async void OnDeleteProfileClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DeleteProfileAsync();

    private async void OnNavigateUpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.NavigateUpAsync();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void OnAcceptFingerprintClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AcceptFingerprintAsync();

    private void OnRejectFingerprintClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RejectFingerprint();

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RemoteGrid.SelectedItem is RemoteEntryViewModel entry && entry.IsDirectory)
            await _viewModel.OpenDirectoryAsync(entry);
    }

    private async void OnDownloadSelectedClick(object? sender, RoutedEventArgs e)
    {
        var selected = RemoteGrid.SelectedItems.Cast<RemoteEntryViewModel>().ToList();
        if (selected.Count > 0)
            await _viewModel.DownloadSelectedAsync(selected);
    }

    private async void OnDownloadDirectoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DownloadCurrentDirectoryAsync();

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelDownload();

    private async void OnBrowseDestinationClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        // Contratto verificato di SelectPathDialog: costruttore senza parametri,
        // DataContext assegnato dal chiamante, ShowDialog<string?> ritorna il percorso o null.
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: true,
                startPath: _viewModel.DestinationFolder
                           ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(result))
            _viewModel.DestinationFolder = result;
    }

    private async void OnNewProfileClick(object? sender, RoutedEventArgs e) =>
        await ManageProfileAsync(new ConnectionProfile(), isNew: true);

    private async void OnManageProfilesClick(object? sender, RoutedEventArgs e)
    {
        bool isNew = _viewModel.SelectedProfile is null;
        var profile = _viewModel.SelectedProfile ?? new ConnectionProfile();
        await ManageProfileAsync(profile, isNew);
    }

    /// <summary>
    /// Apre l'editor per il profilo indicato e, se salvato, aggiorna la lista profili e la persiste.
    /// </summary>
    private async Task ManageProfileAsync(ConnectionProfile profile, bool isNew)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        var editor = new ProfileEditorWindow(new ProfileEditorViewModel(profile, _credentialStore));
        bool saved = await editor.ShowDialog<bool>(owner);

        if (saved)
        {
            if (isNew)
                _viewModel.Profiles.Add(profile);
            // Il percorso arriva dalla viewmodel: view ed editor devono scrivere sullo stesso file.
            await ProfileStore.SaveAsync(_viewModel.ProfilesFilePath, _viewModel.Profiles.ToList());
            if (isNew)
                _viewModel.SelectedProfile = profile;
        }
    }
}
