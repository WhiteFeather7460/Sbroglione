# Audit performance e memory leak — 20 agosto 2026

Esito di due audit paralleli (memory leak + funzioni pesanti) sull'intero core `FileExplorer/`.
Questo documento è la spec di riferimento del piano `docs/superpowers/plans/2026-08-20-perf-leak-fixes.md`.

## Contesto architetturale (verificato)

- Le 7 tab sono istanziate una sola volta in `MainWindow.axaml` e vivono quanto il processo:
  i ViewModel delle tab sono app-lifetime "per caso".
- Nessun file usa `Dispatcher`; i servizi fan-out (confronto, duplicati, verifica, copia,
  byte-compare) **non usano `ConfigureAwait(false)`**: chiamati dai ReactiveCommand sul thread
  UI, ogni continuation dopo un `await` torna sul dispatcher Avalonia. Il lavoro per-file
  (aperture file, hash parziali a 64 KB, confronto a blocchi da 1 MB, callback di progresso)
  gira di fatto serializzato sul thread UI, annullando il parallelismo dichiarato.
- Eccezioni virtuose già corrette: `CopySimulationService`, `DiskUsageService`,
  `FileSystemService.List*` (interamente in `Task.Run`), `WatchFolderService`
  (`ConfigureAwait(false)` sistematico).

## Finding performance (ordinati per impatto)

| # | Dove | Problema | Fix |
|---|------|----------|-----|
| P1 | `DirectoryComparisonService.cs:76-105`, `DuplicateFinderService.cs:116-131` + `ChecksumService.cs:30-48`, `DirectoryVerificationService.cs:58-84`, `FileByteCompareService.cs:79-117`, `FileCopyService.cs:149-177,208-239` | Lavoro per-file su thread UI (niente `ConfigureAwait(false)`); 20k file = decine di migliaia di `File.Open` sync + dispatcher post sul thread UI | `ConfigureAwait(false)` sistematico nei servizi; i callback di progresso vanno marshalled esplicitamente nei VM |
| P2 | `FileCopyService.cs:139-140,198-199` (+ `DirectoryVerificationService.cs:50`) | `EnumerateFiles().ToList()` + `FileInfo.Length` per file, sincroni sul thread chiamante (UI), prima di ogni copia cartella | Prologo dentro `Task.Run`, passata unica `(path, length)` |
| P3 | `FolderFilePairViewModel.cs:61-98` + `FileSystemService.cs:89-105` | Ogni set di `SourcePath` fa listing ricorsivo completo solo per l'Expander "Mostra file da elaborare" (di solito chiuso) + N `Add` per-item su ObservableCollection; nessuna cancellazione | Caricamento lazy alla prima apertura dell'Expander, cancellazione della scansione precedente, swap in blocco della lista |
| P4 | `CopyPairsViewModel.cs:511-518,567-582`; `StatusText` per-file in `ComparisonViewModel.cs:239`, `DuplicatesViewModel.cs:122`, `CopyPairsViewModel.cs:616` | `pair.Progress` aggiornato a ogni blocco da 1 MB (~3000/s su NVMe); `StatusText` riformattato/notificato per ogni file | Throttle ~10 update/s (pattern `SpeedTracker.TryTakeSnapshot`), flush finale esatto |
| P5 | `TreemapControl.cs:31-98` | Un `Border` per ogni figlio senza cap (10k file = 10k controlli, quasi tutti sub-pixel), rebuild integrale a ogni tick di resize | Cap tasselli + tassello aggregato "altri N", skip rect < 1 px, debounce `SizeChanged` ~100 ms |
| P6 | `RemoteBrowserViewModel.cs:130-158,739-748,800-812` | `RefreshLocalStatuses`: un `FileInfo` sync per voce sul thread UI; filtri: `RebuildVisibleItems` a ogni keystroke | Stat via `Task.Run` con assegnazione a fine corsa; debounce ~200 ms sui filtri |
| P7 | `CopySimulationService.cs:64-101` | 4-5 stat per file per destinazione in passate separate (20k file × 2 dest ≈ 120k+ stat) | Passata unica che raccoglie `FileInfo` sorgente una volta |
| P8 | `App.axaml.cs:37-51` → `WatchFolderService.cs:86`; `WatchFoldersViewModel.cs:141-155` | `Directory.Exists` sincrono sul thread UI (sorgente di rete irraggiungibile = timeout SMB prima che la finestra appaia) | `WatchFolderService.Start` dietro `Task.Run` |
| P9 | `DuplicatesView.axaml:45-82` + `DuplicatesViewModel.cs:125-126` | `ItemsControl` non virtualizzato (migliaia di gruppi = migliaia di controlli reali) + `Groups.Add` per-item | `ListBox` virtualizzato per i gruppi + popolamento in blocco |

Nota threading (correttezza, non solo perf): dopo P1 i callback dei servizi girano davvero su
threadpool → ogni set di proprietà reactive nei callback dei VM deve passare da
`Dispatcher.UIThread.Post`. Già oggi `DiskUsageService.onFilesScanned`
(`DiskUsageViewModel.cs:98-99`) assegna cross-thread: data race latente da sistemare insieme.

## Finding memory leak

| # | Dove | Problema | Fix |
|---|------|----------|-----|
| L1 | `CopyPairsViewModel.cs:77-81` | Lambda anonima su evento statico `AppSettingsStore.ThrottleChanged`: VM rooted per sempre, non desottoscrivibile. Leak latente che si attiva al primo cambio di lifetime delle tab (PR #22) e in ogni run di test | Handler in campo + `IDisposable` (pattern `WatchFoldersViewModel`) |
| L2 | `SettingsViewModel.cs:19-23` | Identico a L1 | Identico a L1 |
| L3 | `WatchFoldersView.axaml.cs:14` | `WatchFoldersViewModel` è `IDisposable` ma mai disposto (accettato oggi, documentato) | Nessuna azione in questo piano; nota per il futuro cambio di lifetime |
| L4 | `DiskTypeService.cs:22,41-55,87` | Cache statica senza eviction: le entry scadute restano; su macOS chiave = full path → crescita unbounded | `TryRemove` delle entry scadute al lookup |

## Esiti negativi (verificati, nessuna azione)

Nessun `ReadAllBytes` (streaming ovunque); nessun O(n²) (dictionary con comparer corretti);
CTS tutti disposti; finestre effimere pulite; client FTP/SFTP disposti correttamente;
`SpeedTracker` bounded e throttled; `WatchFolderService` interno pulito (watcher disposti,
loop cancellabili); DataGrid di RemoteBrowser/SelectPathDialog/CopyPairs virtualizzano
(il `MaxHeight=220` dà viewport finita); converter O(1).
