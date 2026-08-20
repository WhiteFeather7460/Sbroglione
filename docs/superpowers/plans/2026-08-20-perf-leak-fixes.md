# Perf & Leak Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminare i colli di bottiglia con cartelle da migliaia di file (lavoro per-file sul thread UI, liste non virtualizzate, progressi non throttled) e i leak latenti da eventi statici.

**Architecture:** I servizi fan-out passano a `ConfigureAwait(false)` con prologhi in `Task.Run`; i callback di progresso vengono marshallati esplicitamente sul thread UI tramite un seam testabile (`UiDispatch`) e throttlati (`UiProgressThrottle`); le liste grandi (Duplicati, FilesToProcess) passano a popolamento in blocco e virtualizzazione; le sottoscrizioni a eventi statici diventano disposable.

**Tech Stack:** .NET 8 (main), Avalonia 11, ReactiveUI, xunit.

**Spec:** `docs/superpowers/specs/2026-08-20-perf-leak-audit.md` (finding P1-P9, L1-L4)

## Global Constraints

- Branch: `perf/audit-fixes` da `main`. Mai commit su `main`.
- Ogni task: build pulita + `dotnet test` interamente verde prima del commit (baseline attuale: 379 test).
- Nessun colore hardcodato nelle view: solo `{DynamicResource Brush.*}`.
- Non aggiungere Claude come co-author nei commit.
- I callback dei servizi dopo il Task 1 girano su threadpool: OGNI set di proprietà reactive dentro un callback di servizio deve passare da `UiDispatch.Post` (introdotto nel Task 2). I set fatti nel corpo dei metodi async dei VM (dopo un `await`) restano sul thread UI e non vanno toccati.
- Comportamento osservabile invariato: stessi testi di stato, stessi esiti; cambia solo dove/quanto spesso vengono calcolati.

---

### Task 1: Servizi fan-out fuori dal thread UI (P1 + P2)

**Model:** opus

**Files:**
- Modify: `FileExplorer/Services/DirectoryComparisonService.cs`
- Modify: `FileExplorer/Services/DuplicateFinderService.cs`
- Modify: `FileExplorer/Services/ChecksumService.cs`
- Modify: `FileExplorer/Services/DirectoryVerificationService.cs`
- Modify: `FileExplorer/Services/FileByteCompareService.cs`
- Modify: `FileExplorer/Services/FileCopyService.cs`

**Interfaces:**
- Consumes: niente da altri task (primo task).
- Produces: firme pubbliche INVARIATE. Cambia solo il contratto di threading: i callback (`onProgress`, ecc.) possono arrivare da thread di threadpool. Il Task 2 si appoggia a questo.

- [ ] **Step 1: `ConfigureAwait(false)` sistematico**

In TUTTI gli `await` dei sei servizi elencati (inclusi quelli dentro lambda `files.Select(async ...)`, loop e metodi privati) aggiungere `.ConfigureAwait(false)`. Esempio (`FileCopyService`, lambda per-file):

```csharp
await semaphore.WaitAsync(ct).ConfigureAwait(false);
...
await CopyFileAsync(sourceFile, destinationFile, ..., ct, bufferSize).ConfigureAwait(false);
...
await Task.WhenAll(tasks).ConfigureAwait(false);
```

Verifica con grep che non resti alcun `await` nudo nei sei file: `grep -n "await " FileExplorer/Services/{DirectoryComparisonService,DuplicateFinderService,ChecksumService,DirectoryVerificationService,FileByteCompareService,FileCopyService}.cs | grep -v "ConfigureAwait"`.

- [ ] **Step 2: prologhi di enumerazione in `Task.Run` con passata unica**

In `FileCopyService.CopyDirectoryAsync` (righe ~139-140) e `CopyDirectoryToManyAsync` (righe ~198-199) sostituire:

```csharp
List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration).ToList();
long totalBytes = files.Sum(file => new FileInfo(file).Length);
```

con:

```csharp
(List<string> files, long totalBytes) = await Task.Run(() =>
{
    var list = new List<string>();
    long total = 0;
    foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
    {
        ct.ThrowIfCancellationRequested();
        list.Add(file);
        total += new FileInfo(file).Length;
    }
    return (list, total);
}, ct).ConfigureAwait(false);
```

Applicare lo stesso pattern al prologo di `DirectoryVerificationService.VerifyDirectoryAsync` (riga ~50, enumerazione dei file sorgente).

- [ ] **Step 3: build + suite completa**

