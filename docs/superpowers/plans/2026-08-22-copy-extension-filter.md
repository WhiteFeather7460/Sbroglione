# Copy Extension Filter (Whitelist/Blacklist) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a copy pair filter which files get copied during a directory copy by extension, using either a whitelist (copy only listed extensions) or a blacklist (copy everything except listed extensions), with multiple comma-separated extensions.

**Architecture:** New `ExtensionFilterMode` enum + `ExtensionFilter` model in `Sbroglione/Models/` encapsulate parsing (comma-separated extension text → normalized set) and matching (`bool Matches(string filePath)`). The filter is threaded as an optional parameter into the two directory-enumeration points in `FileCopyService` (`CopyDirectoryAsync`, `CopyDirectoryToManyAsync`) and mirrored into `CopySimulationService` so "Simulate" stays accurate. It's a per-pair setting on `FolderFilePairViewModel` (mode + free-text extensions), following the same per-pair option pattern already used for `SkipUnchanged`/`ClearDestinationBeforeCopy`. `CopyPairsViewModel` builds the filter from the pair and passes it to both the real copy and the simulation. UI is a `ComboBox` (None/Whitelist/Blacklist) + conditional `TextBox`, next to the existing "Clear destination" checkbox in `CopyPairsView.axaml`, following the exact enum-`ComboBox` convention already used in `ProfileEditorWindow.axaml`.

**Tech Stack:** .NET 8, Avalonia UI (MVVM/ReactiveUI), xunit.

