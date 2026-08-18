using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public class SettingsViewModelThemeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;
    private readonly string _originalThemesDirectory;

    public SettingsViewModelThemeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-vmthemes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalThemesDirectory = ThemeStore.ThemesDirectory;
        ThemeStore.ThemesDirectory = Path.Combine(_dir, "themes");
        _settingsPath = Path.Combine(_dir, "settings.json");
        AppSettingsStore.CurrentPath = _settingsPath;
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.CurrentPath = AppSettingsStore.DefaultPath;
        AppSettingsStore.Current = new AppSettings();
        ThemeStore.ThemesDirectory = _originalThemesDirectory;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static SettingsViewModel MakeVm() => new() { ApplyThemesToApplication = false };

    private static ColorTheme MakeCustom(string name)
    {
        ColorTheme theme = BuiltInThemes.ForVariant("Light");
        theme.Id = Guid.NewGuid().ToString("N");
        theme.Name = name;
        theme.IsBuiltIn = false;
        return theme;
    }

    [Fact]
    public async Task Constructor_loads_saved_custom_themes()
    {
        await ThemeStore.SaveAsync(MakeCustom("Mio"));

        SettingsViewModel vm = MakeVm();

        ColorTheme loaded = Assert.Single(vm.CustomThemes);
        Assert.Equal("Mio", loaded.Name);
    }

    [Fact]
    public async Task ApplyCustomTheme_sets_id_and_saves()
    {
        ColorTheme theme = MakeCustom("Attivo");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();

        vm.ApplyCustomTheme(vm.CustomThemes[0]);
        if (vm.LastSaveTask is not null)
            await vm.LastSaveTask;

        Assert.Equal(theme.Id, AppSettingsStore.Current.CustomThemeId);
        Assert.Equal(theme.Id, vm.ActiveCustomTheme?.Id);
    }

    [Fact]
    public async Task Selecting_base_variant_clears_custom_theme()
    {
        ColorTheme theme = MakeCustom("Attivo");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();
        vm.ApplyCustomTheme(vm.CustomThemes[0]);

        vm.IsThemeDark = true;
        if (vm.LastSaveTask is not null)
            await vm.LastSaveTask;

        Assert.Null(AppSettingsStore.Current.CustomThemeId);
        Assert.Equal("Dark", AppSettingsStore.Current.ThemeVariant);
    }

    [Fact]
    public void CreateThemeFrom_builtin_gives_editable_copy()
    {
        SettingsViewModel vm = MakeVm();

        ColorTheme copy = vm.CreateThemeFrom(BuiltInThemes.ForVariant("Dark"));

        Assert.False(copy.IsBuiltIn);
        Assert.NotEqual(BuiltInThemes.DarkId, copy.Id);
        Assert.Equal("Scuro (copia)", copy.Name);
    }

    [Fact]
    public async Task DeleteThemeAsync_active_theme_reverts_to_variant()
    {
        ColorTheme theme = MakeCustom("DaEliminare");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();
        vm.ApplyCustomTheme(vm.CustomThemes[0]);

        await vm.DeleteThemeAsync(vm.CustomThemes[0]);

        Assert.Null(AppSettingsStore.Current.CustomThemeId);
        Assert.Empty(vm.CustomThemes);
        Assert.Null(ThemeStore.Load(theme.Id));
    }

    [Fact]
    public async Task ImportThemeAsync_adds_and_persists()
    {
        ColorTheme theme = MakeCustom("DaImportare");
        string exportPath = Path.Combine(_dir, "tema.json");
        await ThemeStore.ExportAsync(theme, exportPath);
        SettingsViewModel vm = MakeVm();

        ColorTheme? imported = await vm.ImportThemeAsync(exportPath);

        Assert.NotNull(imported);
        Assert.Single(vm.CustomThemes);
        Assert.NotNull(ThemeStore.Load(imported.Id));
    }

    [Fact]
    public void OnThemeSaved_upserts_list_by_id()
    {
        SettingsViewModel vm = MakeVm();
        ColorTheme theme = MakeCustom("Nuovo");

        vm.OnThemeSaved(theme);
        Assert.Single(vm.CustomThemes);

        ColorTheme renamed = theme.Clone();
        renamed.Name = "Rinominato";
        vm.OnThemeSaved(renamed);

        ColorTheme only = Assert.Single(vm.CustomThemes);
        Assert.Equal("Rinominato", only.Name);
    }
}
