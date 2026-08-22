using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    public RemotePanelContent()
    {
        InitializeComponent();
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
