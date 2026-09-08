using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopySignatureBuilderTests : IDisposable
{
    private readonly string _root;

    public DeltaCopySignatureBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltasig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public async Task BuildAsync_ExactMultipleOfBlockSize_ProducesOneBlockPerChunk()
    {
        string path = Path.Combine(_root, "dest.bin");
        byte[] content = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(path, blockSizeBytes: 10, CancellationToken.None);

        int totalBlocks = signature.BlocksByWeak.Values.Sum(list => list.Count);
        Assert.Equal(3, totalBlocks);
        Assert.Contains(signature.BlocksByWeak.Values.SelectMany(v => v), b => b.Offset == 0 && b.Length == 10);
        Assert.Contains(signature.BlocksByWeak.Values.SelectMany(v => v), b => b.Offset == 20 && b.Length == 10);
    }

    [Fact]
    public async Task BuildAsync_LastBlockShorter_KeepsActualLength()
    {
        string path = Path.Combine(_root, "dest.bin");
        byte[] content = Enumerable.Range(0, 25).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(path, blockSizeBytes: 10, CancellationToken.None);

        var lastBlock = signature.BlocksByWeak.Values.SelectMany(v => v).Single(b => b.Offset == 20);
        Assert.Equal(5, lastBlock.Length);
    }

    [Fact]
    public async Task BuildAsync_SameContentTwice_ProducesSameStrongHash()
    {
        string pathA = Path.Combine(_root, "a.bin");
        string pathB = Path.Combine(_root, "b.bin");
        byte[] content = Enumerable.Repeat((byte)7, 10).ToArray();
        await File.WriteAllBytesAsync(pathA, content);
        await File.WriteAllBytesAsync(pathB, content);

        var sigA = await DeltaCopySignatureBuilder.BuildAsync(pathA, blockSizeBytes: 10, CancellationToken.None);
        var sigB = await DeltaCopySignatureBuilder.BuildAsync(pathB, blockSizeBytes: 10, CancellationToken.None);

        var blockA = sigA.BlocksByWeak.Values.Single().Single();
        var blockB = sigB.BlocksByWeak.Values.Single().Single();
        Assert.Equal(blockA.StrongHash, blockB.StrongHash);
    }
}
