using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
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
    private bool? _isNarrowDualPane;

    // Sotto questa larghezza le colonne del DataGrid di ogni pannello (icona/nome/size/data)
    // si schiacciano fino a non mostrare più il nome file: si passa da fianco-a-fianco a righe
    // impilate cosa' ogni pannello ha di nuovo tutta la larghezza disponibile.
    private const double DualPaneNarrowBreakpoint = 700;

    public RemoteBrowserView()
    {
        InitializeComponent();
        _credentialStore = CredentialStoreFactory.Create();
        _viewModel = new RemoteBrowserViewModel(
            RemoteClientFactory.Create, _credentialStore, ProfileStore.DefaultPath);
        DataContext = _viewModel;

        _localPane = new LocalPaneView();
        _remotePane = new RemotePanelContent { DataContext = _viewModel };
        _localPane.RemoteViewModel = _viewModel;
        _remotePane.GetLocalCurrentPath = () => _localPane.ViewModel.CurrentPath;

        // La destinazione dei download e il badge "su disco" seguono sempre la cartella
        // locale visibile: niente più selezione manuale della destinazione.
        _localPane.ViewModel.WhenAnyValue(vm => vm.CurrentPath)
            .Subscribe(path => _viewModel.DestinationFolder = path);
        // Serve al toggle mutuamente esclusivo Scarica/Carica cartella in base a dove
        // l'utente ha selezionato una cartella (locale o remota).
        _localPane.ViewModel.WhenAnyValue(vm => vm.SelectedItem)
            .Subscribe(item => _viewModel.LocalSelectionIsDirectory = item?.IsDirectory ?? false);

        _leftIsLocal = true;
        LeftPaneHost.Content = _localPane;
        RightPaneHost.Content = _remotePane;

        DualPaneGrid.SizeChanged += (_, e) => UpdateDualPaneLayout(e.NewSize.Width);

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

    private async void OnAcceptFingerprintClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AcceptFingerprintAsync();

    private void OnRejectFingerprintClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RejectFingerprint();

    private async void OnDownloadDirectoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DownloadCurrentDirectoryAsync();

    private async void OnUploadFolderClick(object? sender, RoutedEventArgs e)
    {
        string? result = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, currentPath: null);
        if (!string.IsNullOrWhiteSpace(result))
            await _viewModel.UploadFolderAsync(result);
    }

    private void OnCancelTransferClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.CancelDownload();
        _viewModel.CancelUpload();
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
        bool saved = await ProfileEditorHelper.ShowAsync(profile, _credentialStore);

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
        // Detach first: a Control can't be reparented directly onto another
        // host while still attached to its current one (Avalonia throws).
        LeftPaneHost.Content = null;
        RightPaneHost.Content = null;
        _leftIsLocal = !_leftIsLocal;
        LeftPaneHost.Content = _leftIsLocal ? (object)_localPane : _remotePane;
        RightPaneHost.Content = _leftIsLocal ? (object)_remotePane : _localPane;
    }

    private void UpdateDualPaneLayout(double width)
    {
        bool narrow = width > 0 && width < DualPaneNarrowBreakpoint;
        if (_isNarrowDualPane == narrow)
            return;
        _isNarrowDualPane = narrow;

        if (narrow)
        {
            DualPaneGrid.ColumnDefinitions = new ColumnDefinitions("*");
            DualPaneGrid.RowDefinitions = new RowDefinitions("*,Auto,*");

            Grid.SetColumn(LeftPaneHost, 0);
            Grid.SetRow(LeftPaneHost, 0);
            Grid.SetColumn(RightPaneHost, 0);
            Grid.SetRow(RightPaneHost, 2);

            PaneSplitter.IsVisible = false;
            Grid.SetColumn(SwapPanesButton, 0);
            Grid.SetRow(SwapPanesButton, 1);
            SwapPanesButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SwapPanesButton.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            SwapPanesButton.Margin = new Avalonia.Thickness(0, 4, 0, 4);
        }
        else
        {
            DualPaneGrid.ColumnDefinitions = new ColumnDefinitions("*,28,*");
            DualPaneGrid.RowDefinitions = new RowDefinitions("*");

            Grid.SetColumn(LeftPaneHost, 0);
            Grid.SetRow(LeftPaneHost, 0);
            Grid.SetColumn(RightPaneHost, 2);
            Grid.SetRow(RightPaneHost, 0);

            PaneSplitter.IsVisible = true;
            Grid.SetColumn(PaneSplitter, 1);
            Grid.SetRow(PaneSplitter, 0);
            Grid.SetColumn(SwapPanesButton, 1);
            Grid.SetRow(SwapPanesButton, 0);
            SwapPanesButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SwapPanesButton.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            SwapPanesButton.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        }
    }
}
