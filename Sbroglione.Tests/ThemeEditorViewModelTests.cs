using Avalonia.Media;
using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public class ThemeEditorViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly string _originalThemesDirectory;

    public ThemeEditorViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-themeeditor-" + Guid.NewGuid().ToString("N"));
        _originalThemesDirectory = ThemeStore.ThemesDirectory;
        ThemeStore.ThemesDirectory = _dir;
    }

    public void Dispose()
    {
        ThemeStore.ThemesDirectory = _originalThemesDirectory;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ThemeEditorViewModel MakeVm(out ColorTheme original)
    {
        original = BuiltInThemes.ForVariant("Light");
        original.Id = Guid.NewGuid().ToString("N");
        original.Name = "Base";
        original.IsBuiltIn = false;
        return new ThemeEditorViewModel(original) { LivePreview = false };
    }

    [Fact]
    public void Editor_works_on_a_clone_not_the_original()
    {
        ThemeEditorViewModel vm = MakeVm(out ColorTheme original);

        vm.Name = "Modificato";
        ThemeColorEntryViewModel accent = vm.Groups.SelectMany(g => g.Entries).First(e => e.Key == ThemeColorKeys.Accent);
        accent.Color = Colors.Lime;

        Assert.Equal("Base", original.Name);
        Assert.NotEqual("#FF00FF00", original.Colors[ThemeColorKeys.Accent]);
    }

    [Fact]
    public void Entries_cover_all_keys()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        var keys = vm.Groups.SelectMany(g => g.Entries).Select(e => e.Key).ToHashSet();
        foreach (string key in ThemeColorKeys.All)
            Assert.Contains(key, keys);
    }

    [Fact]
    public void Setting_entry_color_updates_working_theme_hex()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        ThemeColorEntryViewModel surface = vm.Groups.SelectMany(g => g.Entries).First(e => e.Key == ThemeColorKeys.Surface);

        surface.Color = Color.Parse("#123456");

        Assert.Equal("#FF123456", vm.WorkingTheme.Colors[ThemeColorKeys.Surface]);
    }

    [Fact]
    public async Task SaveAsync_persists_theme_with_edits()
    {
        ThemeEditorViewModel vm = MakeVm(out ColorTheme original);
        vm.Name = "Salvato";

        ColorTheme saved = await vm.SaveAsync();

        Assert.Equal(original.Id, saved.Id);
        ColorTheme? reloaded = ThemeStore.Load(original.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Salvato", reloaded.Name);
    }

    [Fact]
    public void IsDarkBase_maps_base_variant()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        Assert.False(vm.IsDarkBase);

        vm.IsDarkBase = true;

        Assert.Equal("Dark", vm.WorkingTheme.BaseVariant);
    }
}