Run: `dotnet build FileExplorer.sln && dotnet test`
Expected: 0 errori, 379/379 verdi. I test dei servizi girano su threadpool e non dipendono dal SynchronizationContext: nessun cambiamento atteso. Se un test fallisce, indagare prima di adattarlo (probabile race resa visibile, non da nascondere).

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Services/
git commit -m "perf(services): ConfigureAwait(false) sistematico e prologhi in Task.Run

Il lavoro per-file di confronto/duplicati/verifica/copia girava serializzato
sul thread UI (continuation post-await sul dispatcher Avalonia). Ora resta
su threadpool; l'enumerazione pre-copia non blocca più il chiamante."
```

---

### Task 2: Marshaling + throttle dei progressi nei ViewModel (P4 + data race)

**Model:** opus

**Files:**
- Create: `FileExplorer/Services/UiDispatch.cs`
- Create: `FileExplorer/Services/UiProgressThrottle.cs`
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs` (callback in `CopySingleFileAsync` ~511-518, `CopyDirectoryAsync` ~567-582 e ~616, callback verifica)
- Modify: `FileExplorer/ViewModels/ComparisonViewModel.cs` (callback progresso ~239)
- Modify: `FileExplorer/ViewModels/DuplicatesViewModel.cs` (callback ~122)
- Modify: `FileExplorer/ViewModels/DiskUsageViewModel.cs` (callback `onFilesScanned` ~98-99: data race già oggi)
- Modify: `FileExplorer/ViewModels/WatchFoldersViewModel.cs` (`OnStatusChanged` ~192-202: stesso marshaling)
- Test: `FileExplorer.Tests/UiProgressThrottleTests.cs` (nuovo)

**Interfaces:**
- Consumes: contratto di threading del Task 1 (callback su threadpool).
- Produces:
  - `public static class UiDispatch { public static Action<Action>? Override; public static void Post(Action action); }`
  - `public sealed class UiProgressThrottle { public UiProgressThrottle(TimeSpan? interval = null, Func<double>? clockSeconds = null); public bool ShouldPublish(); }`
  I task successivi usano `UiDispatch.Post` per ogni callback→UI.

- [ ] **Step 1: test del throttle (RED)**

```csharp
using System;
using FileExplorer.Services;
using Xunit;

public class UiProgressThrottleTests
{
    [Fact]
    public void PublishesFirstCallThenThrottlesUntilIntervalElapses()
    {
        double now = 0;
        var throttle = new UiProgressThrottle(TimeSpan.FromMilliseconds(100), () => now);

        Assert.True(throttle.ShouldPublish());
        Assert.False(throttle.ShouldPublish());
        now = 0.05;
        Assert.False(throttle.ShouldPublish());
        now = 0.11;
        Assert.True(throttle.ShouldPublish());
        Assert.False(throttle.ShouldPublish());
    }
}
```

Run: `dotnet test --filter UiProgressThrottleTests` → FAIL (tipo inesistente).

- [ ] **Step 2: implementare `UiProgressThrottle` e `UiDispatch` (GREEN)**

```csharp
// FileExplorer/Services/UiProgressThrottle.cs
using System;
using System.Diagnostics;
using System.Threading;

namespace FileExplorer.Services;

/// <summary>
/// Gate thread-safe per aggiornamenti UI ad alta frequenza: ShouldPublish ritorna true
/// al massimo una volta per intervallo (default 100 ms). Il primo campione passa sempre.
/// Lo stato finale va pubblicato comunque dal chiamante, fuori dal gate.
/// </summary>
public sealed class UiProgressThrottle
{
    private readonly double _intervalSeconds;
    private readonly Func<double> _clockSeconds;
    private long _lastBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);

    public UiProgressThrottle(TimeSpan? interval = null, Func<double>? clockSeconds = null)
    {
        _intervalSeconds = (interval ?? TimeSpan.FromMilliseconds(100)).TotalSeconds;
        _clockSeconds = clockSeconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    public bool ShouldPublish()
    {
        double now = _clockSeconds();
        long lastBits = Interlocked.Read(ref _lastBits);
        if (now - BitConverter.Int64BitsToDouble(lastBits) < _intervalSeconds)
            return false;
        return Interlocked.CompareExchange(ref _lastBits, BitConverter.DoubleToInt64Bits(now), lastBits) == lastBits;
    }
}
```

```csharp
// FileExplorer/Services/UiDispatch.cs
using System;
using Avalonia.Threading;

namespace FileExplorer.Services;

/// <summary>
/// Seam per postare sul thread UI dai callback dei servizi (che girano su threadpool).
/// Nei test, impostare Override = action => action() per esecuzione sincrona
/// (stesso pattern di ConfirmDialogHelper.Override).
/// </summary>
public static class UiDispatch
{
    public static Action<Action>? Override;

    public static void Post(Action action)
    {
        if (Override is not null)
            Override(action);
        else
            Dispatcher.UIThread.Post(action);
    }
}
```