**Spec:** No separate spec file — this is a bounded change to an existing flow (per `superpowers:brainstorming`'s bounded path). Design was agreed in chat: per-pair scope, free-text comma-separated extensions (not a scanned multi-select). This plan document is authoritative.

## Global Constraints

- Never hardcode colors in views — always `{DynamicResource Brush.*}` (per `Sbroglione/Styles/Palette.axaml` convention).
- New localized strings must be added to BOTH `Services/Localization/StringsEn.cs` and `Services/Localization/StringsIt.cs`.
- Filtering only applies to the directory-copy path (`CopyDirectoryAsync`/`CopyDirectoryToManyAsync`); single-file copies (`CopySingleFileAsync`) are unaffected — same rule already applies to `skipUnchanged`.
- `CopySimulationService`'s directory branch must apply the same filter as the real copy so "Simulate" stays truthful; its single-file branch (`SimulateSingleFile`) is unaffected, mirroring the real-copy behavior.
- All new params on existing public methods must be optional with safe defaults (`null`) appended at the end of the parameter list, since some tests call these methods positionally for the first several args.

---

### Task 1: `ExtensionFilterMode` enum + `ExtensionFilter` model

**Model:** sonnet — self-contained parsing/matching logic, worth a careful implementer.

**Files:**
- Create: `Sbroglione/Models/ExtensionFilterMode.cs`
- Create: `Sbroglione/Models/ExtensionFilter.cs`
- Test: `Sbroglione.Tests/ExtensionFilterTests.cs`

**Interfaces:**
- Produces: `Sbroglione.Models.ExtensionFilterMode` enum with values `None`, `Whitelist`, `Blacklist`. `Sbroglione.Models.ExtensionFilter` with `static ExtensionFilter? Parse(ExtensionFilterMode mode, string? extensionsText)` and instance method `bool Matches(string filePath)`. `Parse` returns `null` when `mode == None` or when `extensionsText` yields no valid extensions after trimming/splitting — callers treat `null` as "no filtering".

- [ ] **Step 1: Write the failing tests**

```csharp
// Sbroglione.Tests/ExtensionFilterTests.cs
using Sbroglione.Models;

namespace Sbroglione.Tests;

public sealed class ExtensionFilterTests
{
    [Fact]
    public void Parse_ModeNone_ReturnsNull()
    {
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.None, "jpg,png"));
    }

    [Fact]
    public void Parse_WhitelistEmptyText_ReturnsNull()
    {
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, ""));
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, null));
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "  , , "));
    }

    [Fact]
    public void Matches_Whitelist_OnlyListedExtensionsMatch()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "jpg, png,MP4");
        Assert.NotNull(filter);
        Assert.True(filter!.Matches(@"C:\photos\a.jpg"));
        Assert.True(filter.Matches(@"C:\photos\B.PNG"));
        Assert.True(filter.Matches(@"C:\videos\c.mp4"));
        Assert.False(filter.Matches(@"C:\docs\d.txt"));
        Assert.False(filter.Matches(@"C:\misc\noext"));
    }

    [Fact]
    public void Matches_Blacklist_ListedExtensionsExcluded()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Blacklist, "tmp,.log");
        Assert.NotNull(filter);
        Assert.False(filter!.Matches(@"C:\x\a.tmp"));
        Assert.False(filter.Matches(@"C:\x\b.LOG"));
        Assert.True(filter.Matches(@"C:\x\c.jpg"));
        Assert.True(filter.Matches(@"C:\x\noext"));
    }

    [Fact]
    public void Parse_LeadingDotAndWhitespaceInExtensions_IsNormalized()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, " .jpg , PNG ");
        Assert.NotNull(filter);
        Assert.True(filter!.Matches(@"C:\a.jpg"));
        Assert.True(filter.Matches(@"C:\b.png"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Sbroglione.Tests --filter ExtensionFilterTests`
Expected: FAIL — `ExtensionFilterMode`/`ExtensionFilter` do not exist (compile error).

- [ ] **Step 3: Implement `ExtensionFilterMode`**

```csharp
// Sbroglione/Models/ExtensionFilterMode.cs
namespace Sbroglione.Models;

/// <summary>Modalità del filtro per estensione applicato durante la copia di una cartella.</summary>
public enum ExtensionFilterMode
{
    /// <summary>Nessun filtro: copia tutti i file.</summary>
    None,

    /// <summary>Copia solo i file con estensione nell'elenco.</summary>
    Whitelist,

    /// <summary>Copia tutti i file tranne quelli con estensione nell'elenco.</summary>
    Blacklist
}
```

- [ ] **Step 4: Implement `ExtensionFilter`**

```csharp
// Sbroglione/Models/ExtensionFilter.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sbroglione.Models;

/// <summary>
/// Filtro per estensione applicato all'enumerazione dei file durante una copia di
/// cartella: whitelist (copia solo le estensioni elencate) o blacklist (copia tutto
/// tranne le estensioni elencate). Le estensioni sono confrontate senza punto e senza
/// distinzione maiuscole/minuscole.
/// </summary>
public sealed class ExtensionFilter
{
    private readonly ExtensionFilterMode _mode;
    private readonly HashSet<string> _extensions;

    private ExtensionFilter(ExtensionFilterMode mode, HashSet<string> extensions)
    {
        _mode = mode;
        _extensions = extensions;
    }

    /// <summary>
    /// Analizza <paramref name="extensionsText"/> (estensioni separate da virgola, es.
    /// "jpg,png,mp4") secondo <paramref name="mode"/>. Ritorna null se il filtro non va
    /// applicato: <paramref name="mode"/> è <see cref="ExtensionFilterMode.None"/> oppure
    /// non c'è nessuna estensione valida dopo il parsing (nessuna restrizione = copia tutto).
    /// </summary>
    public static ExtensionFilter? Parse(ExtensionFilterMode mode, string? extensionsText)
    {
        if (mode == ExtensionFilterMode.None)
            return null;

        var extensions = (extensionsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(e => e.Length > 0)
            .ToHashSet();

        return extensions.Count == 0 ? null : new ExtensionFilter(mode, extensions);
    }

    /// <summary>True se il file va copiato secondo questo filtro.</summary>
    public bool Matches(string filePath)
    {
        string extension = Normalize(Path.GetExtension(filePath));
        bool inList = _extensions.Contains(extension);
        return _mode == ExtensionFilterMode.Whitelist ? inList : !inList;
    }

    private static string Normalize(string extension) =>
        extension.TrimStart('.').Trim().ToLowerInvariant();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Sbroglione.Tests --filter ExtensionFilterTests`
Expected: PASS (6 tests)

- [ ] **Step 6: Commit**

```bash
git add Sbroglione/Models/ExtensionFilterMode.cs Sbroglione/Models/ExtensionFilter.cs Sbroglione.Tests/ExtensionFilterTests.cs
git commit -m "feat: add ExtensionFilter model for whitelist/blacklist copy filtering"
```

---

### Task 2: Apply filter in `FileCopyService` directory enumeration

**Model:** sonnet — touches concurrent copy engine, needs care not to break existing parallelism/progress logic.

**Files:**
- Modify: `Sbroglione/Services/FileCopyService.cs:214-282` (`CopyDirectoryAsync`)
- Modify: `Sbroglione/Services/FileCopyService.cs:298-437` (`CopyDirectoryToManyAsync`)
- Test: `Sbroglione.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Consumes: `Sbroglione.Models.ExtensionFilter.Matches(string filePath): bool` (Task 1).
- Produces: `CopyDirectoryAsync(..., ExtensionFilter? extensionFilter = null)` and `CopyDirectoryToManyAsync(..., ExtensionFilter? extensionFilter = null)` — new optional trailing parameter on both public methods. `null` (the default) means "no filtering", identical to current behavior.

- [ ] **Step 1: Write the failing tests**

Add to `Sbroglione.Tests/FileCopyServiceTests.cs` (inside the existing `FileCopyServiceTests` class, using its `_root` field and constructor/dispose already in the file):

```csharp
[Fact]
public async Task CopyDirectoryAsync_WhitelistFilter_OnlyCopiesMatchingExtensions()
{
    string sourceRoot = Path.Combine(_root, "wl-src");
    string destinationRoot = Path.Combine(_root, "wl-dst");
    Directory.CreateDirectory(sourceRoot);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.txt"), "text");

    var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "jpg");

    await FileCopyService.CopyDirectoryAsync(
        sourceRoot, destinationRoot, 1, null, CancellationToken.None,
        extensionFilter: filter);

    Assert.True(File.Exists(Path.Combine(destinationRoot, "a.jpg")));
    Assert.False(File.Exists(Path.Combine(destinationRoot, "b.txt")));
}

