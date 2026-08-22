using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class LocalPaneView : UserControl
{
    public LocalPaneViewModel ViewModel { get; }

    /// <summary>Impostata dal genitore (RemoteBrowserView) dopo la costruzione: consente al
    /// doppio click su un file locale di caricarlo nella cartella remota corrente.</summary>
    public RemoteBrowserViewModel? RemoteViewModel { get; set; }

    public LocalPaneView() : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public LocalPaneView(string startPath)
    {
        InitializeComponent();
        ViewModel = new LocalPaneViewModel(startPath);
        DataContext = ViewModel;
        Breadcrumb.Path = startPath;
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
        ViewModel.WhenAnyValue(vm => vm.CurrentPath).Subscribe(path => Breadcrumb.Path = path);
    }

    private async void OnNavigateUpClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.NavigateUpAsync();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();

    private async void OnBreadcrumbSegmentClicked(object? sender, string path) =>
        await ViewModel.NavigateToAsync(path);

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel.SelectedItem is not { } item)
            return;

        if (item.IsDirectory)
            await ViewModel.NavigateToAsync(item.FullPath);
        else if (RemoteViewModel is { IsConnected: true } remoteVm)
            await remoteVm.UploadFilesAsync(new[] { item.FullPath });
    }

    private async void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(LocalGrid).Properties.IsLeftButtonPressed)
            return;
        if (ViewModel.SelectedItem is not { IsDirectory: false } item)
            return;

        var data = new DataObject();
        data.Set("sbroglione/local-file-path", item.FullPath);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private void OnGridDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("sbroglione/remote-item")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnGridDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("sbroglione/remote-item") is not RemoteEntryViewModel entry)
            return;
        if (RemoteViewModel is { } remoteVm)
        {
            await remoteVm.DownloadEntryToFolderAsync(entry, ViewModel.CurrentPath);
            await ViewModel.RefreshAsync();
        }
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        string? name = await InputDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.LocalPane.NewFolderTitle"),
            LocalizationService.Tr("Str.LocalPane.NewFolderMessage"), null);
        if (!string.IsNullOrWhiteSpace(name))
            await ViewModel.CreateFolderAsync(name);
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not { } item)
            return;

        string? name = await InputDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.LocalPane.RenameTitle"),
            LocalizationService.Tr("Str.LocalPane.RenameMessage"), item.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != item.Name)
            await ViewModel.RenameSelectedAsync(name);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not { } item)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.LocalPane.DeleteConfirmTitle"),
            string.Format(LocalizationService.Tr("Str.LocalPane.DeleteConfirmMessageFormat"), item.Name),
            LocalizationService.Tr("Str.LocalPane.Delete"));
        if (confirmed)
            await ViewModel.DeleteSelectedAsync();
    }
}