Run: `dotnet test --filter UiProgressThrottleTests` → PASS.

- [ ] **Step 3: adeguare i callback dei VM**

Regola: dentro OGNI callback passato a un servizio, (a) la contabilità (`tracker.Report`, contatori locali con `Interlocked`) resta inline; (b) i set di proprietà passano da `UiDispatch.Post`, gated da un `UiProgressThrottle` per i flussi ad alta frequenza. Lo stato finale resta nel corpo del metodo async (già sul thread UI). Esempio, `CopyPairsViewModel.CopyDirectoryAsync`:

```csharp
var uiThrottle = new UiProgressThrottle();
onProgress: progress =>
{
    bool firstReport = knownFileCount != progress.TotalFiles;
    if (firstReport)
    {
        knownFileCount = progress.TotalFiles;
        tracker.Start(progress.TotalBytes);
    }
    tracker.Report(progress.CopiedBytes);
    bool haveSnapshot = tracker.TryTakeSnapshot(out var snapshot);

    if (!firstReport && !haveSnapshot && !uiThrottle.ShouldPublish())
        return;

    UiDispatch.Post(() =>
    {
        if (firstReport)
            pair.Status = progress.TotalFiles == 0
                ? "Nessun file da copiare"
                : $"Copia cartella… ({progress.TotalFiles} file)";
        pair.Progress = progress.Fraction;
        if (haveSnapshot)
            PublishSpeed(pair, snapshot);
    });
},
```

Stesso trattamento per: il callback di `CopySingleFileAsync` (`pair.Progress` per blocco), il callback verifica (`pair.Status = $"Verifica checksum… (...)"`), `ComparisonViewModel` e `DuplicatesViewModel` (`StatusText = $"{progress.Stage}: ..."` gated dal throttle + Post), `DiskUsageViewModel.onFilesScanned` e `WatchFoldersViewModel.OnStatusChanged` (solo Post, già a bassa frequenza).

Nota `SpeedTracker`: è già lock-based e il suo snapshot è già throttled a 4/s; `TryTakeSnapshot` fa da gate naturale per `PublishSpeed`.

- [ ] **Step 4: fixture test — Override sincrono**

Nei test che esercitano copia/scan dei VM, impostare in setup (constructor della classe test o fixture condivisa): `UiDispatch.Override = action => action();` e ripristinare `null` nel `Dispose`. Cercare le classi test toccate con `grep -l "CopyPairsViewModel\|ComparisonViewModel\|DuplicatesViewModel\|DiskUsageViewModel\|WatchFoldersViewModel" FileExplorer.Tests/*.cs`.

- [ ] **Step 5: build + suite completa**

Run: `dotnet test`
Expected: tutti verdi (379 + 1 nuovo). Fallimenti tipici da sistemare: test che assertano stati intermedi ora throttlati → verificare che assertino solo stati finali (che restano esatti e inline).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Services/UiDispatch.cs FileExplorer/Services/UiProgressThrottle.cs FileExplorer/ViewModels/ FileExplorer.Tests/
git commit -m "perf(vm): marshaling esplicito e throttle degli aggiornamenti di progresso

