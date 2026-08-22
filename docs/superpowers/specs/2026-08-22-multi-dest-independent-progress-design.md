# Copia multi-destinazione con avanzamento indipendente per destinazione

Idea 25 in `IDEE.md`.

## Problema

`CopyFileToManyAsync` legge la sorgente a blocchi e scrive ogni blocco su tutte
le destinazioni con un `Task.WhenAll` sincrono per chunk. Una destinazione
lenta (rete, disco occupato) blocca il ciclo di lettura e quindi rallenta
anche le destinazioni veloci (es. SSD locale). Stesso problema, in scala,
in `CopyDirectoryToManyAsync` (che chiama `CopyFileToManyAsync` per ogni file).

## Decisioni prese in brainstorming

- **Approccio**: lettura singola della sorgente + writer disaccoppiati per
  destinazione via `Channel<byte[]>` bounded (capacity fissa, non
  configurabile in UI). Scartato l'approccio "N letture indipendenti della
  sorgente" (una per destinazione, riuso di `CopyFileAsync`): su disco
  meccanico sorgente causerebbe seek thrashing con più destinazioni
  concorrenti. Un solo approccio (niente ibrido per tipo disco) per non
  duplicare codice/test.
- **Errore su una destinazione**: le altre destinazioni proseguono
  indipendentemente. La destinazione fallita viene segnalata come errore nel
  report/stato, non abortisce le altre. Se **tutte** le destinazioni
  falliscono, l'eccezione si propaga (comportamento coerente con oggi).
- **Parallelismo multi-file**: invariato. `CopyDirectoryToManyAsync` continua
  a copiare più file in parallelo con lo stesso `semaphore`/
  `maxDegreeOfParallelism` risolto dal tipo disco. La modifica riguarda solo
  la copia del singolo file verso le sue N destinazioni.
- **UI progresso**: barra + velocità **per destinazione** nel widget "in
  copia adesso" (non un'unica barra aggregata al minimo).

## Design

### 1. Modello dati (`FolderFilePairViewModel`)

- Nuovo `DestinationProgressViewModel`: `Path`, `Progress` (0..1),
  `SpeedText`, `StateKind` (Copying/Success/Warning/Error), `ErrorMessage?`,
  `CopyingFiles: ObservableCollection<FileSystemItem>`.
- Nuova `ObservableCollection<DestinationProgressViewModel> DestinationsProgress`,
  popolata da `AllDestinations` all'avvio della copia.
- La `CopyingFiles` condivisa esistente (aggiunta in PR #31) viene rimossa,
  sostituita da quella per-destinazione dentro `DestinationProgressViewModel`.
- `pair.Progress`/`pair.SpeedText`/`pair.StateKind` restano come aggregato
  di riepilogo (vedi §3), usati dalla card compatta; `DestinationsProgress`
  alimenta il widget espanso.

### 2. Copy engine (`FileCopyService`)

`CopyFileToManyAsync`:
- Un task **reader**: legge la sorgente a blocchi; per ogni blocco, per ogni
  destinazione non ancora faulted, `WriteAsync` una copia del blocco sul
  `Channel<byte[]>` bounded di quella destinazione (capacity costante, es. 8
  → backpressure limita a ~8 blocchi/destinazione in coda).
- N task **writer** (uno per destinazione): dequeue dal proprio channel,
  scrive sul file, invoca `onBytesCopied(destinationPath, deltaBytes)`. In
  caso di eccezione: cattura, marca la destinazione come faulted con
  l'errore, completa il proprio channel (il reader smette di scriverci,
  smaltisce il resto senza quella destinazione).
- Fine: `Task.WhenAll` di reader + writer. Se tutte le destinazioni sono
  faulted, l'eccezione (la prima) si ripropaga al chiamante. Altrimenti il
  metodo ritorna normalmente con la lista di destinazioni fallite/riuscite
  (nuovo tipo di ritorno, vedi firma sotto).
- `SetLastWriteTimeUtc` post-copia: solo sulle destinazioni riuscite.

Nuova firma (indicativa):
```csharp
public static async Task<CopyToManyResult> CopyFileToManyAsync(
    string sourcePath,
    IReadOnlyList<string> destinationPaths,
    Action<string destinationPath, long deltaBytes>? onBytesCopied,
    CancellationToken ct,
    int bufferSize = DefaultBufferSize)

public readonly record struct CopyToManyResult(
    IReadOnlyList<string> SucceededDestinations,
    IReadOnlyDictionary<string, Exception> FailedDestinations);
```

`CopyDirectoryToManyAsync`: stesso schema propagato per file. `onFileStarted`
/`onFileCompleted` diventano `Action<string destinationPath, string
sourceFile>`. Lo skip-unchanged resta valutato per singola coppia
sorgente/destinazione (una destinazione può skippare, un'altra no).

### 3. Progresso/velocità/verifica (`CopyPairsViewModel`)

- `DirectoryCopyProgressPublisher` (e l'equivalente inline in
  `CopySingleFileAsync`) diventa **uno per destinazione**: proprio
  `SpeedTracker`, `MonotonicProgressGate`, `UiProgressThrottle`. Dizionario
  `destinationPath → publisher` costruito all'avvio della copia.
- Ogni publisher scrive sul `DestinationProgressViewModel` corrispondente
  (Progress, SpeedText, CopyingFiles via onFileStarted/onFileCompleted).
- Aggregato pair-level: `Progress = min` delle progress per-destinazione
  (riflette la più lenta, coerente con "quanto manca perché tutto sia
  completato"). `SpeedText` pair-level: somma delle velocità correnti
  per-destinazione (throughput totale in uscita).
- Verifica checksum: il loop esistente per-destinazione resta; l'esito
  (match/mismatch/missing) viene scritto su
  `DestinationProgressViewModel.StateKind`/`ErrorMessage` invece che solo
  aggregato su `pair.IsVerified`.
- `pair.StateKind` finale = priorità Error > Warning > Success sulle
  `DestinationsProgress` (una destinazione Error rende il pair Error anche
  se le altre sono Success).

### 4. UI (`CopyPairsView.axaml`, widget "in copia adesso")

- `ItemsControl` binda `DestinationsProgress` invece di `AllDestinations` +
  `CopyingFiles` condivisa.
- Per ogni destinazione: intestazione cartella, barra progresso propria,
  `SpeedText` proprio, lista file in copia (WrapPanel/badge, invariata),
  badge errore (icona + tooltip `ErrorMessage`) se `StateKind == Error`
  (riuso stili `Border.badge.*`/brush esistenti, nessun colore hardcoded).
- Barra di riepilogo della card resta unica, alimentata dall'aggregato
  pair-level (§3).

### 5. Errori e test

- Cancellazione (`ct`): propagata a reader e writer come oggi, nessun
  cambiamento di comportamento.
- Test da aggiungere/aggiornare:
  - `FileCopyServiceTests`: una destinazione fallisce (es. path non
    scrivibile) → le altre completano correttamente (contenuto/bytes
    verificati), `CopyToManyResult` riflette successo/fallimento per
    destinazione; tutte falliscono → eccezione propagata.
  - `CopyPairsViewModelTests`: progress/stato per-destinazione, aggregazione
    pair-level (min progress, somma velocità, priorità stato).
- Capacità del channel: costante fissa (es. 8), non esposta in
  UI/impostazioni (YAGNI).

## Fuori scope

- Configurabilità della capacità del buffer per destinazione.
- Retry automatico su destinazione fallita.
- Persistenza/report a fine batch delle destinazioni fallite oltre allo
  stato visibile nel widget/card (nessun log su file, nessun export).
