using System.Runtime.InteropServices;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyServiceTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;

    public DeltaCopyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltasvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings { DeltaBlockSizeKB = 1 }; // blocchi da 1KB nei test
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task TryDeltaCopyAsync_DestinationMissing_ReturnsFalse()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, new byte[3000]);
        string destPath = Path.Combine(_root, "dest.bin");

        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.False(result);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task TryDeltaCopyAsync_SourceAndDestSamePath_ReturnsFalseAndLeavesFileIntact()
    {
        string path = Path.Combine(_root, "same.bin");
        byte[] content = { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, content);

        bool result = await DeltaCopyService.TryDeltaCopyAsync(path, path, null, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task TryDeltaCopyAsync_SourceSmallerThanOneBlock_ReturnsFalse()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(sourcePath, new byte[10]);
        await File.WriteAllBytesAsync(destPath, new byte[10]);

        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryDeltaCopyAsync_ModifiedMiddleSection_ProducesByteExactResultAndReportsProgress()
    {
        byte[] original = new byte[5000];
        new Random(42).NextBytes(original);
        byte[] modified = (byte[])original.Clone();
        for (int i = 2000; i < 2100; i++) modified[i] = (byte)~modified[i];

        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(sourcePath, modified);
        await File.WriteAllBytesAsync(destPath, original);

        long reported = 0;
        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, len => reported += len, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(modified, await File.ReadAllBytesAsync(destPath));
        Assert.Equal(modified.Length, reported);
    }

    [Fact]
    public async Task TryDeltaCopyAsync_ApplyFailsAfterSignatureBuild_PropagatesException()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
            return; // chmod-based denial non applicabile.

        // La signature viene costruita con successo (BuildAsync legge solo destPath, che resta
        // leggibile), ma ApplyAsync fallisce nel creare il file temporaneo perché la cartella di
        // destinazione perde il permesso di scrittura DOPO che la signature è stata calcolata:
        // questo simula "la destinazione è cambiata/non è più scrivibile dopo il signature-build"
        // senza dover vincere una race condition, e verifica che l'eccezione si propaghi invece
        // di essere inghiottita in un `false` (la regressione di dc2520e).
        string destDir = Path.Combine(_root, "denied-dest-dir");
        Directory.CreateDirectory(destDir);
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(destDir, "dest.bin");
        var content = new byte[3000];
        new Random(7).NextBytes(content);
        await File.WriteAllBytesAsync(sourcePath, content);
        await File.WriteAllBytesAsync(destPath, content);

        File.SetUnixFileMode(destDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() =>
                DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None));
        }
        finally
        {
            File.SetUnixFileMode(destDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task TryDeltaCopyAsync_PreservesSourceLastWriteTime()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        var content = new byte[3000];
        await File.WriteAllBytesAsync(sourcePath, content);
        await File.WriteAllBytesAsync(destPath, content);
        var sourceTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceTime);

        await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(destPath));
    }
}