[Fact]
public async Task CopyDirectoryAsync_BlacklistFilter_ExcludesMatchingExtensions()
{
    string sourceRoot = Path.Combine(_root, "bl-src");
    string destinationRoot = Path.Combine(_root, "bl-dst");
    Directory.CreateDirectory(sourceRoot);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.tmp"), "temp");

    var filter = ExtensionFilter.Parse(ExtensionFilterMode.Blacklist, "tmp");

    await FileCopyService.CopyDirectoryAsync(
        sourceRoot, destinationRoot, 1, null, CancellationToken.None,
        extensionFilter: filter);

    Assert.True(File.Exists(Path.Combine(destinationRoot, "a.jpg")));
    Assert.False(File.Exists(Path.Combine(destinationRoot, "b.tmp")));
}

[Fact]
public async Task CopyDirectoryToManyAsync_WhitelistFilter_OnlyCopiesMatchingExtensionsToAllDestinations()
{
    string sourceRoot = Path.Combine(_root, "wlm-src");
    string dest1 = Path.Combine(_root, "wlm-dst1");
    string dest2 = Path.Combine(_root, "wlm-dst2");
    Directory.CreateDirectory(sourceRoot);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.txt"), "text");

    var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "jpg");

    await FileCopyService.CopyDirectoryToManyAsync(
        sourceRoot, new[] { dest1, dest2 }, 1, null, CancellationToken.None,
        extensionFilter: filter);

    Assert.True(File.Exists(Path.Combine(dest1, "a.jpg")));
    Assert.True(File.Exists(Path.Combine(dest2, "a.jpg")));
    Assert.False(File.Exists(Path.Combine(dest1, "b.txt")));
    Assert.False(File.Exists(Path.Combine(dest2, "b.txt")));
}

[Fact]
public async Task CopyDirectoryAsync_NullFilter_CopiesEverything()
{
    string sourceRoot = Path.Combine(_root, "nf-src");
    string destinationRoot = Path.Combine(_root, "nf-dst");
    Directory.CreateDirectory(sourceRoot);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.txt"), "text");

    await FileCopyService.CopyDirectoryAsync(
        sourceRoot, destinationRoot, 1, null, CancellationToken.None);

    Assert.True(File.Exists(Path.Combine(destinationRoot, "a.jpg")));
    Assert.True(File.Exists(Path.Combine(destinationRoot, "b.txt")));
}
```

Add `using Sbroglione.Models;` to the top of `FileCopyServiceTests.cs` if not already present (it already has `using Sbroglione.Models;` at line 2 — confirm before adding a duplicate).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Sbroglione.Tests --filter FileCopyServiceTests`
Expected: FAIL — compile error, `extensionFilter` parameter does not exist on `CopyDirectoryAsync`/`CopyDirectoryToManyAsync`.

