# Graph Report - .  (2026-08-17)

## Corpus Check
- 94 files · ~53,098 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1058 nodes · 1903 edges · 100 communities (63 shown, 37 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 36 edges (avg confidence: 0.83)
- Token cost: 141,793 input · 141,793 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Domain 0|Domain 0]]
- [[_COMMUNITY_Domain 1|Domain 1]]
- [[_COMMUNITY_Domain 2|Domain 2]]
- [[_COMMUNITY_Domain 3|Domain 3]]
- [[_COMMUNITY_UI & ViewModels|UI & ViewModels]]
- [[_COMMUNITY_Domain 5|Domain 5]]
- [[_COMMUNITY_Domain 6|Domain 6]]
- [[_COMMUNITY_Domain 7|Domain 7]]
- [[_COMMUNITY_Domain 8|Domain 8]]
- [[_COMMUNITY_Services & Stores|Services & Stores]]
- [[_COMMUNITY_Domain 10|Domain 10]]
- [[_COMMUNITY_Domain 11|Domain 11]]
- [[_COMMUNITY_Domain 12|Domain 12]]
- [[_COMMUNITY_Domain 13|Domain 13]]
- [[_COMMUNITY_Tests & Validation|Tests & Validation]]
- [[_COMMUNITY_UI & ViewModels|UI & ViewModels]]
- [[_COMMUNITY_Domain 16|Domain 16]]
- [[_COMMUNITY_Domain 17|Domain 17]]
- [[_COMMUNITY_Domain 18|Domain 18]]
- [[_COMMUNITY_Domain 19|Domain 19]]
- [[_COMMUNITY_Domain 20|Domain 20]]
- [[_COMMUNITY_Domain 21|Domain 21]]
- [[_COMMUNITY_Domain 22|Domain 22]]
- [[_COMMUNITY_Domain 23|Domain 23]]
- [[_COMMUNITY_Domain 24|Domain 24]]
- [[_COMMUNITY_Domain 25|Domain 25]]
- [[_COMMUNITY_Domain 26|Domain 26]]
- [[_COMMUNITY_Domain 27|Domain 27]]
- [[_COMMUNITY_Styling & Conversion|Styling & Conversion]]
- [[_COMMUNITY_Domain 29|Domain 29]]
- [[_COMMUNITY_Domain 30|Domain 30]]
- [[_COMMUNITY_Domain 31|Domain 31]]
- [[_COMMUNITY_Domain 32|Domain 32]]
- [[_COMMUNITY_Domain 33|Domain 33]]
- [[_COMMUNITY_Domain 34|Domain 34]]
- [[_COMMUNITY_Domain 35|Domain 35]]
- [[_COMMUNITY_Services & Stores|Services & Stores]]
- [[_COMMUNITY_Domain 37|Domain 37]]
- [[_COMMUNITY_UI & ViewModels|UI & ViewModels]]
- [[_COMMUNITY_Domain 39|Domain 39]]
- [[_COMMUNITY_Domain 40|Domain 40]]
- [[_COMMUNITY_Styling & Conversion|Styling & Conversion]]
- [[_COMMUNITY_Domain 42|Domain 42]]
- [[_COMMUNITY_Domain 43|Domain 43]]
- [[_COMMUNITY_Domain 44|Domain 44]]
- [[_COMMUNITY_Domain 45|Domain 45]]
- [[_COMMUNITY_Domain 46|Domain 46]]
- [[_COMMUNITY_Domain 47|Domain 47]]
- [[_COMMUNITY_Domain 48|Domain 48]]
- [[_COMMUNITY_Services & Stores|Services & Stores]]
- [[_COMMUNITY_Domain 50|Domain 50]]
- [[_COMMUNITY_UI & ViewModels|UI & ViewModels]]
- [[_COMMUNITY_Services & Stores|Services & Stores]]
- [[_COMMUNITY_Domain 53|Domain 53]]
- [[_COMMUNITY_UI & ViewModels|UI & ViewModels]]
- [[_COMMUNITY_Tests & Validation|Tests & Validation]]
- [[_COMMUNITY_Tests & Validation|Tests & Validation]]
- [[_COMMUNITY_Domain 57|Domain 57]]
- [[_COMMUNITY_Domain 58|Domain 58]]
- [[_COMMUNITY_Tests & Validation|Tests & Validation]]
- [[_COMMUNITY_Domain 60|Domain 60]]
- [[_COMMUNITY_Domain 61|Domain 61]]
- [[_COMMUNITY_Domain 62|Domain 62]]
- [[_COMMUNITY_Domain 63|Domain 63]]
- [[_COMMUNITY_Domain 64|Domain 64]]
- [[_COMMUNITY_Domain 65|Domain 65]]
- [[_COMMUNITY_Domain 66|Domain 66]]
- [[_COMMUNITY_Domain 67|Domain 67]]
- [[_COMMUNITY_Domain 68|Domain 68]]
- [[_COMMUNITY_Domain 69|Domain 69]]
- [[_COMMUNITY_Domain 70|Domain 70]]
- [[_COMMUNITY_Domain 71|Domain 71]]
- [[_COMMUNITY_Domain 72|Domain 72]]
- [[_COMMUNITY_Domain 73|Domain 73]]
- [[_COMMUNITY_Domain 74|Domain 74]]
- [[_COMMUNITY_Domain 75|Domain 75]]
- [[_COMMUNITY_Domain 76|Domain 76]]
- [[_COMMUNITY_Domain 77|Domain 77]]
- [[_COMMUNITY_Domain 78|Domain 78]]
- [[_COMMUNITY_Domain 81|Domain 81]]
- [[_COMMUNITY_Domain 85|Domain 85]]
- [[_COMMUNITY_Domain 92|Domain 92]]
- [[_COMMUNITY_Tests & Validation|Tests & Validation]]
- [[_COMMUNITY_Domain 94|Domain 94]]
- [[_COMMUNITY_Domain 95|Domain 95]]
- [[_COMMUNITY_Domain 96|Domain 96]]
- [[_COMMUNITY_Domain 97|Domain 97]]
- [[_COMMUNITY_Domain 98|Domain 98]]
- [[_COMMUNITY_Domain 99|Domain 99]]

