using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>
/// Pannello remoto (lista file + progress + context menu): stesso markup che prima viveva
/// direttamente in RemoteBrowserView, estratto per essere ospitato nel dual-pane locale+remoto.
/// Nessun costruttore ViewModel proprio: il DataContext (la RemoteBrowserViewModel) viene
/// assegnato dal parent RemoteBrowserView.
/// </summary>
public partial class RemotePanelContent : UserControl
{
    /// <summary>Espone la griglia remota a RemoteBrowserView per letture di SelectedItem(s).</summary>
    public DataGrid Grid => RemoteGrid;

    private RemoteBrowserViewModel? ViewModel => DataContext as RemoteBrowserViewModel;

    /// <summary>Impostata dal genitore (RemoteBrowserView) dopo la costruzione: consente al
    /// doppio click su un file remoto di scaricarlo nella cartella locale corrente.</summary>
    public Func<string>? GetLocalCurrentPath { get; set; }

    // Colonne fisse (icona 44 + size 110 + modified 150 + su disco 120 = 424) che sotto
    // questa larghezza non lascerebbero più spazio alla colonna nome ("*"): si nascondono
    // "Data modifica" e "Su disco" per ridare respiro al nome file.
    private const double NarrowGridBreakpoint = 600;

    public RemotePanelContent()
    {
        InitializeComponent();
        RemoteGrid.AddHandler(InputElement.PointerPressedEvent, OnGridPointerPressedPreview, RoutingStrategies.Tunnel);
        RemoteGrid.SizeChanged += (_, e) =>
        {
            bool showSecondaryColumns = e.NewSize.Width >= NarrowGridBreakpoint;
            RemoteGrid.Columns[3].IsVisible = showSecondaryColumns;
            RemoteGrid.Columns[4].IsVisible = showSecondaryColumns;
        };
    }

    /// <summary>Intercetta il click in fase di tunnel, prima che il DataGrid applichi la sua
    /// selezione: se la riga sotto il puntatore è già selezionata, la deseleziona e marca
    /// l'evento come gestito così il DataGrid non la riseleziona subito dopo.</summary>
    private void OnGridPointerPressedPreview(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(RemoteGrid).Position;
        var row = RemoteGrid.GetVisualAt(point)?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.IsSelected ?? false)
        {
            e.Handled = true;
            RemoteGrid.SelectedIndex = -1;
        }
    }

    private async void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.NavigateUpAsync();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.RefreshAsync();
    }

    private async void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || ViewModel is not { } vm)
            return;

        await vm.NavigateToAsync(box.Text ?? string.Empty);
    }

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (RemoteGrid.SelectedItem is not RemoteEntryViewModel entry)
            return;

        if (entry.IsDirectory)
            await vm.OpenDirectoryAsync(entry);
        else if (GetLocalCurrentPath is { } getLocalPath)
            await vm.DownloadEntryToFolderAsync(entry, getLocalPath());
    }

    private async void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(RemoteGrid).Properties.IsLeftButtonPressed)
            return;
        if (RemoteGrid.SelectedItem is not RemoteEntryViewModel { IsDirectory: false } entry)
            return;

        var data = new DataObject();
        data.Set("sbroglione/remote-item", entry);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }


    private void OnGridDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("sbroglione/local-file-path")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnGridDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("sbroglione/local-file-path") is not string localPath)
            return;
        if (ViewModel is { IsConnected: true } vm)
            await vm.UploadFilesAsync(new[] { localPath });
    }

    private async void OnRemoteNewFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        string? name = await InputDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.RemoteBrowser.NewFolderTitle"),
            LocalizationService.Tr("Str.RemoteBrowser.NewFolderMessage"), null);
        if (!string.IsNullOrWhiteSpace(name))
            await vm.CreateFolderAsync(name);
    }

    private async void OnRemoteRenameClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (RemoteGrid.SelectedItem is not RemoteEntryViewModel entry)
            return;

        string? name = await InputDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.RemoteBrowser.RenameTitle"),
            LocalizationService.Tr("Str.RemoteBrowser.RenameMessage"), entry.Item.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != entry.Item.Name)
            await vm.RenameSelectedAsync(entry, name);
    }

    private async void OnRemoteDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (RemoteGrid.SelectedItem is not RemoteEntryViewModel entry)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.RemoteBrowser.DeleteConfirmTitle"),
            string.Format(LocalizationService.Tr("Str.RemoteBrowser.DeleteConfirmMessageFormat"), entry.Item.Name),
            LocalizationService.Tr("Str.RemoteBrowser.Delete"));
        if (confirmed)
            await vm.DeleteSelectedAsync(entry);
    }
}