- [ ] **Step 3: Implement the filter parameter in `CopyDirectoryAsync`**

In `Sbroglione/Services/FileCopyService.cs`, change the signature at line 214 and the enumeration loop at line 228-239:

```csharp
    public static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false,
        Action<string>? onFileStarted = null,
        Action<string>? onFileCompleted = null,
        ExtensionFilter? extensionFilter = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        (List<string> files, long totalBytes) = await Task.Run(() =>
        {
            var list = new List<string>();
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
            {
                ct.ThrowIfCancellationRequested();
                if (extensionFilter is not null && !extensionFilter.Matches(file))
                    continue;
                list.Add(file);
                total += new FileInfo(file).Length;
            }
            return (list, total);
        }, ct).ConfigureAwait(false);
```

Add `using Sbroglione.Models;` to the top of `FileCopyService.cs` (it currently has no `Models` using — verify before adding).

- [ ] **Step 4: Implement the filter parameter in `CopyDirectoryToManyAsync`**

Same change at line 298 (signature) and line 313-324 (enumeration):

```csharp
    public static async Task<CopyDirectoryToManyResult> CopyDirectoryToManyAsync(
        string sourceRoot,
        IReadOnlyList<string> destinationRoots,
        int maxDegreeOfParallelism,
        Action<string, CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false,
        Action<string, string>? onFileStarted = null,
        Action<string, string>? onFileCompleted = null,
        Action<string, string, Exception>? onFileFailed = null,
        ExtensionFilter? extensionFilter = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        (List<string> files, long totalBytes) = await Task.Run(() =>
        {
            var list = new List<string>();
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
            {
                ct.ThrowIfCancellationRequested();
                if (extensionFilter is not null && !extensionFilter.Matches(file))
                    continue;
                list.Add(file);
                total += new FileInfo(file).Length;
            }
            return (list, total);
        }, ct).ConfigureAwait(false);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Sbroglione.Tests --filter FileCopyServiceTests`
Expected: PASS (all existing FileCopyServiceTests plus the 4 new ones)

- [ ] **Step 6: Commit**

```bash
git add Sbroglione/Services/FileCopyService.cs Sbroglione.Tests/FileCopyServiceTests.cs
git commit -m "feat: apply extension filter in FileCopyService directory enumeration"
```

---

### Task 3: Apply filter in `CopySimulationService`

**Model:** sonnet — must stay behaviorally mirrored with Task 2's real-copy filtering.

**Files:**
- Modify: `Sbroglione/Services/CopySimulationService.cs`
- Test: `Sbroglione.Tests/CopySimulationServiceTests.cs`

**Interfaces:**
- Consumes: `Sbroglione.Models.ExtensionFilter.Matches(string filePath): bool` (Task 1).
- Produces: `CopySimulationService.SimulateAsync(string sourcePath, IReadOnlyList<string> destinationRoots, bool skipUnchanged, CancellationToken ct, ExtensionFilter? extensionFilter = null): Task<CopySimulationResult>` — new optional trailing parameter, `null` default preserves current behavior. `TotalFiles`/`TotalBytes` in the returned `CopySimulationResult` reflect only the files that pass the filter.