## God Nodes (most connected - your core abstractions)
1. `Task` - 41 edges
2. `RemoteBrowserViewModel` - 40 edges
3. `RemoteBrowserViewModelTests` - 29 edges
4. `RemoteBrowserDownloadTests` - 23 edges
5. `Task` - 23 edges
6. `Fact` - 23 edges
7. `DownloadServiceTests` - 22 edges
8. `AppSettingsStore` - 22 edges
9. `RemoteBrowserView` - 22 edges
10. `Task` - 21 edges

## Surprising Connections (you probably didn't know these)
- `Verify post-copy (copy + verify)` --semantically_similar_to--> `Buffer Size Validation (256KB-16MB)`  [INFERRED] [semantically similar]
  IDEE.md → FileExplorer/Services/AppSettingsStore.cs
- `Integrated disk benchmark` --conceptually_related_to--> `Manual Parallelism Validation (1-32)`  [INFERRED]
  IDEE.md → FileExplorer/Services/AppSettingsStore.cs
- `Program` --references--> `App`  [EXTRACTED]
  FileExplorer.Desktop/Program.cs → FileExplorer/App.axaml
- `FileExplorer` --references--> `AppSettingsStore`  [INFERRED]
  FileExplorer/FileExplorer.csproj → FileExplorer/Services/AppSettingsStore.cs
- `AppSettingsStoreTests` --references--> `AppSettingsStore`  [EXTRACTED]
  FileExplorer.Tests/AppSettingsStoreTests.cs → FileExplorer/Services/AppSettingsStore.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Copy Operation Workflow** — viewmodels_copyairsviewmodel, services_filecopyservice, services_disktypeservice, services_checksumservice, services_copyparallelismresolver [EXTRACTED 1.00]