I callback dei servizi ora arrivano da threadpool: i set di proprietà passano
da UiDispatch.Post e sono gated a ~10/s da UiProgressThrottle. Chiude anche
la data race pre-esistente di DiskUsageViewModel/WatchFoldersViewModel."
```

---

### Task 3: FilesToProcess lazy e in blocco (P3)

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs:30-98`
- Modify: `FileExplorer/Views/CopyPairsView.axaml:191` (Expander)
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs` (o file dedicato se i test della coppia sono altrove: `grep -l "FilesToProcess" FileExplorer.Tests/*.cs`)

**Interfaces:**
- Consumes: niente dai task precedenti.
- Produces: `FolderFilePairViewModel.FilesToProcess` cambia tipo: da `ObservableCollection<FileSystemItem>` a `IReadOnlyList<FileSystemItem>` (property reactive, swap in blocco). Nuove membri: `bool IsFilesExpanded { get; set; }`, `Task FilesLoad { get; }` (per i test, pattern `SourceStateRefresh`).

- [ ] **Step 1: test (RED)**

```csharp
[Fact]
public async Task FilesToProcess_NotLoadedUntilExpanded()
{
    using var dir = new TempDirectory();                  // usare l'helper temp già in uso nella suite
    File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");

    var pair = new FolderFilePairViewModel { SourcePath = dir.Path };
    await pair.SourceStateRefresh;
    Assert.Empty(pair.FilesToProcess);                    // niente listing finché l'Expander è chiuso

    pair.IsFilesExpanded = true;
    await pair.FilesLoad;
    Assert.Single(pair.FilesToProcess);
}

[Fact]
public async Task FilesToProcess_ReloadsOnSourceChangeWhileExpanded()
{
    using var dir1 = new TempDirectory();
    using var dir2 = new TempDirectory();
    File.WriteAllText(Path.Combine(dir2.Path, "b.txt"), "x");

    var pair = new FolderFilePairViewModel { SourcePath = dir1.Path, IsFilesExpanded = true };
    await pair.SourceStateRefresh; await pair.FilesLoad;

    pair.SourcePath = dir2.Path;
    await pair.SourceStateRefresh; await pair.FilesLoad;
    Assert.Single(pair.FilesToProcess);
}
```

(Adattare l'helper directory temporanea a quello reale della suite.) Run → FAIL.

- [ ] **Step 2: implementazione (GREEN)**

In `FolderFilePairViewModel`:

```csharp
private IReadOnlyList<FileSystemItem> _filesToProcess = Array.Empty<FileSystemItem>();
public IReadOnlyList<FileSystemItem> FilesToProcess
{
    get => _filesToProcess;
    private set => this.RaiseAndSetIfChanged(ref _filesToProcess, value);
}

private bool _isFilesExpanded;
public bool IsFilesExpanded
{
    get => _isFilesExpanded;
    set
    {
        this.RaiseAndSetIfChanged(ref _isFilesExpanded, value);
        if (value)
            FilesLoad = LoadFilesToProcessAsync();
    }
}

public Task FilesLoad { get; private set; } = Task.CompletedTask;

private async Task LoadFilesToProcessAsync()
{
    string? path = _sourcePath;
    if (path is null || await FileSystemService.GetPathTypeAsync(path) != PathType.Directory)
    {
        if (path == _sourcePath)
            FilesToProcess = Array.Empty<FileSystemItem>();
        return;
    }

    var listing = await FileSystemService.ListFilesRecursiveAsync(path);
    if (path != _sourcePath)
        return;                                   // sorgente cambiata nel frattempo: esito scartato

    FilesToProcess = listing.Items;               // swap unico: un solo PropertyChanged
}
```

`RefreshSourceStateAsync` perde il ramo listing: dopo aver impostato `SourceExists`, fa `FilesToProcess = Array.Empty<FileSystemItem>();` e, se `IsFilesExpanded`, `FilesLoad = LoadFilesToProcessAsync();`. Nella view: `<Expander Header="Mostra file da elaborare" IsExpanded="{Binding IsFilesExpanded, Mode=TwoWay}">`. Il DataGrid interno non cambia (virtualizza già; `IReadOnlyList` è un ItemsSource valido).

- [ ] **Step 3: suite completa** — Run: `dotnet test`. Adattare i test esistenti che assumevano il caricamento eager (aggiungere `IsFilesExpanded = true` + `await FilesLoad`).

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer/Views/CopyPairsView.axaml FileExplorer.Tests/
git commit -m "perf(copy): listing 'file da elaborare' lazy e con swap in blocco

Il listing ricorsivo partiva a ogni set di SourcePath (anche da journal e
profili all'avvio) e popolava la griglia item-per-item. Ora parte solo alla
prima apertura dell'Expander e pubblica la lista con un unico PropertyChanged."
```

---

### Task 4: Duplicati virtualizzati e popolati in blocco (P9)

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/DuplicatesViewModel.cs:55-57,88,113,125-126`
- Modify: `FileExplorer/Views/DuplicatesView.axaml:45-82`
- Modify: `FileExplorer/Styles/Controls.axaml` (nuova classe `ListBox.cards`)
- Test: il file test esistente dei duplicati (`grep -l "DuplicatesViewModel" FileExplorer.Tests/*.cs`)

**Interfaces:**
- Consumes: niente.
- Produces: `DuplicatesViewModel.Groups` diventa property reactive settabile (`ObservableCollection<DuplicateGroupViewModel>`, swap a fine scan; `Remove` per le eliminazioni continua a funzionare). `HasGroups` invariato nel contratto.

- [ ] **Step 1: test (RED)**

```csharp
[Fact]
public void HasGroups_TracksCollectionSwapAndRemoval()
{
    var vm = new DuplicatesViewModel();
    Assert.False(vm.HasGroups);

    var group = new DuplicateGroupViewModel(new DuplicateGroup(10, new[] { "/a/f1", "/b/f1" }));
    vm.Groups = new ObservableCollection<DuplicateGroupViewModel> { group };
    Assert.True(vm.HasGroups);

    vm.Groups.Remove(group);
    Assert.False(vm.HasGroups);          // il CollectionChanged della NUOVA collection è agganciato
}
```

(Verificare la firma reale di `DuplicateGroup` nel modello e adattare la costruzione.) Run → FAIL (`Groups` non settabile).

- [ ] **Step 2: ViewModel (GREEN)**

```csharp
private ObservableCollection<DuplicateGroupViewModel> _groups = new();
public ObservableCollection<DuplicateGroupViewModel> Groups
{
    get => _groups;
    set
    {
        _groups.CollectionChanged -= OnGroupsChanged;
        this.RaiseAndSetIfChanged(ref _groups, value);
        _groups.CollectionChanged += OnGroupsChanged;
        this.RaisePropertyChanged(nameof(HasGroups));
    }
}

private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
    this.RaisePropertyChanged(nameof(HasGroups));
```

Nel costruttore sostituire la lambda attuale con `Groups.CollectionChanged += OnGroupsChanged;`. In `ScanAsync`: `Groups = new ObservableCollection<DuplicateGroupViewModel>();` al posto di `Groups.Clear()`, e a fine scan:

```csharp
var groups = new ObservableCollection<DuplicateGroupViewModel>(
    found.Select(group => new DuplicateGroupViewModel(group)));
Groups = groups;                          // un solo reset per la UI
```

- [ ] **Step 3: view virtualizzata**

In `Controls.axaml` aggiungere la classe (accanto agli stili `TabControl.nav`):

```xml
<Style Selector="ListBox.cards">
  <Setter Property="Background" Value="Transparent" />
</Style>
<Style Selector="ListBox.cards ListBoxItem">
  <Setter Property="Padding" Value="0" />
  <Setter Property="HorizontalContentAlignment" Value="Stretch" />
  <Setter Property="Focusable" Value="False" />
</Style>
<Style Selector="ListBox.cards ListBoxItem:selected /template/ ContentPresenter,
                 ListBox.cards ListBoxItem:pointerover /template/ ContentPresenter">
  <Setter Property="Background" Value="Transparent" />
</Style>
```

In `DuplicatesView.axaml` sostituire `ScrollViewer` + `ItemsControl` esterni con:

```xml
<ListBox Classes="cards" IsVisible="{Binding HasGroups}"
         ItemsSource="{Binding Groups}" Margin="20,0,20,12"
         SelectionMode="Single">
  <ListBox.ItemTemplate>
    <!-- DataTemplate identico a quello attuale (Border.card con header e ItemsControl dei Files) -->
  </ListBox.ItemTemplate>
</ListBox>
```

L'`ItemsControl` interno dei `Files` resta (i gruppi hanno poche copie l'uno). Il `RelativeSource AncestorType=UserControl` dei due bottoni continua a funzionare dentro il ListBox.

- [ ] **Step 4: suite completa** — Run: `dotnet test`. Expected: verdi (i test che facevano `vm.Groups.Add(...)` continuano a compilare: il tipo non cambia).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/DuplicatesViewModel.cs FileExplorer/Views/DuplicatesView.axaml FileExplorer/Styles/Controls.axaml FileExplorer.Tests/
git commit -m "perf(duplicati): lista gruppi virtualizzata e popolata in blocco

ItemsControl non virtualizzato -> ListBox (VirtualizingStackPanel di default)
con stile .cards senza cromo di selezione; Groups pubblicata con un unico
swap invece di un Add per gruppo."
```

---

### Task 5: Leak da eventi statici + eviction cache dischi (L1, L2, L4)

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs:77-81` (+ dichiarazione classe)
- Modify: `FileExplorer/ViewModels/SettingsViewModel.cs:15-27`
- Modify: `FileExplorer/Services/DiskTypeService.cs:41-55`
- Test: `FileExplorer.Tests/DiskTypeServiceTests.cs` (esistente) + test dispose nei file test dei due VM

**Interfaces:**
- Consumes: niente.
- Produces: `CopyPairsViewModel : IDisposable`, `SettingsViewModel : IDisposable` (unsubscribe da `AppSettingsStore.ThrottleChanged`). `DiskTypeService` espone `internal static void EvictExpiredForTest()` non necessario: l'eviction avviene nel lookup (testata via TTL, vedi step).

- [ ] **Step 1: test (RED)**

```csharp
[Fact]
public void Dispose_UnsubscribesFromThrottleChanged()
{
    var vm = new SettingsViewModel();
    vm.Dispose();

    bool raised = false;
    vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(SettingsViewModel.ThrottleEnabled);
    AppSettingsStore.RaiseThrottleChanged();      // usare il meccanismo reale con cui l'evento viene sollevato
    Assert.False(raised);
}
```

(Verificare come `ThrottleChanged` viene sollevato oggi — probabilmente da un setter di `AppSettingsStore` — e scatenarlo da lì; stesso test per `CopyPairsViewModel`.) Run → FAIL (`Dispose` inesistente).

- [ ] **Step 2: implementazione (GREEN)**

Pattern identico in entrambi i VM (già usato da `WatchFoldersViewModel`):

```csharp
private readonly Action _throttleChangedHandler;

public SettingsViewModel()
{
    _throttleChangedHandler = () =>
    {
        this.RaisePropertyChanged(nameof(ThrottleEnabled));
        this.RaisePropertyChanged(nameof(ThrottleMBps));
    };
    AppSettingsStore.ThrottleChanged += _throttleChangedHandler;
    ...
}

public void Dispose()
{
    AppSettingsStore.ThrottleChanged -= _throttleChangedHandler;
    GC.SuppressFinalize(this);
}
```

`CopyPairsViewModel` ha già altri membri: aggiungere `IDisposable` all'elenco interfacce e, se un `Dispose` esiste già, estenderlo.

- [ ] **Step 3: eviction cache `DiskTypeService`**

Nel lookup (riga ~41):

```csharp
if (Cache.TryGetValue(cacheKey, out var cached))
{
    if (DateTime.UtcNow - cached.CachedAt < CacheTtl)
        return cached.Type;
    Cache.TryRemove(cacheKey, out _);            // entry scaduta: via dal dizionario
}
```

Test: la classe è già coperta da `DiskTypeServiceTests`; se la cache non è ispezionabile, testare il comportamento (una entry ri-rilevata dopo scadenza) oppure esporre `internal static int CacheCountForTest => Cache.Count;` (c'è già `InternalsVisibleTo FileExplorer.Tests`).

- [ ] **Step 4: suite completa** — Run: `dotnet test`.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer/Services/DiskTypeService.cs FileExplorer.Tests/
git commit -m "fix(leak): sottoscrizioni ThrottleChanged disposable ed eviction cache dischi

Le lambda anonime sull'evento statico AppSettingsStore.ThrottleChanged
rootavano per sempre CopyPairsViewModel/SettingsViewModel alla prima
ricreazione delle tab; ora handler in campo + IDisposable (pattern
WatchFoldersViewModel). DiskTypeService rimuove le entry scadute al lookup."
```

---

### Task 6: TreemapControl con cap, skip sub-pixel e debounce (P5)

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/Views/TreemapControl.cs`
- Test: nessun test UI headless nella suite per i controlli custom; la logica di cap va in un metodo statico testabile — Test: `FileExplorer.Tests/TreemapControlTests.cs` (nuovo)

**Interfaces:**
- Consumes: niente.
- Produces: `internal static (List<DiskUsageNode> Visible, int HiddenCount, long HiddenBytes) CapNodes(IReadOnlyList<DiskUsageNode> children, int maxTiles)` su `TreemapControl`.

- [ ] **Step 1: test della logica di cap (RED)**

```csharp
[Fact]
public void CapNodes_KeepsLargestAndAggregatesRest()
{
    var children = Enumerable.Range(1, 500)
        .Select(i => new DiskUsageNode { Name = $"f{i}", SizeBytes = i })   // adattare al costruttore reale
        .ToList();

    var (visible, hiddenCount, hiddenBytes) = TreemapControl.CapNodes(children, maxTiles: 400);

    Assert.Equal(400, visible.Count);
    Assert.Equal(100, hiddenCount);
    Assert.Equal(Enumerable.Range(1, 100).Sum(i => (long)i), hiddenBytes); // i 100 più piccoli
    Assert.Equal(500, visible[0].SizeBytes);                               // ordinati per dimensione desc
}
```

Run → FAIL.

- [ ] **Step 2: implementazione (GREEN)**

In `Rebuild()`:
- Dopo l'ordinamento, applicare `CapNodes(nodes, 400)`; se `HiddenCount > 0` aggiungere in coda alle size un elemento aggregato e, nel loop, renderizzarlo come Border senza handler click con tooltip `$"altri {HiddenCount} elementi — {SizeFormatter.Format(HiddenBytes)}"` e background `Brush.Treemap.6`.
- Skip sub-pixel: `if (rect.Width < 1 || rect.Height < 1) continue;` (sostituisce il check `<= 0`).
- Debounce resize: sostituire `SizeChanged += (_, _) => Rebuild();` con un `DispatcherTimer` one-shot da 100 ms:

```csharp
private readonly DispatcherTimer _resizeDebounce;

public TreemapControl()
{
    _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Rebuild(); };
    SizeChanged += (_, _) => { _resizeDebounce.Stop(); _resizeDebounce.Start(); };
    ActualThemeVariantChanged += (_, _) => Rebuild();
}
```

(`DispatcherTimer` è su un controllo con lifetime pari al controllo stesso: il timer viene fermato dopo il tick, nessun leak.)

- [ ] **Step 3: suite completa** — Run: `dotnet test`.

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Views/TreemapControl.cs FileExplorer.Tests/TreemapControlTests.cs
git commit -m "perf(treemap): cap a 400 tasselli con aggregato, skip sub-pixel, debounce resize"
```

---

### Task 7: Remote browser reattivo + simulazione a passata unica (P6 + P7)

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/RemoteBrowserViewModel.cs:126-159,739-748,800-812`
- Modify: `FileExplorer/Services/CopySimulationService.cs:64-101`
- Modify: `FileExplorer/Services/FileCopyService.cs` (overload `IsUnchanged`)
- Test: file test esistenti (`grep -l "RemoteBrowserViewModel\|CopySimulationService" FileExplorer.Tests/*.cs`)

**Interfaces:**
- Consumes: `UiDispatch` dal Task 2.
- Produces: `RemoteBrowserViewModel` espone `internal static TimeSpan FilterDebounce` (default 200 ms; i test la azzerano) e `Task FilterRefresh { get; }` (pattern `SourceStateRefresh`). `FileCopyService` guadagna `internal static bool IsUnchanged(FileInfo source, string destinationFile)` accanto all'overload esistente.

- [ ] **Step 1: debounce filtri**

Nei cinque setter dei filtri (`FilterPattern`, `FilterMinSizeKb`, `FilterMaxSizeKb`, `FilterModifiedAfter`, `FilterModifiedBefore`) e in `OnlyMissing`, sostituire la chiamata diretta `RebuildVisibleItems()` con `ScheduleRebuild()`:

```csharp
internal static TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(200);
private CancellationTokenSource? _filterCts;

public Task FilterRefresh { get; private set; } = Task.CompletedTask;

private void ScheduleRebuild()
{
    _filterCts?.Cancel();
    _filterCts?.Dispose();
    var cts = _filterCts = new CancellationTokenSource();
    FilterRefresh = RebuildAfterDebounceAsync(cts.Token);
}

private async Task RebuildAfterDebounceAsync(CancellationToken ct)
{
    try { await Task.Delay(FilterDebounce, ct); }
    catch (OperationCanceledException) { return; }
    UiDispatch.Post(RebuildVisibleItems);
}
```

I call-site non-filtro di `RebuildVisibleItems` (es. fine listing) restano diretti. Nei test dei filtri: `RemoteBrowserViewModel.FilterDebounce = TimeSpan.Zero;` in setup + `await vm.FilterRefresh;` prima delle assert (con `UiDispatch.Override = a => a()` dal Task 2).

- [ ] **Step 2: `RefreshLocalStatuses` fuori dal thread UI**

```csharp
private async Task RefreshLocalStatusesAsync()
{
    string? destination = DestinationFolder;
    var entries = Items.ToList();                  // snapshot sul thread UI

    var statuses = await Task.Run(() => entries.Select(entry =>
        entry.IsDirectory || string.IsNullOrWhiteSpace(destination)
            ? null
            : DownloadService.GetLocalStatus(entry.Item, Path.Combine(destination, entry.Name)))
        .ToList());

    for (int i = 0; i < entries.Count; i++)        // continuation: di nuovo sul thread UI
        entries[i].LocalStatus = statuses[i];
}
```

Aggiornare i call-site (`LoadListingCoreAsync` e gli altri trovati con `grep -n "RefreshLocalStatuses" FileExplorer/ViewModels/RemoteBrowserViewModel.cs`) in `await RefreshLocalStatusesAsync();`.

- [ ] **Step 3: simulazione a passata unica**

In `CopySimulationService.Simulate` (ramo directory) fondere le tre passate (totale, skipped, overwrites) in una sola che costruisce `FileInfo` sorgente una volta per file e un `FileInfo` destinazione una volta per (file, destinazione):

```csharp
long totalBytes = 0;
int skipped = 0;
long skippedBytes = 0;
var overwritesByRoot = destinationRoots.ToDictionary(root => root, _ => 0);

foreach (var (source, relative) in files)
{
    ct.ThrowIfCancellationRequested();
    var sourceInfo = new FileInfo(source);
    totalBytes += sourceInfo.Length;

    bool unchangedEverywhere = skipUnchanged;
    foreach (var root in destinationRoots)
    {
        var destInfo = new FileInfo(Path.Combine(root, relative));
        if (destInfo.Exists)
            overwritesByRoot[root]++;
        if (skipUnchanged)
            unchangedEverywhere &= FileCopyService.IsUnchanged(sourceInfo, destInfo);
    }

    if (skipUnchanged && unchangedEverywhere)
    {
        skipped++;
        skippedBytes += sourceInfo.Length;
    }
}
```

Aggiungere in `FileCopyService` l'overload `internal static bool IsUnchanged(FileInfo source, FileInfo destination)` con la stessa regola dell'esistente (dimensione uguale + LastWriteTimeUtc entro 2 s), e far delegare l'overload string-based a questo. I risultati (`CopySimulationResult`) restano identici: i test esistenti della simulazione devono passare invariati.

- [ ] **Step 4: suite completa** — Run: `dotnet test`.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/RemoteBrowserViewModel.cs FileExplorer/Services/CopySimulationService.cs FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/
git commit -m "perf(remote,dry-run): debounce filtri, stat locali su threadpool, simulazione a passata unica"
```

---

### Task 8: Avvio watch-folder fuori dal thread UI (P8)

**Model:** haiku

**Files:**
- Modify: `FileExplorer/App.axaml.cs:37-51`
- Modify: `FileExplorer/ViewModels/WatchFoldersViewModel.cs` (call-site di `WatchFolderService.Start`/`Stop` in `OnRuleChanged`, righe ~141-155)

**Interfaces:**
- Consumes: niente (il servizio è già thread-safe con lock interni).
- Produces: niente di nuovo.

- [ ] **Step 1: App.axaml.cs**

Sostituire il loop di avvio con una versione fire-and-forget su threadpool (una regola con sorgente di rete irraggiungibile non deve più ritardare l'apertura della finestra):

```csharp
List<WatchRule> rules = WatchRuleStore.Load();
_ = Task.Run(() =>
{
    foreach (WatchRule rule in rules)
    {
        if (!rule.Enabled)
            continue;
        try
        {
            WatchFolderService.Start(rule);
        }
        catch (Exception)
        {
            // Difesa in profondità: Start non lancia più, ma una singola regola
            // malata non deve fermare le altre.
        }
    }
});
```

(`using System.Threading.Tasks;` e `using System.Collections.Generic;` se mancanti.)

- [ ] **Step 2: WatchFoldersViewModel.OnRuleChanged**

Leggere il metodo e avvolgere le sole chiamate `WatchFolderService.Start(...)` / `Stop(...)` in `_ = Task.Run(() => { try { ... } catch { } });` mantenendo invariata la logica che decide cosa avviare/fermare (che resta sul thread UI). Se il metodo aggiorna lo stato della regola dopo lo Start, quello stato arriva già via `StatusChanged` (marshalled dal Task 2): non serve attendere.

- [ ] **Step 3: suite completa** — Run: `dotnet test`. I test dei watch-folder usano `SyncOverride`/seam esistenti; se qualcuno assume Start sincrono, attendere con le primitive già esposte dal servizio (o `Task.Delay` breve NO: usare gli hook esistenti — indagare prima di modificare).

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/App.axaml.cs FileExplorer/ViewModels/WatchFoldersViewModel.cs
git commit -m "perf(watch): Start dei runner su threadpool

Directory.Exists su una sorgente di rete irraggiungibile bloccava il thread
UI (all'avvio: prima che la MainWindow apparisse) fino al timeout SMB."
```

---

## Self-Review (fatto in stesura)

- Spec coverage: P1→T1, P2→T1, P3→T3, P4→T2, P5→T6, P6→T7, P7→T7, P8→T8, P9→T4, L1/L2→T5, L4→T5. L3 esplicitamente senza azione (documentato in spec).
- Tipi coerenti: `UiDispatch.Post`/`UiProgressThrottle.ShouldPublish` definiti in T2 e consumati in T7; `FilesToProcess` cambia tipo solo in T3 e la view è aggiornata nello stesso task; overload `IsUnchanged(FileInfo, FileInfo)` definito e consumato in T7.
- Ordine: T1 prima di T2 (contratto threading); T2 prima di T7/T8 (UiDispatch). T3-T6 indipendenti.
