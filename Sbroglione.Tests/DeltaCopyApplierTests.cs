// Sbroglione.Tests/DeltaCopyApplierTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyApplierTests : IDisposable
{
    private readonly string _root;

    public DeltaCopyApplierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltaapply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public async Task ApplyAsync_MixedInstructions_ReconstructsExactBytes()
    {
        byte[] oldContent = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction>
        {
            new LiteralInstruction(new byte[] { 100, 101 }),
            new CopyBlockInstruction(DestOffset: 5, Length: 3),
            new LiteralInstruction(new byte[] { 102 })
        };

        long totalReported = 0;
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, len => totalReported += len, CancellationToken.None);

        byte[] result = await File.ReadAllBytesAsync(destPath);
        Assert.Equal(new byte[] { 100, 101, 5, 6, 7, 102 }, result);
        Assert.Equal(6, totalReported);
    }

    [Fact]
    public async Task ApplyAsync_NoTempFileLeftBehindAfterSuccess()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction> { new CopyBlockInstruction(0, 3) };
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, CancellationToken.None);

        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ApplyAsync_EmptyInstructions_ProducesEmptyFile()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        await DeltaCopyApplier.ApplyAsync(new List<DeltaCopyInstruction>(), destPath, destPath, null, CancellationToken.None);

        Assert.Empty(await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task ApplyAsync_Cancelled_DoesNotLeaveTempFile()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction> { new LiteralInstruction(new byte[] { 9 }) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, cts.Token));

        Assert.Single(Directory.GetFiles(_root)); // solo dest.bin originale, nessun .tmp orfano
    }

    [Fact]
    public async Task ApplyAsync_OldDestTruncatedAfterSignature_ThrowsAndCleansUpTempFile()
    {
        byte[] oldContent = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        // Signature calcolata quando il file aveva 10 byte: il blocco chiede 5 byte a offset 8,
        // ma nel frattempo il file viene troncato a 9 byte (offset 8 legge un solo byte).
        var instructions = new List<DeltaCopyInstruction> { new CopyBlockInstruction(DestOffset: 8, Length: 5) };
        await File.WriteAllBytesAsync(destPath, oldContent[..9]);

        await Assert.ThrowsAsync<IOException>(() =>
            DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, CancellationToken.None));

        Assert.Single(Directory.GetFiles(_root)); // solo dest.bin, nessun .tmp orfano
    }

    [Fact]
    public async Task ApplyAsync_PreservesUnixFileModeOfFinalDest()
    {
        if (OperatingSystem.IsWindows())
            return;

        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        const UnixFileMode expectedMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.SetUnixFileMode(destPath, expectedMode);

        var instructions = new List<DeltaCopyInstruction> { new CopyBlockInstruction(0, 3) };
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, CancellationToken.None);

        Assert.Equal(expectedMode, File.GetUnixFileMode(destPath));
    }
}
