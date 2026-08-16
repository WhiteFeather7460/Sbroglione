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

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = AppSettingsStore.Load(StorePath);
        Assert.True(settings.AutoParallelism);
        Assert.True(settings.VerifyChecksumAfterCopy);
        Assert.Equal("Default", settings.ThemeVariant);
    }

    [Fact]
    public async Task Load_ThenSaveAsync_RoundTripsAllFields()
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
        var loaded = AppSettingsStore.Load(StorePath);

        Assert.False(loaded.AutoParallelism);
        Assert.Equal(12, loaded.ManualParallelism);
        Assert.Equal(4 * 1024 * 1024, loaded.BufferSizeBytes);
        Assert.False(loaded.VerifyChecksumAfterCopy);
        Assert.Equal("Dark", loaded.ThemeVariant);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "{ non-json !!!");

        var settings = AppSettingsStore.Load(StorePath);
        Assert.True(settings.AutoParallelism);
    }

    [Fact]
    public void LoadCurrent_UsesCurrentPath()
    {
        AppSettingsStore.CurrentPath = StorePath;
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath,
            System.Text.Json.JsonSerializer.Serialize(new AppSettings { ManualParallelism = 9 }));

        AppSettingsStore.LoadCurrent();

        Assert.Equal(9, AppSettingsStore.Current.ManualParallelism);
    }

    [Theory]
    [InlineData(0, 262144)]
    [InlineData(999999999, 16777216)]
    [InlineData(-5, 262144)]
    public async Task LoadAsync_OutOfRangeBufferSizeBytes_IsClamped(int rawValue, int expectedClamped)
    {
        string json = $"{{\"BufferSizeBytes\":{rawValue}}}";
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, json);

        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.Equal(expectedClamped, loaded.BufferSizeBytes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(999, 32)]
    public async Task LoadAsync_OutOfRangeManualParallelism_IsClamped(int rawValue, int expectedClamped)
    {
        string json = $"{{\"ManualParallelism\":{rawValue}}}";
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, json);

        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.Equal(expectedClamped, loaded.ManualParallelism);
    }

    [Theory]
    [InlineData(0, 262144)]
    [InlineData(999999999, 16777216)]
    public void Load_OutOfRangeBufferSizeBytes_IsClamped(int rawValue, int expectedClamped)
    {
        string json = $"{{\"BufferSizeBytes\":{rawValue}}}";
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, json);

        var loaded = AppSettingsStore.Load(StorePath);

        Assert.Equal(expectedClamped, loaded.BufferSizeBytes);
    }

    [Fact]
    public async Task SaveCurrentAsync_ConcurrentCalls_DoNotThrowAndLeaveValidFile()
    {
        AppSettingsStore.CurrentPath = StorePath;

        var tasks = Enumerable.Range(0, 5).Select(i =>
        {
            AppSettingsStore.Current = new AppSettings { ManualParallelism = i + 1 };
            return AppSettingsStore.SaveCurrentAsync();
        }).ToArray();

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));

        Assert.Null(exception);

        var loaded = await AppSettingsStore.LoadAsync(StorePath);
        Assert.InRange(loaded.ManualParallelism, 1, 32);
        Assert.False(File.Exists(StorePath + ".tmp"));
    }
}
