# Android porting — Fase 4 (design spec)

## Contesto

Fase 1-3 del porting Android (`ISingleViewApplicationLifetime`, `MainView` responsive,
seam `IFileSystemAccessor`, watch-folder come foreground service scaffoldato,
`OverlayDialogHost`/`DialogPresenter`) sono complete a livello di codice ma non
verificate su device reale, e restano lacune elencate nel punto 26 di `IDEE.md`.
Nessuna PR va aperta finché tutte le fasi pianificate non sono complete (istruzione
esplicita utente): questo spec copre l'ultima fase di codice prima della verifica
manuale finale e della PR unica.

## Scope

1. Accesso reale a cartelle fuori sandbox (SAF) tramite picker di sistema.
2. Chiusura nota IDEE su metadata di list/enumerate (verifica, non implementazione).
3. Abilitazione reale di `IsWatchFolderSupported` risolvendo i concern noti.
4. Copertura dei 3 dialoghi desktop-only rimasti fuori scope Fase 3.
5. Verifica manuale finale end-to-end (fuori da questo spec di codice, ma è il
   criterio di uscita della fase).

Fuori scope: accesso a provider SAF non risolvibili a path reale (cloud-backed,
SD non standard) — vanno in errore esplicito, non supportati.

## 1. SAF reale — picker di sistema + risoluzione path

**Problema**: `IFileSystemAccessor` (seam Fase 2/3) astrae le operazioni file, ma
copy/checksum/compare/dedup (`FileCopyService`, `ChecksumService`,
`DirectoryComparisonService`, `DuplicateFinderService`, `FileByteCompareService`)
usano `System.IO` diretto, bypassando il seam. Su Android, cartelle fuori dalla
sandbox dell'app richiedono permesso esplicito (SAF); un URI `content://` non è
apribile da `System.IO`.

**Approccio scelto**: nessuna nuova implementazione di `IFileSystemAccessor`. Il
picker di sistema (`Intent.ActionOpenDocumentTree`) restituisce un `content://`
URI; si richiede `ContentResolver.TakePersistableUriPermission` (sopravvive a
riavvii/kill del processo) e si risolve l'URI a un path filesystem reale via
`DocumentsContract` (funziona per storage primario e SD standard — path del tipo
`/storage/emulated/0/...` o `/storage/<uuid>/...`). Il path risolto rientra nel
flusso esistente: stessa `SelectPathDialogViewModel`, stesso
`DefaultFileSystemAccessor`, nessuna modifica a copy/checksum/compare/dedup.

Se la risoluzione fallisce (provider non standard, es. Google Drive montato,
SD non riconosciuta): errore esplicito e leggibile ("cartella non supportata su
questo dispositivo"), nessun fallback silenzioso, nessun crash.

**UI**: nuovo pulsante "Scegli con selettore di sistema" in
`SelectPathDialogContent`, visibile solo su Android (desktop mantiene solo il
browser custom esistente, che resta invariato). Il pulsante avvia il picker
nativo via un seam statico (pattern analogo a `App.StartBackgroundWatchHost`
usato per il foreground service in Fase 3), l'esito (path risolto o errore)
torna al ViewModel come se l'utente avesse digitato/selezionato il path a mano.

**Persistenza permesso**: le concessioni SAF vanno ri-prese a ogni avvio app per
le cartelle già usate (regole watch-folder, coppie di copia salvate) — altrimenti
il permesso "vive" ma va ri-confermato se l'utente non ripassa dal picker.
`ContentResolver.PersistedUriPermissions` va controllato all'avvio e le entry
scadute/non più valide vanno segnalate (non silenziosamente ignorate) dove usate
(watch rule disabilitata con motivo visibile, non sparita).

## 2. List/metadata — verifica, non implementazione

`FileSystemService.ListDirectoryAsync`/`ListFilesRecursiveAsync` chiamano già
`Accessor.EnumerateEntries`/`EnumerateEntriesRecursive`, estesi in Fase 3 con
size (`SizeBytes`) e mtime (`LastModified`) su `FileSystemItem`. La nota in
`IDEE.md` ("richiedono metadata non esposti dal seam") è superata: nessun lavoro
di codice qui. Aggiornare solo il testo del punto 26 in `IDEE.md` a fine fase.

## 3. Watch-folder — abilitazione reale

In `WatchFolderForegroundService` (Fase 3), risolvere i 5 concern noti prima di
flippare `FileSystemService`/config `IsWatchFolderSupported` a `true`:

- **`StartForeground` senza try/catch**: può lanciare (`ForegroundServiceDidNotStartInTimeException`
  o rifiuto di sistema su restrizioni batteria) — va in try/catch con log e stop
  pulito del service, non crash del processo.
- **Nessun percorso di stop esplicito oltre `OnDestroy`**: serve un comando stop
  raggiungibile dalla UI (es. l'utente disabilita l'ultima regola attiva) che
  fermi il service, non solo l'`Activity` che lo distrugge.
- **Conflitto Start/Stop concorrente**: una volta che la UI può fermare il
  service e il sistema può ri-avviarlo (sticky), serve una guardia (lock/flag)
  contro race tra i due path.
- **Leak del seam su `MainActivity` invece di `ApplicationContext`**: il seam
  statico va agganciato al contesto applicazione, non all'Activity corrente,
  per evitare di trattenere un riferimento a un'Activity distrutta.
- **Notifica sempre in EN su restart sticky**: comportamento noto e accettato
  (fallback quando `LocalizationService` non è ancora inizializzato) — non è un
  bug da fixare, resta com'è.

Dopo i fix: `IsWatchFolderSupported` passa a `true`. Verifica reale
(notifica visibile, sopravvivenza a Doze, sync effettiva, limite 6h/24h FGS
dataSync) rientra nella verifica manuale finale (fuori da questo spec).

## 4. Dialoghi desktop-only

3 punti individuati in Fase 3 ma fuori scope: upload cartella remota, editor
profili, editor temi. Stesso pattern già usato per Browse/rename/conferma/
credenziali: doppio percorso Window-su-desktop/overlay-su-Android tramite
`OverlayDialogHost`/`DialogPresenter`, zero regressione desktop attesa (verificare
comunque, come nei fix precedenti).

## 5. Verifica manuale finale (criterio di uscita, non di codice)

Layout responsive, FTP/SFTP, tema custom, foreground service (notifica, Doze,
sync reale), tutti i dialoghi (inclusi i 3 nuovi), tasto Back hardware
(routing `MainActivity.OnBackPressed`, non ancora fatto — mappato solo
Escape/Backspace). Su device reale se disponibile nella sessione di verifica,
altrimenti emulatore con nota esplicita del gap. Solo dopo esito positivo si
apre la PR unica per l'intero porting Android.

## Testing

- Unit test per la risoluzione URI→path (casi: storage primario, SD standard,
  provider non risolvibile → errore).
- Unit test per i fix di `WatchFolderForegroundService` dove isolabili senza
  Android runtime (es. guardia Start/Stop se estraibile in logica pura).
- Nessun nuovo test per i 3 dialoghi oltre a quelli già esistenti per gli altri
  dialoghi overlay (pattern consolidato).
- Verifica manuale (sezione 5) copre ciò che gli unit test su desktop non
  possono: comportamento reale su Android runtime.