- **Settings Persistence & Configuration** — viewmodels_settingsviewmodel, services_appsettingsstore, models_appsettings [EXTRACTED 1.00]
- **Path Selection Dialog** — views_selectpathdialog_axaml_cs, viewmodels_selectpathdialogviewmodel, services_filesystemservice [EXTRACTED 1.00]
- **File Copy Processing Flow** — viewmodels_copypairsviewmodel_startcopyasync, services_filecopryservice_copyfileasync, services_checksumservice_computesha256async [INFERRED 0.85]
- **Disk-Aware Copy Optimization** — services_disktypeservice_getdisktypeasync, services_disktypeservice_parserotationalflag, services_filecopryservice_copydirectoryasync [INFERRED 0.80]
- **Service Test Coverage** — tests_appsettingsstoretests_appsettingsstoretests, tests_filecopyrservicetests_filecopyrservicetests, tests_disktypeservicetests_disktypeservicetests, tests_copypairsviewmodeltests_copypairsviewmodeltests [EXTRACTED 1.00]
- **Settings Lifecycle (Load → Validate → Save)** — services_appsettingsstore_loadcurrentasync, services_appsettingsstore_clamp, services_appsettingsstore_savecurrentasync [INFERRED 0.95]
- **Button Styling Variants** — styles_controls_buttonprimary, styles_controls_buttonsecondary, styles_controls_buttoniconbtn, styles_controls_buttononaccent [INFERRED 0.90]
- **Copy Operations Enhancement Ideas** — idee_copyverification, idee_persistentcopyqueue, idee_multidestinationcopy, idee_ioThrottling, idee_deltacopy [INFERRED 0.85]
- **Platform-specific Credential Store Implementations** — services_mackeychaincredentialstore, services_secrettoolcredentialstore, services_windowscredentialstore, services_nullcredentialstore [EXTRACTED 1.00]
- **ViewModels Hierarchy** — viewmodels_viewmodelbase, viewmodels_copypairsviewmodel, viewmodels_filebrowserviewmodel, viewmodels_mainwindowviewmodel, viewmodels_profileeditorviewmodel, viewmodels_remotebrowserviewmodel, viewmodels_remoteentryviewmodel, viewmodels_settingsviewmodel [EXTRACTED 1.00]
- **Remote File Operations Architecture** — services_iremotetileclient, services_sftpremoterteclient, services_remoteclientfactory, services_uploadservice, viewmodels_remotebrowserviewmodel [INFERRED 0.85]
- **Settings configuration and persistence flow** — viewmodels_settingsviewmodel, services_appsettingsstore, views_settingsview [INFERRED 0.95]
- **Application styling and theming system** — app, styles_palette, styles_controls, avalonia_fluenttheme, viewmodels_settingsviewmodel [INFERRED 0.85]
- **File operations and verification suite** — services_filecopservice, services_checksumservice, tests_filecopservicetests [INFERRED 0.75]

## Communities (100 total, 37 thin omitted)

### Community 0 - "Domain 0"
Cohesion: 0.09
Nodes (22): BlockingCredentialStore, CancellationToken, ConnectionProfile, Fact, FakeRemoteClient, Func, Guid, ICredentialStore (+14 more)

### Community 1 - "Domain 1"
Cohesion: 0.07
Nodes (26): Platform-specific Credential Store Strategy, Guid, Task, Completed, ExitCode, Guid, int, Process (+18 more)

### Community 2 - "Domain 2"
Cohesion: 0.10
Nodes (16): CancellationTokenSource, DateTimeOffset, bool, ConnectionProfile, double, DownloadFilter, Func, ICredentialStore (+8 more)

### Community 3 - "Domain 3"
Cohesion: 0.13
Nodes (18): bool, CancellationToken, ConnectionProfile, Fact, FakeRemoteClient, IProgress, IRemoteFileClient, RemoteBrowserViewModel (+10 more)

### Community 4 - "UI & ViewModels"
Cohesion: 0.07
Nodes (19): CopyPairsViewModelTests, AppSettings, Fact, string, Task, Fact, string, Task (+11 more)

### Community 5 - "Domain 5"
Cohesion: 0.12
Nodes (16): CancellationToken, ConnectionProfile, Fact, FakeRemoteClient, IProgress, IRemoteFileClient, RemoteBrowserViewModel, RemoteError (+8 more)

### Community 6 - "Domain 6"
Cohesion: 0.12
Nodes (17): CancellingRemoteClient, CancellationToken, ConnectionProfile, DownloadFilter, DownloadReport, Fact, FakeRemoteClient, IProgress (+9 more)

### Community 7 - "Domain 7"
Cohesion: 0.12
Nodes (18): CancellationToken, ConnectionProfile, DateTime, Fact, FakeRemoteClient, IProgress, IReadOnlyList, RemoteError (+10 more)

### Community 8 - "Domain 8"
Cohesion: 0.10
Nodes (18): Guid, Task, Completed, ExitCode, Guid, int, Process, ProcessStartInfo (+10 more)

### Community 9 - "Services & Stores"
Cohesion: 0.09
Nodes (11): ConnectionProfile, ICredentialStore, RemoteBrowserViewModel, RoutedEventArgs, TappedEventArgs, Task, UserControl, CopyPairsView (+3 more)

### Community 10 - "Domain 10"
Cohesion: 0.14
Nodes (13): CacheKey, ConcurrentDictionary, AppSettings, DiskType, CancellationToken, DiskType, Task, TimeSpan (+5 more)

### Community 11 - "Domain 11"
Cohesion: 0.08
Nodes (25): net8.0, Microsoft.NET.Sdk, net8.0, Microsoft.NET.Sdk, FileExplorer.Tests, Microsoft.NET.Sdk, net10.0, Avalonia ($(AvaloniaVersion)) (+17 more)

