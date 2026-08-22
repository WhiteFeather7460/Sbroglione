using Sbroglione.Models;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class FolderFilePairViewModelTests : IDisposable
{
    private readonly string _root;

    public FolderFilePairViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public async Task ExistingSource_EnablesCanStart()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "hello");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = file,
            DestinationPath = Path.Combine(_root, "dest")
        };
        await pair.SourceStateRefresh;

        Assert.True(pair.SourceExists);
        Assert.True(pair.CanStart);
    }

    [Fact]
    public async Task MissingSource_DisablesCanStart()
    {
        var pair = new FolderFilePairViewModel
        {
            SourcePath = Path.Combine(_root, "nope.txt"),
            DestinationPath = Path.Combine(_root, "dest")
        };
        await pair.SourceStateRefresh;

        Assert.False(pair.SourceExists);
        Assert.False(pair.CanStart);
    }

    [Fact]
    public async Task DirectorySource_LoadsFilesToProcess()
    {
        string dir = Path.Combine(_root, "src");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "b");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        Assert.Equal(2, pair.FilesToProcess.Count);
    }

    [Fact]
    public async Task FilesToProcess_NotLoadedUntilExpanded()
    {
        string dir = Path.Combine(_root, "src1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir };
        await pair.SourceStateRefresh;
        Assert.Empty(pair.FilesToProcess);             // niente listing finché l'Expander è chiuso

        pair.IsFilesExpanded = true;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task FilesToProcess_ReloadsOnSourceChangeWhileExpanded()
    {
        string dir1 = Path.Combine(_root, "src2");
        string dir2 = Path.Combine(_root, "src3");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "b.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir1, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        pair.SourcePath = dir2;
        await pair.SourceStateRefresh;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task SettingSourcePathAndIsFilesExpandedTogether_StartsOnlyOneLoad()
    {
        // Object initializer: SourcePath (che scatena RefreshSourceStateAsync) e
        // IsFilesExpanded=true (che scatena il proprio load) partono nella stessa
        // sequenza sincrona. Devono coordinarsi su un solo listing per lo stesso path,
        // non su due task concorrenti che fanno lo stesso I/O ricorsivo.
        string dir = Path.Combine(_root, "src4");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        Assert.Equal(1, pair.FilesLoadStartCountForTests);
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task RedundantIsFilesExpandedSet_DoesNotStartAnotherLoad()
    {
        // Il binding TwoWay su Expander.IsExpanded può ri-alzare true→true (es. sul
        // redraw). Non deve ripartire un secondo listing per lo stesso SourcePath.
        string dir = Path.Combine(_root, "src5");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        pair.IsFilesExpanded = true; // redundant re-raise
        await pair.FilesLoad;

        Assert.Equal(1, pair.FilesLoadStartCountForTests);
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task Reopening_AfterExternalChange_RefreshesFilesToProcess()
    {
        // Expand -> collapse -> il contenuto della dir cambia su disco (watch rule, modifica
        // esterna) mentre l'Expander è chiuso -> re-expand: la griglia deve riflettere il nuovo
        // contenuto, non quello cacheato dalla prima apertura (il gate a generazioni deduplica
        // solo i trigger concorrenti dello stesso ciclo di apertura, non tra un'apertura e la
        // successiva).
        string dir = Path.Combine(_root, "src6");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);

        pair.IsFilesExpanded = false;
        File.WriteAllText(Path.Combine(dir, "b.txt"), "y"); // modifica esterna a Expander chiuso

        pair.IsFilesExpanded = true;
        await pair.FilesLoad;

        Assert.Equal(2, pair.FilesToProcess.Count);
        Assert.Equal(2, pair.FilesLoadStartCountForTests);
    }

    [Fact]
    public async Task SourcePathChange_ClearsPreviousListingImmediately()
    {
        // Il clear della lista appartenente alla sorgente precedente è sincrono con il set
        // di SourcePath, non dopo l'await della verifica: fatto dopo, poteva cancellare il
        // listing appena pubblicato dal load della stessa generazione (griglia vuota a
        // Expander aperto, senza nessuno che ricaricasse).
        string dir1 = Path.Combine(_root, "src7");
        string dir2 = Path.Combine(_root, "src8");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir1, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir2, "b.txt"), "y");
        File.WriteAllText(Path.Combine(dir2, "c.txt"), "z");

        var pair = new FolderFilePairViewModel { SourcePath = dir1, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);

        pair.SourcePath = dir2;
        Assert.Empty(pair.FilesToProcess);   // subito, senza attendere I/O

        await pair.SourceStateRefresh;
        await pair.FilesLoad;
        Assert.Equal(2, pair.FilesToProcess.Count);
    }

    [Fact]
    public async Task ListingCompletedBeforeSourceCheck_IsNotCleared()
    {
        // L'ordine di completamento tra la verifica della sorgente e il listing non è
        // garantito (path locale vs share lenta): quando il listing arriva per primo, la
        // continuation della verifica non deve azzerare la lista già pubblicata.
        string dir = Path.Combine(_root, "src9");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.FilesLoad;
        await pair.SourceStateRefresh;

        Assert.Single(pair.FilesToProcess);
        Assert.True(pair.SourceExists);
    }

    [Fact]
    public async Task SameSourceAndDestination_DisablesCanStart()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "hello");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = file,
            DestinationPath = file
        };
        await pair.SourceStateRefresh;

        Assert.False(pair.CanStart);
    }

    [Fact]
    public void DestinationsProgress_StartsEmpty_AndAcceptsAddedEntries()
    {
        var pair = new FolderFilePairViewModel();
        Assert.Empty(pair.DestinationsProgress);

        var destination = new DestinationProgressViewModel(@"C:\dest");
        pair.DestinationsProgress.Add(destination);

        Assert.Equal(@"C:\dest", destination.Path);
        Assert.Equal(CopyStateKind.Copying, destination.StateKind);
        Assert.Equal(0, destination.Progress);
        Assert.Empty(destination.CopyingFiles);
        Assert.Single(pair.DestinationsProgress);
    }

    [Fact]
    public void DestinationProgressViewModel_PropertySetters_RaisePropertyChanged()
    {
        var destination = new DestinationProgressViewModel("/dest");
        var raised = new List<string>();
        destination.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        destination.Progress = 0.5;
        destination.Status = "Copia in corso";
        destination.SpeedText = "10 MB/s";
        destination.StateKind = CopyStateKind.Error;
        destination.ErrorMessage = "disco pieno";

        Assert.Contains(nameof(DestinationProgressViewModel.Progress), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.Status), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.SpeedText), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.StateKind), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.ErrorMessage), raised);
    }

    [Fact]
    public void ShowCopyingWidget_TrueWhileCopying_FalseAfterFullSuccess()
    {
        var pair = new FolderFilePairViewModel();
        Assert.False(pair.ShowCopyingWidget);

        pair.IsCopying = true;
        Assert.True(pair.ShowCopyingWidget);

        var destination = new DestinationProgressViewModel("/dest");
        pair.DestinationsProgress.Add(destination);
        destination.StateKind = CopyStateKind.Success;

        pair.IsCopying = false;

        // Copia interamente riuscita: il widget torna a nascondersi come prima di questo fix.
        Assert.False(pair.ShowCopyingWidget);
    }

    [Fact]
    public void ShowCopyingWidget_StaysTrueAfterCopyEnds_WhenADestinationFailed()
    {
        var pair = new FolderFilePairViewModel();
        pair.IsCopying = true;

        var good = new DestinationProgressViewModel("/good");
        var bad = new DestinationProgressViewModel("/bad");
        pair.DestinationsProgress.Add(good);
        pair.DestinationsProgress.Add(bad);
        good.StateKind = CopyStateKind.Success;
        bad.StateKind = CopyStateKind.Error;

        pair.IsCopying = false;

        // Una destinazione in errore: il widget resta visibile anche a copia finita, così
        // l'utente può vedere quale destinazione ha fallito e perché (Issue 1).
        Assert.True(pair.ShowCopyingWidget);
    }

    [Fact]
    public void ShowCopyingWidget_RaisesPropertyChanged_WhenDestinationStateKindChanges()
    {
        var pair = new FolderFilePairViewModel();
        pair.IsCopying = true;

        var destination = new DestinationProgressViewModel("/dest");
        pair.DestinationsProgress.Add(destination);

        pair.IsCopying = false;
        Assert.False(pair.ShowCopyingWidget);

        var raised = new List<string>();
        pair.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        destination.StateKind = CopyStateKind.Error;

        Assert.Contains(nameof(FolderFilePairViewModel.ShowCopyingWidget), raised);
        Assert.True(pair.ShowCopyingWidget);
    }

    [Fact]
    public void ShowCopyingWidget_ResetsOnNewCopy_WhenDestinationsProgressIsCleared()
    {
        var pair = new FolderFilePairViewModel();
        pair.IsCopying = true;

        var bad = new DestinationProgressViewModel("/bad") { StateKind = CopyStateKind.Error };
        pair.DestinationsProgress.Add(bad);

        pair.IsCopying = false;
        Assert.True(pair.ShowCopyingWidget);

        // Simula quanto fa StartCopyAsync per una nuova copia: IsCopying=true, poi
        // Clear()+ripopolamento di DestinationsProgress.
        pair.IsCopying = true;
        pair.DestinationsProgress.Clear();
        pair.DestinationsProgress.Add(new DestinationProgressViewModel("/new"));

        Assert.True(pair.ShowCopyingWidget); // ancora in copia
        pair.IsCopying = false;
        Assert.False(pair.ShowCopyingWidget); // nessun errore sulla nuova destinazione: nascosto
    }

    [Fact]
    public void ExtensionFilterMode_DefaultsToNone()
    {
        var pair = new FolderFilePairViewModel();
        Assert.Equal(ExtensionFilterMode.None, pair.ExtensionFilterMode);
        Assert.False(pair.IsExtensionFilterActive);
    }

    [Fact]
    public void IsExtensionFilterActive_TrueWhenModeIsNotNone()
    {
        var pair = new FolderFilePairViewModel { ExtensionFilterMode = ExtensionFilterMode.Whitelist };
        Assert.True(pair.IsExtensionFilterActive);
    }

    [Fact]
    public void BuildExtensionFilter_ModeNone_ReturnsNull()
    {
        var pair = new FolderFilePairViewModel { ExtensionFilterText = "jpg,png" };
        Assert.Null(pair.BuildExtensionFilter());
    }

    [Fact]
    public void BuildExtensionFilter_WhitelistWithText_ReturnsMatchingFilter()
    {
        var pair = new FolderFilePairViewModel
        {
            ExtensionFilterMode = ExtensionFilterMode.Whitelist,
            ExtensionFilterText = "jpg,png"
        };

        var filter = pair.BuildExtensionFilter();

        Assert.NotNull(filter);
        Assert.True(filter!.Matches(@"C:\a.jpg"));
        Assert.False(filter.Matches(@"C:\a.txt"));
    }
}
