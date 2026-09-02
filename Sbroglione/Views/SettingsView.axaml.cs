using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>Scheda "Impostazioni": parametri di copia e aspetto.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private static ColorTheme? ThemeOf(object? sender) =>
        (sender as Control)?.DataContext as ColorTheme;

    private void OnApplyThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            vm.ApplyCustomTheme(theme);
    }

    private async void OnEditThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            await OpenEditorAsync(vm, theme);
    }

    private async void OnNewFromLightClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await OpenEditorAsync(vm, vm.CreateThemeFrom(BuiltInThemes.Light));
    }

    private async void OnNewFromDarkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await OpenEditorAsync(vm, vm.CreateThemeFrom(BuiltInThemes.Dark));
    }

    private async void OnDeleteThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            await vm.DeleteThemeAsync(theme);
    }

    private async void OnExportThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || ThemeOf(sender) is not { } theme)
            return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Tr("Str.Settings.ExportThemeDialogTitle"),
            SuggestedFileName = theme.Name + ".json",
            FileTypeChoices = [new FilePickerFileType(LocalizationService.Tr("Str.Settings.ThemeJsonFileType")) { Patterns = ["*.json"] }]
        });
        if (file?.TryGetLocalPath() is { } path)
            await vm.ExportThemeAsync(theme, path);
    }

    private async void OnImportThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Tr("Str.Settings.ImportThemeDialogTitle"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(LocalizationService.Tr("Str.Settings.ThemeJsonFileType")) { Patterns = ["*.json"] }]
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await vm.ImportThemeAsync(path);
    }

    /// <summary>
    /// Apre l'editor con anteprima live sul tema indicato. All'annullamento ripristina lo
    /// stato precedente (tema custom attivo o variante base); al salvataggio attiva il tema.
    /// </summary>
    private async Task OpenEditorAsync(SettingsViewModel vm, ColorTheme theme)
    {
        ColorTheme? previousActive = vm.ActiveCustomTheme;
        var editorVm = new ThemeEditorViewModel(theme);
        ThemeService.Apply(editorVm.WorkingTheme);

        ColorTheme? saved = await ThemeEditorHelper.ShowAsync(editorVm);

        if (saved is not null)
        {
            vm.OnThemeSaved(saved);
            vm.ApplyCustomTheme(saved);
        }
        else if (previousActive is not null)
        {
            ThemeService.Apply(previousActive);
        }
        else
        {
            ThemeService.Revert(AppSettingsStore.Current.ThemeVariant);
        }
    }
}
