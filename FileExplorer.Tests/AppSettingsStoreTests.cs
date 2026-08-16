using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public AppSettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string StorePath => Path.Combine(_root, "sub", "settings.json");

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaults()
    {
        var settings = await AppSettingsStore.LoadAsync(StorePath);
        Assert.True(settings.AutoParallelism);
        Assert.True(settings.VerifyChecksumAfterCopy);
        Assert.Equal("Default", settings.ThemeVariant);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsAllFields()
    {
        var settings = new AppSettings
        {
            AutoParallelism = false,
            ManualParallelism = 12,
            BufferSizeBytes = 4 * 1024 * 1024,
            VerifyChecksumAfterCopy = false,
            ThemeVariant = "Dark"
        };

        await AppSettingsStore.SaveAsync(StorePath, settings);
        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.False(loaded.AutoParallelism);
        Assert.Equal(12, loaded.ManualParallelism);
        Assert.Equal(4 * 1024 * 1024, loaded.BufferSizeBytes);
        Assert.False(loaded.VerifyChecksumAfterCopy);
        Assert.Equal("Dark", loaded.ThemeVariant);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, "{ non-json !!!");

        var settings = await AppSettingsStore.LoadAsync(StorePath);
        Assert.True(settings.AutoParallelism);
    }

    [Fact]
    public async Task LoadCurrentAsync_UsesCurrentPath()
    {
        AppSettingsStore.CurrentPath = StorePath;
        await AppSettingsStore.SaveAsync(StorePath, new AppSettings { ManualParallelism = 9 });

        await AppSettingsStore.LoadCurrentAsync();

        Assert.Equal(9, AppSettingsStore.Current.ManualParallelism);
    }

    [Fact]
    public async Task SaveCurrentAsync_WritesCurrentToCurrentPath()
    {
        AppSettingsStore.CurrentPath = StorePath;
        AppSettingsStore.Current = new AppSettings { ManualParallelism = 15 };

        await AppSettingsStore.SaveCurrentAsync();
        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.Equal(15, loaded.ManualParallelism);
    }
}
