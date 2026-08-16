# Impostazioni copia + parallelismo adattivo — Design

Data: 2026-08-16
Stato: approvato in brainstorming, in attesa di piano di implementazione

## Obiettivo

Adattare dinamicamente il parallelismo della copia cartelle in base al tipo di disco
(SSD vs HDD) di sorgente e destinazione, ed esporre una nuova tab "Impostazioni" per
configurare parallelismo (auto/manuale), buffer size di copia, verifica checksum
post-copia, e tema chiaro/scuro/sistema.

## Decisioni chiave (dal brainstorming)

- Nessun DI container (coerente con resto app): persistenza e stato via servizio
  statico `AppSettingsStore`, stesso pattern di `ProfileStore` (JSON in AppData).
- Rilevamento SSD/HDD **cross-platform** (Linux/Windows/macOS), non solo Linux.
- Rilevamento considera **sorgente E destinazione**, vince il caso peggiore
  (se uno dei due è HDD → parallelismo 1, sequenziale).
- Rilevamento scatta **solo all'avvio della copia** (dentro `CopyDirectoryAsync`),
  mai su modifica dei campi percorso — evita query OS inutili su path incompleti.
- Si applica solo a copia cartella (dove il parallelismo esiste). Copia singolo file
  resta sequenziale a blocchi; beneficia solo della buffer size configurabile.
- Override manuale: toggle "Automatico" ON di default; OFF abilita un valore
  numerico esplicito 1-32 thread.
- Buffer size configurabile 256KB-16MB, default 1MB (invariato rispetto a oggi).
- Verifica checksum post-copia (oggi sempre attiva su singolo file) diventa toggle.
- Tema: non ancora configurabile da UI oggi (solo hardcoded `RequestedThemeVariant`
  in `App.axaml`) — aggiunto qui, applicato live senza restart.
- Persistenza: **auto-save** ad ogni modifica in Impostazioni (no bottone "Salva"),
  coerente con l'assenza di conferme esplicite nel resto dell'app.
- Fallback rilevamento fallito (permessi, path di rete, drive rimosso) → `Unknown`,
  trattato come SSD (parallelismo pieno) per non peggiorare il comportamento attuale.

## Modelli (`FileExplorer/Models/`)

- `AppSettings` (ReactiveObject): `AutoParallelism` (bool, default `true`),
  `ManualParallelism` (int, default `Math.Max(2, ProcessorCount - 1)`, range 1-32),
  `BufferSizeBytes` (int, default `1_048_576`, range 256KB-16MB),
  `VerifyChecksumAfterCopy` (bool, default `true`),
  `ThemeVariant` (string, default `"Default"`, valori `"Default"`/`"Dark"`/`"Light"`).
- `DiskType` (enum): `Ssd`, `Hdd`, `Unknown`.

## Servizi (`FileExplorer/Services/`)

- `AppSettingsStore` (statico, mirror di `ProfileStore`):
  - `DefaultPath` → `%AppData%/FileExplorer/settings.json`
  - `LoadAsync(path)` → deserializza; default (`new AppSettings()`) se file
    manca/corrotto
  - `SaveAsync(path, AppSettings)` → scrive JSON, crea cartella se assente
  - `Current` → istanza statica caricata in `App.axaml.cs` prima della creazione
    di `MainWindow`
- `DiskTypeService` (statico):
  - `GetDiskTypeAsync(string path, CancellationToken ct) → Task<DiskType>`
  - Implementazione per OS dietro `RuntimeInformation.IsOSPlatform`:
    - Linux: risolve device montato per `path` (parse `/proc/mounts`), legge
      `/sys/block/<dev>/queue/rotational` (0=SSD, 1=HDD)
    - Windows: WMI `MSFT_PhysicalDisk.MediaType` mappato al volume del path
    - macOS: parsing output `diskutil info` sul campo "Solid State"/"Media Type"
  - Cache `ConcurrentDictionary<string driveRoot, (DiskType, DateTime cachedAt)>`,
    TTL 5 minuti
  - Logica di parsing (rotational flag, output diskutil/WMI) estratta in funzioni
    pure testabili, separate dalle chiamate OS reali
- `CopyParallelismResolver` (statico, funzione pura testabile):
  - `Resolve(AppSettings settings, DiskType sourceType, DiskType destType) → int`
  - Auto: `sourceType == Hdd || destType == Hdd` → `1`, altrimenti
    `Math.Max(2, ProcessorCount - 1)`
  - Manuale: `settings.ManualParallelism`

## Modifiche a servizi esistenti

- `FileCopyService.CopyFileAsync`: `BufferSize` da costante privata a parametro
  con default, chiamato passando `AppSettingsStore.Current.BufferSizeBytes`
- `CopyPairsViewModel.CopySingleFileAsync`: blocco verifica checksum condizionato
  a `AppSettingsStore.Current.VerifyChecksumAfterCopy`; se `false`, skip diretto a
  stato "Completato" (niente `SourceChecksum`/`DestinationChecksum` calcolati)
- `CopyPairsViewModel.CopyDirectoryAsync`: `maxDegreeOfParallelism` calcolato via
  `DiskTypeService.GetDiskTypeAsync` (source + dest) + `CopyParallelismResolver`
  invece del valore fisso `ProcessorCount - 1`

## ViewModel e UI

- `MainWindow.axaml` → terza `TabItem` "Impostazioni" (icona `fa-solid fa-gear`),
  dopo "Server remoto"
- `SettingsView.axaml` (creata con propria `SettingsViewModel` nel costruttore,
  come le altre tab). Layout a card (`Border.card`), colori solo via
  `{DynamicResource Brush.*}`:
  - Sezione **Copia**: toggle "Parallelismo automatico"; NumericUpDown "Thread
    copia" (1-32, abilitato solo se toggle OFF); slider/NumericUpDown "Buffer
    size" (256KB-16MB, label human-readable); toggle "Verifica checksum dopo
    copia"
  - Sezione **Aspetto**: selezione tema Sistema/Chiaro/Scuro
- `SettingsViewModel` (ReactiveObject): proprietà passthrough su
  `AppSettingsStore.Current`; ogni setter aggiorna lo stato e triggera
  `SaveAsync` fire-and-forget (log su errore, non blocca UI); cambio tema
  applica subito `Application.Current!.RequestedThemeVariant`

## Error handling

- `AppSettingsStore.LoadAsync`/`SaveAsync` falliscono silenziosamente (log) →
  default o stato precedente, mai eccezione che blocca l'avvio o la UI
- `DiskTypeService` non lancia mai: qualunque fallimento (permessi, path
  inesistente, parsing inatteso) → `Unknown`

## Test plan (xunit, `FileExplorer.Tests`)

- `AppSettingsStoreTests.cs` — load/save/default/file corrotto (mirror di
  `ProfileStoreTests.cs`)
- `DiskTypeServiceTests.cs` — solo logica di parsing pura (stringa
  `/proc/mounts` iniettata, valore rotational, output diskutil/WMI simulato),
  non chiamate OS reali dirette
- `CopyParallelismResolverTests.cs` — auto HDD→1, auto SSD/Unknown→N, manuale→
  valore settings, entrambi i dischi controllati (uno HDD basta per forzare 1)
- `SettingsViewModelTests.cs` — proprietà passthrough triggerano save; toggle
  auto disabilita/abilita lo slider thread
