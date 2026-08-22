using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class RemoteBrowserView : UserControl
{
    // CredentialStoreFactory.Create() sonda il keyring di sistema nel costruttore e può
    // bloccare fino a ~3s: va creato una sola volta e condiviso tra la viewmodel e l'editor
    // profili, invece di essere ricreato a ogni apertura di "Gestisci profili"/"Nuovo profilo".
    private readonly ICredentialStore _credentialStore;
    private readonly RemoteBrowserViewModel _viewModel;
    private readonly LocalPaneView _localPane;
    private readonly RemotePanelContent _remotePane;
    private bool _leftIsLocal;

    public RemoteBrowserView()
    {
        InitializeComponent();
        _credentialStore = CredentialStoreFactory.Create();
        _viewModel = new RemoteBrowserViewModel(
            RemoteClientFactory.Create, _credentialStore, ProfileStore.DefaultPath);
        DataContext = _viewModel;

        _localPane = new LocalPaneView();
        _remotePane = new RemotePanelContent { DataContext = _viewModel };
        _leftIsLocal = true;
        LeftPaneHost.Content = _localPane;
        RightPaneHost.Content = _remotePane;

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

    private async void OnDownloadSelectedClick(object? sender, RoutedEventArgs e)
    {
        var selected = _remotePane.Grid.SelectedItems.Cast<RemoteEntryViewModel>().ToList();
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

    private async void OnUploadFilesClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        // Un file alla volta: stesso contratto di SelectPathDialog usato per la destinazione dei
        // download (nessun file picker nativo multi-selezione nell'app). Ripetibile per più file.
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: false,
                startPath: System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);

        // Senza elemento selezionato SelectPathDialog ritorna la cartella corrente: non è un file
        // valido da caricare, va ignorato invece di far fallire l'upload.
        if (!string.IsNullOrWhiteSpace(result) && !Directory.Exists(result))
            await _viewModel.UploadFilesAsync(new[] { result });
    }

    private async void OnUploadFolderClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: true,
                startPath: System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(result))
            await _viewModel.UploadFolderAsync(result);
    }

    private void OnCancelUploadClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelUpload();

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

    private void OnSwapPanesClick(object? sender, RoutedEventArgs e)
    {
        _leftIsLocal = !_leftIsLocal;
        LeftPaneHost.Content = _leftIsLocal ? (object)_localPane : _remotePane;
        RightPaneHost.Content = _leftIsLocal ? (object)_remotePane : _localPane;
    }

    private async void OnBreadcrumbSegmentClicked(object? sender, string path)
    {
        // Naviga solo se il percorso è cambiato: evita un elenco superfluo se l'utente
        // clicca il segmento finale (la cartella corrente).
        if (path != _viewModel.CurrentPath)
        {
            // RemoteBrowserViewModel non espone Navigate diretto a un percorso: riusa la
            // stessa strada di OpenDirectoryAsync passando per un giro breve su CurrentPath.
            await NavigateRemoteToAsync(path);
        }
    }

    private async Task NavigateRemoteToAsync(string path)
    {
        var target = _viewModel.Items.FirstOrDefault(i => i.Item.FullPath == path && i.IsDirectory);
        if (target is not null)
        {
            await _viewModel.OpenDirectoryAsync(target);
            return;
        }
        // Segmento non tra le voci correnti (es. radice, o un antenato più su): sale con
        // NavigateUpAsync finché CurrentPath combacia, che è già l'unica primitiva di
        // navigazione diretta esposta dalla viewmodel.
        while (_viewModel.CurrentPath != path && _viewModel.CurrentPath != "/")
            await _viewModel.NavigateUpAsync();
    }
}
