# Remote Browser (FTP/SFTP) — Design

Data: 2026-08-15
Stato: approvato in brainstorming, in attesa di piano di implementazione

## Obiettivo

Nuova tab "Server remoto" nell'app FileExplorer per connettersi a server FTP/FTPS/SFTP,
navigare le directory remote, selezionare uno o più file e scaricarli in una cartella
locale scelta, oppure scaricare l'intera directory aperta. I download supportano filtri
(nome, dimensione, data, solo mancanti, ricorsione) e un check di esistenza dei file
già presenti su disco.

## Decisioni chiave (dal brainstorming)

- Profili di connessione salvati e riutilizzabili tra sessioni.
- Password nel **keyring del sistema operativo** (GNOME Keyring/KWallet, Windows
  Credential Manager, macOS Keychain), mai su file. I metadati profilo
  (host/porta/utente/protocollo) in un piccolo JSON in AppData, senza segreti.
- Check "già presente" = stesso **nome + dimensione + data modifica**.
- File già presenti **saltati automaticamente** durante il download; report finale
  scaricati/saltati/falliti. Toggle opzionale "sovrascrivi sempre" nei controlli.
- "Scarica directory aperta": checkbox in UI **"includi sottocartelle"** (ricorsione a scelta).
- Shell: **TabControl a 2 tab** — "Copia" (CopyPairsView esistente) e "Server remoto".
  FileBrowserView (stub) resta fuori.
- Architettura client: **approccio A** — interfaccia unificata `IRemoteFileClient` con
  implementazioni FluentFTP (FTP/FTPS) e SSH.NET (SFTP).

## Dipendenze NuGet nuove

- `FluentFTP` — FTP e FTPS
- `SSH.NET` — SFTP
- Libreria keyring cross-platform (candidata: **KeySharp**; da verificare a inizio
  implementazione — se inadatta, scegliere equivalente che copra Credential
  Manager/Keychain/libsecret). Fallback runtime definito in Sicurezza.

## Modelli (`FileExplorer/Models/`)

- `RemoteProtocol` (enum): `Ftp`, `Ftps`, `Sftp`.
- `ConnectionProfile`: `Id` (Guid), `Name`, `Host`, `Port`, `Username`, `Protocol`,
  `LastDestinationFolder`, `AcceptedHostKeyFingerprint` (solo SFTP). Nessuna password.
- `RemoteItem`: `Name`, `FullPath`, `IsDirectory`, `Size`, `Modified`.
- `RemoteListingResult`: `Items` + `RemoteError?` (stesso pattern di `DirectoryListingResult`).
- `DownloadFilter`: `NamePattern` (wildcard, separatore `;`, es. `*.jpg;report*`),
  `MinSize?`, `MaxSize?`, `ModifiedAfter?`, `ModifiedBefore?`, `OnlyMissing` (bool),
  `Recursive` (bool).
- `LocalFileStatus` (enum): `Missing`, `Present` (nome+dimensione+data uguali),
  `Different` (esiste ma dimensione o data diverse).
- `RemoteError` (kinds): `AuthFailed`, `HostUnreachable`, `Timeout`, `PermissionDenied`,
  `NotFound`, `TransferFailed`.
- `DownloadReport`: elenchi scaricati/saltati/falliti (con motivo per i falliti).

## Servizi (`FileExplorer/Services/`)

- `IRemoteFileClient` (`IAsyncDisposable`, istanziabile — deviazione motivata dai servizi
  statici esistenti perché mantiene stato di connessione):
  - `ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)`
  - `ListDirectoryAsync(string path, CancellationToken ct)` → `RemoteListingResult`
  - `ListRecursiveAsync(string path, CancellationToken ct)`
  - `DownloadFileAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken ct)`
- `FtpRemoteClient` (FluentFTP, copre `Ftp` e `Ftps`), `SftpRemoteClient` (SSH.NET).
- `RemoteClientFactory`: `ConnectionProfile` → client corretto.
- `ProfileStore` (statico): load/save dei profili in JSON sotto AppData.
- `CredentialService`: `GetPasswordAsync(profileId)` / `SetPasswordAsync` /
  `DeletePasswordAsync` sul keyring OS; espone `IsKeyringAvailable`.
