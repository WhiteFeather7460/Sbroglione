# Android porting — Fase 4 (design spec)

## Contesto

Fase 1-3 del porting Android (`ISingleViewApplicationLifetime`, `MainView` responsive,
seam `IFileSystemAccessor`, watch-folder come foreground service scaffoldato,
`OverlayDialogHost`/`DialogPresenter`) sono complete a livello di codice ma non
verificate su device reale. Nessuna PR va aperta finché tutte le fasi pianificate
non sono complete (istruzione esplicita utente): questo spec copre l'ultima fase
di codice prima della verifica manuale finale e della PR unica.

## Correzioni rispetto alla nota IDEE.md punto 26 (verificate leggendo il codice)

La nota IDEE elenca 4 voci come "da fare" per Fase 4. Verificando il codice
attuale, solo una è reale lavoro:

1. **SAF reale — non necessario.** L'app usa già `MANAGE_EXTERNAL_STORAGE`
   (all-files-access, `StoragePermission.cs`): una volta concesso dall'utente
   (pagina di sistema, già cablata in `RequestStorageAccess`), ogni path
   filesystem è accessibile via `System.IO` diretto, nessun `content://` URI
   coinvolto. Un picker SAF a tree sarebbe un secondo modello di accesso
   ridondante col permesso ampio già scelto. **Droppato dallo scope** (decisione
   utente, 2026-09-04) — resta un'opzione per un eventuale futuro rilascio Play
   Store, fuori da questa fase.
2. **Metadata di list/enumerate — già chiuso.** `FileSystemService.ListDirectoryAsync`/
   `ListFilesRecursiveAsync` chiamano già `Accessor.EnumerateEntries`/
   `EnumerateEntriesRecursive`, estesi in Fase 3 con `SizeBytes`/`LastModified`
   su `FileSystemItem`. Nessun lavoro di codice.
3. **`IsWatchFolderSupported` — già cablato dinamicamente**, non un flag statico:
   segue `StoragePermission.IsGranted` (`App.axaml.cs:110/116`,
   `MainActivity.cs:35`). Il gap osservato in sessione precedente era il
   permesso non ancora concesso sull'emulatore (azione utente a runtime), non
   codice mancante.
4. **3 dialoghi desktop-only — già chiusi.** `ProfileEditorHelper`,
   `ThemeEditorHelper`, `SelectPathDialogHelper` (usato anche da "Carica
   cartella" in `RemoteBrowserView.axaml.cs:83-88`) sono già cablati su
   `DialogPresenter.ShowAsync` con doppio costruttore Window/overlay. Nessun
   lavoro di codice, solo verifica manuale che si aprano in overlay su Android.

## Scope reale di Fase 4

1. **`WatchFolderForegroundService` — 4 fix di robustezza** prima di considerare
   il foreground service pronto per la verifica manuale finale.
2. **Tasto Back hardware Android** — non instradato (solo Escape/Backspace via
   tastiera), serve routing da `MainActivity.OnBackPressed` a `OverlayDialogHost`/
   navigazione.
3. **Verifica manuale finale end-to-end** (fuori da questo spec di codice, ma è
   il criterio di uscita della fase — vedi sezione dedicata).

Fuori scope: SAF, metadata seam, i 3 dialoghi (vedi correzioni sopra — già fatti
o non necessari).

## 1. `WatchFolderForegroundService` — fix di robustezza

Concern noti dalla Fase 3, in `Sbroglione.Android/WatchFolderForegroundService.cs`:

- **`StartForeground` senza try/catch** (righe 63-66): può lanciare
  (`ForegroundServiceDidNotStartInTimeException` o rifiuto di sistema su
  restrizioni batteria). Va in try/catch: se fallisce, fermare il service
  pulito (`StopSelf()`) invece di lasciare il processo in stato inconsistente
  o farlo crashare.
- **Nessun percorso di stop esplicito oltre `OnDestroy`**: oggi l'unico modo di
  fermare i runner è la distruzione del service da parte del sistema. Serve un
  comando raggiungibile dalla UI quando l'utente disabilita l'ultima regola
  attiva (`WatchFoldersViewModel`, dove oggi si chiama solo
  `App.StartBackgroundWatchHost?.Invoke()` per l'avvio — manca il simmetrico
  stop). Nuovo seam `App.StopBackgroundWatchHost` (stesso pattern di
  `StartBackgroundWatchHost`), registrato in `MainActivity` per chiamare
  `StopService(new Intent(this, typeof(WatchFolderForegroundService)))`.
- **Conflitto Start/Stop concorrente**: con uno stop esplicito raggiungibile
  dalla UI e un riavvio sticky lato sistema, serve una guardia contro race tra
  `OnStartCommand` e `OnDestroy` che corrono su thread diversi. `_runnersStarted`
  passa da `bool` semplice a un accesso `lock`-protetto (il campo non è mai
  avuto necessità di thread-safety finora perché c'era un solo punto di stop).
- **Leak del seam su `MainActivity` invece di `ApplicationContext`**: verificare
  se `App.StartBackgroundWatchHost` (e il nuovo `StopBackgroundWatchHost`)
  catturano `this` (l'Activity) nella closure registrata in
  `MainActivity.CustomizeAppBuilder`. Se sì, il metodo che crea l'`Intent` va
  cambiato per usare `Android.App.Application.Context` invece di `this`, così
  la closure statica non trattiene un riferimento a un'Activity che può essere
  distrutta e ricreata.
- **Notifica sempre in EN su restart sticky**: comportamento noto e accettato
  (fallback quando `LocalizationService` non è ancora inizializzato) — non è un
  bug, resta com'è, nessuna azione.

## 2. Tasto Back hardware Android

`MainActivity` non sovrascrive `OnBackPressed`: il tasto Back di sistema non è
instradato a nulla lato Avalonia (solo Escape/Backspace via tastiera sono
mappati, inutili senza tastiera fisica). Serve:

- Override `OnBackPressed` in `MainActivity` che, se un dialog overlay
  (`OverlayDialogHost`) è aperto, lo chiude (equivalente ad Annulla) invece di
  uscire dall'app o propagare al sistema.
- Se nessun overlay è aperto, comportamento di default Android (torna alla
  Home / esce dall'app) — nessuna navigazione custom da costruire ora, fuori
  scope.

## 3. Verifica manuale finale (criterio di uscita, non di codice)

Layout responsive, FTP/SFTP, tema custom, foreground service (notifica, Doze,
sync reale, limite 6h/24h FGS dataSync), tutti i dialoghi overlay (inclusi i 3
già chiusi via `DialogPresenter`), tasto Back hardware (fix sezione 2). Su
device reale se disponibile nella sessione di verifica, altrimenti emulatore
con nota esplicita del gap. Solo dopo esito positivo si apre la PR unica per
l'intero porting Android.

## Testing

- Unit test per la guardia Start/Stop di `WatchFolderForegroundService` se
  estraibile in logica pura testabile senza Android runtime (es. una piccola
  classe di stato con lock, testata isolatamente); se la logica resta troppo
  legata alle API `Service`/`Intent` per essere isolata, verifica manuale nella
  sezione 3 la copre.
- Nessun unit test automatizzato per `OnBackPressed` (richiede Android runtime):
  copre la verifica manuale.
- Nessun nuovo codice/test per SAF, metadata, o i 3 dialoghi: già chiusi o fuori
  scope (vedi correzioni).