### Community 12 - "Domain 12"
Cohesion: 0.16
Nodes (13): CancellationToken, ConnectionProfile, DateTime, Exception, IProgress, RemoteError, RemoteItem, RemoteListingResult (+5 more)

### Community 13 - "Domain 13"
Cohesion: 0.17
Nodes (13): AsyncFtpClient, CancellationToken, ConnectionProfile, DateTime, Exception, IProgress, RemoteError, RemoteItem (+5 more)

### Community 14 - "Tests & Validation"
Cohesion: 0.18
Nodes (7): AppSettingsStoreTests, AppSettings, Fact, InlineData, string, Task, Theory

### Community 15 - "UI & ViewModels"
Cohesion: 0.13
Nodes (9): ProfileEditorViewModel, RoutedEventArgs, RoutedEventArgs, TappedEventArgs, KeyEventArgs, MainWindow, ProfileEditorWindow, SelectPathDialog (+1 more)

### Community 16 - "Domain 16"
Cohesion: 0.18
Nodes (7): DiskType, Fact, InlineData, string, Task, Theory, DiskTypeServiceTests

### Community 17 - "Domain 17"
Cohesion: 0.18
Nodes (10): CancellationToken, ConnectionProfile, DateTime, IProgress, RemoteError, RemoteItem, RemoteListingResult, Task (+2 more)

### Community 18 - "Domain 18"
Cohesion: 0.18
Nodes (9): DirectoryInfo, DirectoryListingResult, Exception, FileSystemItem, Task, FileInfo, ListingError, PathType (+1 more)

### Community 19 - "Domain 19"
Cohesion: 0.14
Nodes (13): DownloadProgress, CancellationToken, DownloadFilter, DownloadReport, IProgress, IReadOnlyList, IRemoteFileClient, LocalFileStatus (+5 more)

### Community 20 - "Domain 20"
Cohesion: 0.21
Nodes (6): Fact, InlineData, string, Task, Theory, FileSystemServiceTests

### Community 21 - "Domain 21"
Cohesion: 0.22
Nodes (7): ConnectionProfile, Fact, InlineData, ProfileEditorViewModel, Task, Theory, ProfileEditorViewModelTests

### Community 22 - "Domain 22"
Cohesion: 0.18
Nodes (12): CancellationToken, Dictionary, IProgress, IReadOnlyList, IRemoteFileClient, RemoteItem, Task, TimeSpan (+4 more)

### Community 23 - "Domain 23"
Cohesion: 0.25
Nodes (9): CancellationToken, Dictionary, Task, FolderFilePairViewModel, CopyStateKind, MVVM Data Binding Pattern, ReactiveUI Observable Pattern, CopyPairsViewModelTests (+1 more)

### Community 24 - "Domain 24"
Cohesion: 0.29
Nodes (7): Credential, DllImport, Guid, int, Task, IntPtr, WindowsCredentialStore

### Community 25 - "Domain 25"
Cohesion: 0.25
Nodes (9): CancellationToken, ConnectionProfile, IProgress, RemoteError, RemoteItem, RemoteListingResult, Task, IAsyncDisposable (+1 more)

### Community 26 - "Domain 26"
Cohesion: 0.19
Nodes (6): DateTime, Fact, InlineData, RemoteItem, Theory, DownloadFilterTests

### Community 27 - "Domain 27"
Cohesion: 0.30
Nodes (5): DateTime, Fact, RemoteItem, string, DownloadServiceStatusTests

### Community 28 - "Styling & Conversion"
Cohesion: 0.21
Nodes (7): EnumEqualsConverter, LocalFileStatusConverter, CultureInfo, Type, CultureInfo, Type, IValueConverter

### Community 29 - "Domain 29"
Cohesion: 0.17
Nodes (12): OnFrameworkInitializationCompleted, int, JsonSerializerOptions, Concurrency Serialization via SemaphoreSlim, JSON Serialization with Atomic Writes, SemaphoreSlim, AppSettingsStore, LoadCurrent (+4 more)

### Community 30 - "Domain 30"
Cohesion: 0.29
Nodes (7): Action, CopyProgress, CancellationToken, int, Task, CopyProgress (record struct), FileCopyService

### Community 31 - "Domain 31"
Cohesion: 0.24
Nodes (7): Parallelism auto/manual resolution, AppSettings, RaisePropertyChanged, Auto-save property pattern, Fire-and-forget async save, SettingsViewModelTests, SettingsView

### Community 32 - "Domain 32"
Cohesion: 0.20
Nodes (7): CopyStateKind, bool, double, string, Task, ObservableCollection, FolderFilePairViewModel

