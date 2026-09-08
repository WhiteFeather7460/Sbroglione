using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyScannerTests : IDisposable
{
    private readonly string _root;

    public DeltaCopyScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltascan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private async Task<string> WriteAsync(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    [Fact]
    public async Task ScanAsync_IdenticalContent_ProducesOnlyCopyBlockInstructions()
    {
        byte[] content = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        string destPath = await WriteAsync("dest.bin", content);
        string sourcePath = await WriteAsync("source.bin", content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        Assert.All(instructions, i => Assert.IsType<CopyBlockInstruction>(i));
        Assert.Equal(4, instructions.Count);
    }

    [Fact]
    public async Task ScanAsync_ByteInsertedAtStart_StillFindsShiftedBlocks()
    {
        byte[] original = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        byte[] shifted = new byte[] { 255 }.Concat(original).ToArray();
        string destPath = await WriteAsync("dest.bin", original);
        string sourcePath = await WriteAsync("source.bin", shifted);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        // Un byte letterale iniziale, poi i blocchi originali (shiftati di 1 nella sorgente
        // ma identici nel contenuto) devono essere trovati come CopyBlockInstruction.
        Assert.IsType<LiteralInstruction>(instructions[0]);
        Assert.Single((instructions[0] as LiteralInstruction)!.Data);
        Assert.Contains(instructions, i => i is CopyBlockInstruction);
    }

    [Fact]
    public async Task ScanAsync_MiddleBlockModified_ProducesLiteralForThatBlockOnly()
    {
        byte[] content = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        byte[] modified = (byte[])content.Clone();
        for (int i = 10; i < 20; i++) modified[i] = (byte)(255 - i);

        string destPath = await WriteAsync("dest.bin", content);
        string sourcePath = await WriteAsync("source.bin", modified);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        Assert.Equal(3, instructions.Count);
        Assert.IsType<CopyBlockInstruction>(instructions[0]);
        Assert.IsType<LiteralInstruction>(instructions[1]);
        Assert.Equal(10, ((LiteralInstruction)instructions[1]).Data.Length);
        Assert.IsType<CopyBlockInstruction>(instructions[2]);
    }

    [Fact]
    public async Task ScanAsync_SourceShorterThanDest_ReconstructsExactSourceContent()
    {
        byte[] destContent = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        byte[] sourceContent = destContent.Take(15).ToArray();

        string destPath = await WriteAsync("dest.bin", destContent);
        string sourcePath = await WriteAsync("source.bin", sourceContent);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        byte[] reconstructed = Reconstruct(instructions, destContent);
        Assert.Equal(sourceContent, reconstructed);
    }

    [Fact]
    public async Task ScanAsync_CompletelyDifferentContent_ProducesOnlyLiteralInstructions()
    {
        byte[] destContent = Enumerable.Repeat((byte)1, 20).ToArray();
        byte[] sourceContent = Enumerable.Repeat((byte)2, 20).ToArray();

        string destPath = await WriteAsync("dest.bin", destContent);
        string sourcePath = await WriteAsync("source.bin", sourceContent);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        byte[] reconstructed = Reconstruct(instructions, destContent);
        Assert.Equal(sourceContent, reconstructed);
    }

    private static byte[] Reconstruct(IReadOnlyList<DeltaCopyInstruction> instructions, byte[] oldDestContent)
    {
        using var output = new MemoryStream();
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case LiteralInstruction literal:
                    output.Write(literal.Data);
                    break;
                case CopyBlockInstruction block:
                    output.Write(oldDestContent, (int)block.DestOffset, block.Length);
                    break;
            }
        }
        return output.ToArray();
    }
}
