using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public sealed class CopySimulationServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-simulate-" + Guid.NewGuid().ToString("N"));

    public CopySimulationServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SimulateAsync_Directory_CountsFilesBytesAndOverwrites()
    {
        string source = Path.Combine(_tempDir, "src");
        string destination = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(source, "sub", "b.bin"), new byte[20]);
        // "a.bin" esiste già in destinazione: è una sovrascrittura.
        await File.WriteAllBytesAsync(Path.Combine(destination, "a.bin"), new byte[99]);

        string[] destinations = { destination };
        var result = await CopySimulationService.SimulateAsync(
            source, destinations, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(30, result.TotalBytes);
        Assert.Equal(0, result.SkippedFiles);
        var dest = Assert.Single(result.Destinations);
        Assert.Equal(1, dest.OverwriteCount);
        Assert.NotNull(dest.FreeBytes);
        Assert.True(dest.Fits);
    }

    [Fact]
    public async Task SimulateAsync_DuplicateDestinationRoots_DoesNotThrow_CountsOverwritesPerPosition()
    {
        // AddExtraDestinationAsync propone come default lo stesso DestinationPath e non deduplica:
        // destinationRoots può contenere la stessa radice due volte. Un Dictionary<string,int>
        // indicizzato sul path lancerebbe ArgumentException("chiave duplicata"); la simulazione
        // deve invece tollerarlo come fa la copia reale, contando gli overwrite per posizione.
        string source = Path.Combine(_tempDir, "src-dup");
        string destination = Path.Combine(_tempDir, "dst-dup");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[10]);
        // "a.bin" esiste già in destinazione: è una sovrascrittura, su entrambe le posizioni.
        await File.WriteAllBytesAsync(Path.Combine(destination, "a.bin"), new byte[5]);

        string[] destinations = { destination, destination };
        var result = await CopySimulationService.SimulateAsync(
            source, destinations, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(2, result.Destinations.Count);
        Assert.All(result.Destinations, d => Assert.Equal(1, d.OverwriteCount));
    }

    [Fact]
    public async Task SimulateAsync_SkipUnchanged_CountsUnchangedAsSkipped()
    {
        string source = Path.Combine(_tempDir, "src2");
        string destination = Path.Combine(_tempDir, "dst2");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);

        string sourceFile = Path.Combine(source, "same.bin");
        string destinationFile = Path.Combine(destination, "same.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[10]);
        File.Copy(sourceFile, destinationFile);
        File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));

        string[] destinations = { destination };
        var result = await CopySimulationService.SimulateAsync(
            source, destinations, skipUnchanged: true, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.SkippedFiles);
    }

    [Fact]
    public async Task SimulateAsync_SingleFile_Works()
    {
        string sourceFile = Path.Combine(_tempDir, "single.bin");
        string destination = Path.Combine(_tempDir, "dst3");
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(sourceFile, new byte[42]);

        string[] destinations = { destination };
        var result = await CopySimulationService.SimulateAsync(
            sourceFile, destinations, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(42, result.TotalBytes);
        Assert.Equal(0, Assert.Single(result.Destinations).OverwriteCount);
    }

    [Fact]
    public async Task SimulateAsync_SingleFile_DestinationIsExistingFilePath_CountsAsOverwrite()
    {
        // Quando la destinazione è il path di un file (non una cartella), la copia reale scrive
        // proprio lì: la simulazione deve rilevarlo come sovrascrittura, non cercare "dentro" root.
        string sourceFile = Path.Combine(_tempDir, "single2.bin");
        string destinationFile = Path.Combine(_tempDir, "existing-dest.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[42]);
        await File.WriteAllBytesAsync(destinationFile, new byte[7]);

        string[] destinations = { destinationFile };
        var result = await CopySimulationService.SimulateAsync(
            sourceFile, destinations, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(1, Assert.Single(result.Destinations).OverwriteCount);
    }

    [Fact]
    public async Task SimulateAsync_SingleFile_SkipUnchanged_NeverSkips()
    {
        // La copia reale di un file singolo (CopySingleFileAsync) ignora SkipUnchanged e ricopia
        // sempre: la simulazione non deve promettere uno skip che poi non avviene.
        string sourceFile = Path.Combine(_tempDir, "same-single.bin");
        string destinationFile = Path.Combine(_tempDir, "same-single-dest.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[10]);
        File.Copy(sourceFile, destinationFile);
        File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));

        string[] destinations = { destinationFile };
        var result = await CopySimulationService.SimulateAsync(
            sourceFile, destinations, skipUnchanged: true, CancellationToken.None);

        Assert.Equal(0, result.SkippedFiles);
    }

    [Fact]
    public async Task SimulateAsync_MissingSource_ThrowsFileNotFoundException()
    {
        string missingSource = Path.Combine(_tempDir, "non-esiste.bin");
        string destination = Path.Combine(_tempDir, "dst4");
        Directory.CreateDirectory(destination);

        string[] destinations = { destination };
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CopySimulationService.SimulateAsync(
                missingSource, destinations, skipUnchanged: false, CancellationToken.None));
    }

    [Fact]
    public async Task SimulateAsync_SkipUnchanged_AllUnchanged_FitsRemainsTrue()
    {
        // Nota: non è possibile mockare DriveInfo per verificare deterministicamente la sottrazione
        // dei byte saltati dal calcolo di Fits; questo test verifica solo che, con tutti i file
        // invariati, il conteggio SkippedFiles combaci col totale e Fits non regredisca a false
        // (lo spazio libero reale sul tempdir è comunque sufficiente per pochi byte).
        string source = Path.Combine(_tempDir, "src5");
        string destination = Path.Combine(_tempDir, "dst5");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);

        string sourceFile = Path.Combine(source, "same.bin");
        string destinationFile = Path.Combine(destination, "same.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[10]);
        File.Copy(sourceFile, destinationFile);
        File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));

        string[] destinations = { destination };
        var result = await CopySimulationService.SimulateAsync(
            source, destinations, skipUnchanged: true, CancellationToken.None);

        Assert.Equal(result.TotalFiles, result.SkippedFiles);
        Assert.True(Assert.Single(result.Destinations).Fits);
    }
}