### Community 33 - "Domain 33"
Cohesion: 0.29
Nodes (5): CopyParallelismResolverTests, DiskType, Fact, InlineData, Theory

### Community 34 - "Domain 34"
Cohesion: 0.33
Nodes (4): Fact, string, Task, ProfileStoreTests

### Community 35 - "Domain 35"
Cohesion: 0.33
Nodes (4): Fact, string, Task, SelectPathDialogViewModelTests

### Community 36 - "Services & Stores"
Cohesion: 0.22
Nodes (6): ConnectionProfile, ICredentialStore, string, Task, RemoteProtocol, ProfileEditorViewModel

### Community 38 - "UI & ViewModels"
Cohesion: 0.31
Nodes (5): LocalFileStatus, FileBrowserViewModel, MainWindowViewModel, RemoteEntryViewModel, ViewModelBase

### Community 39 - "Domain 39"
Cohesion: 0.25
Nodes (8): Buffer Size Validation (256KB-16MB), Manual Parallelism Validation (1-32), Verify post-copy (copy + verify), Integrated disk benchmark, Real-time speed graph, Clamp, Load, LoadAsync

### Community 40 - "Domain 40"
Cohesion: 0.33
Nodes (3): Application, App, ThemeVariant

### Community 41 - "Styling & Conversion"
Cohesion: 0.29
Nodes (5): NotAnyConverter, CultureInfo, Type, IList, IMultiValueConverter

### Community 42 - "Domain 42"
Cohesion: 0.43
Nodes (4): string, FileSystemItem, ReactiveObject, SelectPathDialog (code-behind)

### Community 43 - "Domain 43"
Cohesion: 0.29
Nodes (5): CancellationToken, Task, ChecksumService, ComputeSha256Async, CopySingleFileAsync

### Community 44 - "Domain 44"
Cohesion: 0.38
Nodes (3): Fact, string, SftpHostKeyFingerprintTests

### Community 45 - "Domain 45"
Cohesion: 0.38
Nodes (5): bool, FileSystemItem, string, Task, SelectPathDialogViewModel

### Community 46 - "Domain 46"
Cohesion: 0.29
Nodes (7): CopyStateKind, GetDiskTypeAsync, ParseRotationalFlag, CopyDirectoryAsync, CopyFileAsync, CopyDirectoryAsync, StartCopyAsync

### Community 47 - "Domain 47"
Cohesion: 0.33
Nodes (6): App, FluentTheme, Program, FontAwesomeIconProvider, Controls (Styling), Palette

### Community 48 - "Domain 48"
Cohesion: 0.40
Nodes (3): AppBuilder, Program, STAThread

### Community 49 - "Services & Stores"
Cohesion: 0.40
Nodes (3): CredentialStoreFactoryTests, Fact, Task

### Community 53 - "Domain 53"
Cohesion: 0.50
Nodes (4): Border.card, Button.primary, Button.secondary, Theme-Aware Brush System

### Community 54 - "UI & ViewModels"
Cohesion: 0.50
Nodes (4): Palette (Theme Brushes), CopyPairsView, MainWindow, SettingsView

### Community 55 - "Tests & Validation"
Cohesion: 0.67
Nodes (3): Buffer size parameter pattern, FileCopyService, FileCopyServiceTests

## Knowledge Gaps
- **257 isolated node(s):** `net8.0`, `Avalonia.Desktop ($(AvaloniaVersion))`, `Microsoft.NET.Sdk`, `STAThread`, `AppBuilder` (+252 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **37 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `CopyPairsViewModel` connect `Domain 23` to `Domain 32`, `UI & ViewModels`, `Domain 10`, `Domain 43`, `Domain 42`, `UI & ViewModels`, `Domain 30`, `Domain 31`?**
  _High betweenness centrality (0.054) - this node is a cross-community bridge._
- **Why does `UploadServiceTests` connect `Domain 7` to `UI & ViewModels`?**
  _High betweenness centrality (0.049) - this node is a cross-community bridge._
- **Why does `DownloadServiceTests` connect `Domain 6` to `UI & ViewModels`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **What connects `net8.0`, `Avalonia.Desktop ($(AvaloniaVersion))`, `Microsoft.NET.Sdk` to the rest of the system?**
  _267 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Domain 0` be split into smaller, more focused modules?**
  _Cohesion score 0.09045045045045046 - nodes in this community are weakly interconnected._
- **Should `Domain 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06570048309178744 - nodes in this community are weakly interconnected._
- **Should `Domain 2` be split into smaller, more focused modules?**
  _Cohesion score 0.10404040404040404 - nodes in this community are weakly interconnected._