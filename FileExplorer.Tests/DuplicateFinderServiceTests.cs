using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DuplicateFinderServiceTests : IDisposable
{
    private readonly string _root;

    public DuplicateFinderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task WriteAsync(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public async Task FindDuplicatesAsync_IdenticalFiles_GroupedTogether()
    {
        await WriteAsync("uno.txt", "stesso contenuto");
        await WriteAsync("sub/due.txt", "stesso contenuto");
        await WriteAsync("tre.txt", "contenuto differente!");

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.Contains(Path.Combine(_root, "uno.txt"), group.FilePaths);
        Assert.Contains(Path.Combine(_root, "sub", "due.txt"), group.FilePaths);
    }

    [Fact]
    public async Task FindDuplicatesAsync_SameSizeDifferentContent_NotGrouped()
    {
        await WriteAsync("a.txt", "AAAA");
        await WriteAsync("b.txt", "BBBB"); // stessa lunghezza, contenuto diverso

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_LargeFilesSamePrefixDifferentTail_ResolvedByFullHash()
    {
        // Prefisso identico oltre i 64 KB del pre-filtro, coda diversa:
        // il solo hash parziale li raggrupperebbe, l'hash completo li separa.
        string prefix = new string('x', 70 * 1024);
        await WriteAsync("big1.bin", prefix + "FINE-1");
        await WriteAsync("big2.bin", prefix + "FINE-2");

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsOrderedByWastedSpace()
    {
        await WriteAsync("small1.txt", "ab");
        await WriteAsync("small2.txt", "ab");
        await WriteAsync("large1.txt", new string('z', 5000));
        await WriteAsync("large2.txt", new string('z', 5000));

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Equal(2, groups.Count);
        Assert.Equal(5000, groups[0].FileSize); // il gruppo con più spreco viene prima
    }
}