First, read `Sbroglione.Tests/CopySimulationServiceTests.cs` to confirm its existing helper/fixture pattern (temp dir setup) before writing new tests — follow whatever pattern it already uses (likely similar to `FileCopyServiceTests`'s `_root`/`IDisposable` pattern).

- [ ] **Step 1: Write the failing tests**

Add to `Sbroglione.Tests/CopySimulationServiceTests.cs`, using that file's existing temp-directory fixture (mirror its existing test setup exactly — do not introduce a second unrelated pattern):

```csharp
[Fact]
public async Task SimulateAsync_WhitelistFilter_OnlyCountsMatchingExtensions()
{
    string sourcePath = Path.Combine(_root, "sim-wl-src");
    string destination = Path.Combine(_root, "sim-wl-dst");
    Directory.CreateDirectory(sourcePath);
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "b.txt"), "text");

    var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "jpg");

    var result = await CopySimulationService.SimulateAsync(
        sourcePath, new[] { destination }, skipUnchanged: false, CancellationToken.None,
        extensionFilter: filter);

    Assert.Equal(1, result.TotalFiles);
}

[Fact]
public async Task SimulateAsync_BlacklistFilter_ExcludesMatchingExtensions()
{
    string sourcePath = Path.Combine(_root, "sim-bl-src");
    string destination = Path.Combine(_root, "sim-bl-dst");
    Directory.CreateDirectory(sourcePath);
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "b.tmp"), "temp");

    var filter = ExtensionFilter.Parse(ExtensionFilterMode.Blacklist, "tmp");

    var result = await CopySimulationService.SimulateAsync(
        sourcePath, new[] { destination }, skipUnchanged: false, CancellationToken.None,
        extensionFilter: filter);

    Assert.Equal(1, result.TotalFiles);
}

[Fact]
public async Task SimulateAsync_NullFilter_CountsAllFiles()
{
    string sourcePath = Path.Combine(_root, "sim-nf-src");
    string destination = Path.Combine(_root, "sim-nf-dst");
    Directory.CreateDirectory(sourcePath);
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourcePath, "b.txt"), "text");

    var result = await CopySimulationService.SimulateAsync(
        sourcePath, new[] { destination }, skipUnchanged: false, CancellationToken.None);

    Assert.Equal(2, result.TotalFiles);
}
```

Add `using Sbroglione.Models;` to the top of `CopySimulationServiceTests.cs` if not already present.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Sbroglione.Tests --filter CopySimulationServiceTests`
Expected: FAIL — compile error, `extensionFilter` parameter does not exist on `SimulateAsync`.

- [ ] **Step 3: Implement the filter parameter**

In `Sbroglione/Services/CopySimulationService.cs`, add `using Sbroglione.Models;` to the top, then change:

```csharp
    public static Task<CopySimulationResult> SimulateAsync(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        CancellationToken ct,
        ExtensionFilter? extensionFilter = null)
    {
        return Task.Run(() => Simulate(sourcePath, destinationRoots, skipUnchanged, extensionFilter, ct), ct);
    }

    private static CopySimulationResult Simulate(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        ExtensionFilter? extensionFilter,
        CancellationToken ct)
    {
        bool isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
            throw new FileNotFoundException("Missing simulation source", sourcePath);

        if (!isDirectory)
            return SimulateSingleFile(sourcePath, destinationRoots, ct);

        List<(string Source, string Relative)> files = Directory.EnumerateFiles(sourcePath, "*", SafeEnumeration)
            .Where(f => extensionFilter is null || extensionFilter.Matches(f))
            .Select(f => (f, Path.GetRelativePath(sourcePath, f)))
            .ToList();
```

(Keep the rest of `Simulate` and `SimulateSingleFile` unchanged — `SimulateSingleFile` intentionally does not receive the filter, matching the real-copy behavior where a single-file source is always copied regardless of filter.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Sbroglione.Tests --filter CopySimulationServiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/Services/CopySimulationService.cs Sbroglione.Tests/CopySimulationServiceTests.cs
git commit -m "feat: apply extension filter in CopySimulationService dry-run"
```

---

### Task 4: Per-pair filter properties on `FolderFilePairViewModel`

**Model:** haiku — mechanical property additions following an exact existing pattern (`SkipUnchanged`/`SourceExists`).

**Files:**
- Modify: `Sbroglione/ViewModels/FolderFilePairViewModel.cs:317-320`
- Test: `Sbroglione.Tests` — find the existing test file covering `FolderFilePairViewModel` (search `grep -rl "FolderFilePairViewModel" Sbroglione.Tests/*.cs` if not obvious) and add tests there; if none exists, create `Sbroglione.Tests/FolderFilePairViewModelTests.cs`.

**Interfaces:**
- Consumes: `Sbroglione.Models.ExtensionFilterMode`, `Sbroglione.Models.ExtensionFilter.Parse` (Task 1).
- Produces: `FolderFilePairViewModel.ExtensionFilterMode` (`ExtensionFilterMode`, default `None`, ReactiveUI property), `FolderFilePairViewModel.ExtensionFilterText` (`string`, default `""`, ReactiveUI property), `FolderFilePairViewModel.IsExtensionFilterActive` (`bool`, computed, raises `PropertyChanged` whenever `ExtensionFilterMode` changes — used by the view to show/hide the extensions `TextBox`), `FolderFilePairViewModel.BuildExtensionFilter(): ExtensionFilter?` (builds from current `ExtensionFilterMode`/`ExtensionFilterText`, returns `null` when inactive — consumed by Task 5).

- [ ] **Step 1: Write the failing tests**

```csharp
// Sbroglione.Tests/FolderFilePairViewModelTests.cs (create if it doesn't already exist —
// check first with `grep -rl "class FolderFilePairViewModelTests" Sbroglione.Tests/*.cs`;
// if a file already exists, add these facts into it instead of creating a duplicate)
using Sbroglione.Models;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class FolderFilePairViewModelTests
{
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Sbroglione.Tests --filter FolderFilePairViewModelTests`
Expected: FAIL — `ExtensionFilterMode`, `ExtensionFilterText`, `IsExtensionFilterActive`, `BuildExtensionFilter` do not exist on `FolderFilePairViewModel`.

- [ ] **Step 3: Implement the properties**

In `Sbroglione/ViewModels/FolderFilePairViewModel.cs`, replace lines 313-320:

```csharp
    /// <summary>
    /// True per le coppie ripristinate dal journal: la copia di cartelle salta
    /// i file già identici in destinazione (stessa dimensione e mtime).
    /// </summary>
    public bool SkipUnchanged { get; set; }

    /// <summary>Se true, prima di copiare svuota tutte le destinazioni (primaria + extra), previa conferma.</summary>
    public bool ClearDestinationBeforeCopy { get; set; }

    private ExtensionFilterMode _extensionFilterMode = ExtensionFilterMode.None;

    /// <summary>Modalità del filtro per estensione applicato alla copia di cartelle di questa coppia.</summary>
    public ExtensionFilterMode ExtensionFilterMode
    {
        get => _extensionFilterMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _extensionFilterMode, value);
            this.RaisePropertyChanged(nameof(IsExtensionFilterActive));
        }
    }

    private string _extensionFilterText = string.Empty;

    /// <summary>Estensioni separate da virgola (es. "jpg,png,mp4") usate da <see cref="ExtensionFilterMode"/>.</summary>
    public string ExtensionFilterText
    {
        get => _extensionFilterText;
        set => this.RaiseAndSetIfChanged(ref _extensionFilterText, value);
    }

    /// <summary>True quando il filtro per estensione è attivo (modalità diversa da None); pilota la visibilità del campo testo in UI.</summary>
    public bool IsExtensionFilterActive => ExtensionFilterMode != ExtensionFilterMode.None;

    /// <summary>Costruisce il filtro per estensione corrente, o null se non attivo/senza estensioni valide.</summary>
    public ExtensionFilter? BuildExtensionFilter() => ExtensionFilter.Parse(ExtensionFilterMode, ExtensionFilterText);
```

(`FolderFilePairViewModel.cs` already has `using Sbroglione.Models;` at line 8 — no new using needed.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Sbroglione.Tests --filter FolderFilePairViewModelTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/ViewModels/FolderFilePairViewModel.cs Sbroglione.Tests/FolderFilePairViewModelTests.cs
git commit -m "feat: add per-pair extension filter properties to FolderFilePairViewModel"
```

---

### Task 5: Wire filter into `CopyPairsViewModel` copy + simulate calls

**Model:** sonnet — touches the largest/most stateful ViewModel in the app; needs to match existing test harness precisely.

**Files:**
- Modify: `Sbroglione/ViewModels/CopyPairsViewModel.cs:468-469` (`SimulatePairAsync`)
- Modify: `Sbroglione/ViewModels/CopyPairsViewModel.cs:724-731` (`CopyDirectoryAsync` private method)
- Test: `Sbroglione.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes: `FolderFilePairViewModel.BuildExtensionFilter(): ExtensionFilter?` (Task 4), `FileCopyService.CopyDirectoryToManyAsync(..., ExtensionFilter? extensionFilter = null)` (Task 2), `CopySimulationService.SimulateAsync(..., ExtensionFilter? extensionFilter = null)` (Task 3).
- Produces: no new public API — behavioral wiring only. A pair with `ExtensionFilterMode != None` now excludes non-matching files from both `StartCopyCommand`-driven directory copies and `SimulateCommand`.

First, read `Sbroglione.Tests/CopyPairsViewModelTests.cs` to find how it currently drives a directory copy end-to-end (temp dirs, `FolderFilePairViewModel` construction, awaiting `StartCopyCommand`/`pair.FilesLoad`) so the new test follows the exact same harness.

- [ ] **Step 1: Write the failing test**

Add to `Sbroglione.Tests/CopyPairsViewModelTests.cs`, mirroring its existing directory-copy test setup (temp source/destination dirs, `FolderFilePairViewModel` with `SourcePath`/`DestinationPath` set, then executing the start-copy path the same way existing tests in that file do):

```csharp
[Fact]
public async Task StartCopyAsync_WhitelistExtensionFilter_OnlyCopiesMatchingFiles()
{
    string sourceRoot = Path.Combine(_root, "vm-wl-src");
    string destinationRoot = Path.Combine(_root, "vm-wl-dst");
    Directory.CreateDirectory(sourceRoot);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.txt"), "text");

    var viewModel = new CopyPairsViewModel();
    var pair = new FolderFilePairViewModel
    {
        SourcePath = sourceRoot,
        DestinationPath = destinationRoot,
        ExtensionFilterMode = ExtensionFilterMode.Whitelist,
        ExtensionFilterText = "jpg"
    };
    await pair.SourceStateRefresh;

    await viewModel.StartCopyCommand.Execute(pair);

    Assert.True(File.Exists(Path.Combine(destinationRoot, "a.jpg")));
    Assert.False(File.Exists(Path.Combine(destinationRoot, "b.txt")));
}
```

Adjust this test's construction/execution calls (`viewModel.StartCopyCommand.Execute(pair)`, or whatever the file's existing convention is — e.g. it might call a plain method instead of a ReactiveCommand, or need `AppSettingsStore.Current` reset like `FileCopyServiceTests` does) to match exactly what neighboring tests in `CopyPairsViewModelTests.cs` already do — read the file first and copy its pattern precisely rather than guessing.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Sbroglione.Tests --filter StartCopyAsync_WhitelistExtensionFilter_OnlyCopiesMatchingFiles`
Expected: FAIL — `b.txt` also gets copied (filter not wired yet), or compile error if `ExtensionFilterMode`/`ExtensionFilterText` aren't recognized (they exist from Task 4, so this should be a behavioral failure, not compile).

- [ ] **Step 3: Wire the filter into `SimulatePairAsync`**

In `Sbroglione/ViewModels/CopyPairsViewModel.cs`, change line 468-469:

```csharp
            var result = await CopySimulationService.SimulateAsync(
                pair.SourcePath!, destinations, pair.SkipUnchanged, CancellationToken.None,
                extensionFilter: pair.BuildExtensionFilter());
```

- [ ] **Step 4: Wire the filter into the directory-copy call**

In the private `CopyDirectoryAsync` method, change the `FileCopyService.CopyDirectoryToManyAsync` call (line 724-731) to add the new named argument:

```csharp
        var result = await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: (destination, progress) => publisherByRoot[destination].Report(progress),
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged,
            extensionFilter: pair.BuildExtensionFilter(),
            onFileStarted: (destination, sourceFile) =>
```

(Only the `extensionFilter: pair.BuildExtensionFilter(),` line is new — keep every other line of this call exactly as-is, including the `onFileStarted`/`onFileCompleted`/`onFileFailed` lambdas that follow it.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Sbroglione.Tests --filter StartCopyAsync_WhitelistExtensionFilter_OnlyCopiesMatchingFiles`
Expected: PASS

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test`
Expected: PASS — no regressions in `CopyPairsViewModelTests`, `FileCopyServiceTests`, `CopySimulationServiceTests`.

- [ ] **Step 7: Commit**

```bash
git add Sbroglione/ViewModels/CopyPairsViewModel.cs Sbroglione.Tests/CopyPairsViewModelTests.cs
git commit -m "feat: wire per-pair extension filter into copy and simulate"
```

---

### Task 6: UI — extension filter controls + localization

**Model:** sonnet — XAML binding/converter conventions plus manual UI smoke-test judgment.

**Files:**
- Modify: `Sbroglione/Views/CopyPairsView.axaml:1-6` (xmlns) and `:147-149` (insert new controls after the `ClearDestination` `CheckBox`)
- Modify: `Sbroglione/Services/Localization/StringsEn.cs`
- Modify: `Sbroglione/Services/Localization/StringsIt.cs`

**Interfaces:**
- Consumes: `FolderFilePairViewModel.ExtensionFilterMode`, `ExtensionFilterText`, `IsExtensionFilterActive` (Task 4); `Sbroglione.Models.ExtensionFilterMode` enum values `None`/`Whitelist`/`Blacklist` (Task 1); localization keys `Str.CopyPairs.ExtensionFilterLabel`, `Str.CopyPairs.ExtensionFilterWatermark`.
- Produces: no new interface — this is the leaf UI task.

- [ ] **Step 1: Add the `models` xmlns**

In `Sbroglione/Views/CopyPairsView.axaml`, the root `<UserControl>` tag currently is:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:conv="clr-namespace:Sbroglione.Converters"
             xmlns:views="clr-namespace:Sbroglione.Views"
             x:Class="Sbroglione.Views.CopyPairsView">
```

Add a `models` xmlns (same convention as `Sbroglione/Views/ProfileEditorWindow.axaml:4`):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:conv="clr-namespace:Sbroglione.Converters"
             xmlns:views="clr-namespace:Sbroglione.Views"
             xmlns:models="clr-namespace:Sbroglione.Models"
             x:Class="Sbroglione.Views.CopyPairsView">
```

- [ ] **Step 2: Insert the filter controls after the "Clear destination" checkbox**

Current lines 147-149:

```xml
                  <CheckBox Content="{DynamicResource Str.CopyPairs.ClearDestination}"
                            IsChecked="{Binding ClearDestinationBeforeCopy}"
                            IsEnabled="{Binding !IsCopying}" />
```

Replace with (checkbox unchanged, new `StackPanel` added right after it, inside the same parent `StackPanel Spacing="8"` at line 93):

```xml
                  <CheckBox Content="{DynamicResource Str.CopyPairs.ClearDestination}"
                            IsChecked="{Binding ClearDestinationBeforeCopy}"
                            IsEnabled="{Binding !IsCopying}" />

                  <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                    <TextBlock Text="{DynamicResource Str.CopyPairs.ExtensionFilterLabel}"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource Brush.TextMuted}" />
                    <ComboBox SelectedItem="{Binding ExtensionFilterMode}"
                              IsEnabled="{Binding !IsCopying}"
                              MinWidth="120">
                      <ComboBox.Items>
                        <models:ExtensionFilterMode>None</models:ExtensionFilterMode>
                        <models:ExtensionFilterMode>Whitelist</models:ExtensionFilterMode>
                        <models:ExtensionFilterMode>Blacklist</models:ExtensionFilterMode>
                      </ComboBox.Items>
                    </ComboBox>
                    <TextBox Text="{Binding ExtensionFilterText}"
                             Watermark="{DynamicResource Str.CopyPairs.ExtensionFilterWatermark}"
                             IsVisible="{Binding IsExtensionFilterActive}"
                             IsEnabled="{Binding !IsCopying}"
                             Width="220" />
                  </StackPanel>
```

- [ ] **Step 3: Add localized strings**

In `Sbroglione/Services/Localization/StringsEn.cs`, right after the `Str.CopyPairs.ClearDestinationMessageFormat` entry (line 118):

```csharp
        ["Str.CopyPairs.ExtensionFilterLabel"] = "Extension filter:",
        ["Str.CopyPairs.ExtensionFilterWatermark"] = "e.g. jpg,png,mp4",
```

In `Sbroglione/Services/Localization/StringsIt.cs`, right after the `Str.CopyPairs.ClearDestinationMessageFormat` entry (line 122):

```csharp
        ["Str.CopyPairs.ExtensionFilterLabel"] = "Filtro estensioni:",
        ["Str.CopyPairs.ExtensionFilterWatermark"] = "es. jpg,png,mp4",
```

- [ ] **Step 4: Build and manually smoke-test**

Run: `dotnet build Sbroglione.sln`
Expected: build succeeds, no XAML binding errors in output.

Run: `dotnet run --project Sbroglione.Desktop`, open the "Copia file" tab, add a pair with a source folder containing mixed extensions, set the new ComboBox to "Whitelist" and type an extension in the text box, click Simulate — confirm the summary count reflects only matching files, then Start and confirm only matching files land in the destination. Repeat with "Blacklist". Confirm switching the ComboBox back to "None" hides the text box and copies everything again. Check both languages (EN/IT) render the new label/watermark correctly (see `Sbroglione/Services/Localization/LocalizationService.cs` for how to switch language in-app, or set it via existing app settings UI).

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/Views/CopyPairsView.axaml Sbroglione/Services/Localization/StringsEn.cs Sbroglione/Services/Localization/StringsIt.cs
git commit -m "feat: add extension whitelist/blacklist controls to copy pairs UI"
```