- `DownloadService`: orchestrazione download — applica `DownloadFilter`, calcola
  `LocalFileStatus` rispetto alla cartella destinazione, salta i `Present` (salvo
  "sovrascrivi sempre"), scarica in sequenza con progress, ricrea le sottocartelle se
  `Recursive`, ritorna `DownloadReport`.

Regole trasversali: tutto async con `CancellationToken`, nessun I/O sul thread UI
(stesse regole della PR #2 sui percorsi di rete).

## ViewModel e UI

- `MainWindow.axaml` → `TabControl` con tab "Copia" (`CopyPairsView`) e "Server remoto"
  (`RemoteBrowserView`). Stili tab in `Styles/Controls.axaml`; colori solo via
  `{DynamicResource Brush.*}`; icone Projektanker `fa-*`.
- `RemoteBrowserViewModel` (creato dalla view nel costruttore, come le altre tab):
  - Connessione: `Profiles`, `SelectedProfile`, `IsConnected`, `IsBusy`, `StatusMessage`.
  - Navigazione: `CurrentPath`, `Items` (RemoteItem + `LocalFileStatus`), doppio click
    cartella per entrare, pulsante "su", refresh.
  - Filtri: proprietà bound a `DownloadFilter`; applicati live alla lista visualizzata
    e passati a `DownloadService` per i download.
  - Download: `DestinationFolder` (persistita per profilo in `LastDestinationFolder`),
    `DownloadSelectedCommand` (multi-selezione), `DownloadCurrentDirectoryCommand`,
    `CancelCommand`; progress file corrente + n/totale + barra.
  - Profili: `ProfileEditorViewModel` in dialog (nuovo/modifica/elimina); password
    chiesta al connect se assente dal keyring.
- `RemoteBrowserView.axaml`, layout verticale:
  1. Barra connessione: ComboBox profili, connetti/disconnetti/gestisci profili,
     badge stato (`Border.badge.*`).
  2. Barra percorso: path corrente, su, refresh.
  3. Pannello filtri (Expander): pattern nome, size min/max, date, toggle
     "solo mancanti" e "includi sottocartelle", toggle "sovrascrivi sempre".
  4. Lista file: DataGrid multi-select — icona, nome, dimensione, data, colonna
     "Su disco" con badge Mancante/Presente/Diverso (vs `DestinationFolder`).
  5. Barra download: destinazione + sfoglia (riusa `SelectPathDialog`),
     "Scarica selezionati", "Scarica directory", progress + annulla.

## Gestione errori

- Mai eccezioni silenziate: errori classificati in `RemoteError` e mostrati in un
  banner stile `SelectPathDialog`.
- Timeout connessione ~15 s.
- Nel batch di download un file fallito non interrompe gli altri: finisce nel
  `DownloadReport` con il motivo.
- Annullamento via `CancellationToken`; il file parziale in scrittura viene rimosso.

## Sicurezza

- Password mai su disco né nei log; vive esclusivamente nel keyring OS.
- Se il keyring non è disponibile (es. Linux senza GNOME Keyring/KWallet), l'app chiede
  la password a ogni connessione e avvisa che non può salvarla. Nessun fallback su file.
- SFTP: alla prima connessione mostra la fingerprint della host key e chiede conferma;
  la fingerprint accettata è salvata nel profilo e verificata alle connessioni
  successive (protezione MITM). Fingerprint cambiata → connessione rifiutata con
  avviso esplicito e possibilità di ri-accettare consapevolmente.
- FTP semplice trasmette le credenziali in chiaro: l'editor profili mostra un avviso
  e suggerisce FTPS se disponibile.

## Testing (`FileExplorer.Tests`, xunit, TDD)

- `DownloadFilter`: matching wildcard, range dimensione/data, combinazioni.
- Calcolo `LocalFileStatus`: Missing/Present/Different con file temporanei reali.
- `DownloadService` con `FakeRemoteClient` in-memory: skip dei presenti, ricorsione e
  ricreazione struttura, report, cancellazione, fallimento di un singolo file.
- `ProfileStore`: roundtrip JSON, file assente al primo avvio.
- `RemoteBrowserViewModel` con fake client: connect/list/navigazione, stato busy, errori.
- Fuori dai test automatici (verifica manuale documentata nel piano): client reali
  FluentFTP/SSH.NET contro un server vero, integrazione keyring per OS.

## Consegna

Branch `feature/remote-browser`, pull request verso `main` (mai commit diretti su main).
