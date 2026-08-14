# UI Restyle — Design

Data: 2026-08-14
Stato: approvato dall'utente (dialogo di brainstorming con mockup nel visual companion)

## Obiettivo

Restyle visivo completo dell'app (Avalonia 11, .NET 8, MVVM/ReactiveUI) con look colorato e moderno. **Le funzionalità non cambiano**: stessa logica di copia, verifica checksum, navigazione del dialogo.

## Decisioni prese

| Tema | Decisione |
|---|---|
| Ambito | Restyle + riorganizzazione layout delle viste; funzionalità invariate |
| Tema | Segue il sistema (`RequestedThemeVariant=Default`); chiaro e scuro entrambi curati |
| Stile | Colorato con personalità; palette accent corallo `#FF5E62` → arancio `#FF9446` (gradiente) |
| Dipendenze | Solo un icon pack (`Projektanker.Icons.Avalonia` + FontAwesome); tutto il resto stile custom nel progetto |
| Tab "Esplora" | Nascosta dalla finestra (view/viewmodel restano nel codice, non referenziati) |
| Layout principale | "Header gradiente + card" (mockup A) |
| Dialogo percorso | "Barra percorso rifinita" (mockup B): interazione identica all'attuale |
| Titlebar | Nativa: niente decorazioni custom (app usata su Linux, WM inaffidabili con `ExtendClientArea`) |

## 1. Fondamenta stile

Nuova cartella `FileExplorer/Styles/`, inclusa da `App.axaml` dopo `FluentTheme` (che resta come base):

- **`Palette.axaml`** — `ResourceDictionary` con `ThemeDictionaries` (`Light`/`Dark`). Risorse con chiavi stabili (`Brush.Accent`, `Brush.AccentGradient`, `Brush.Surface`, `Brush.Card`, `Brush.TextPrimary`, `Brush.TextMuted`, `Brush.Success`, `Brush.Error`, `Brush.Warning`, `Brush.FieldBackground`, ecc.).
  - Chiaro: sfondo bianco caldo `#FAF9F7`, card `#FFFFFF`, testo `#2B2420` / muted `#8A7F78`.
  - Scuro: sfondo `#1E1B1A`, card `#2A2624`, testo `#F2ECE7` / muted `#A79A91`.
  - I valori sono i default di partenza: in implementazione si possono ritoccare solo per contrasto (WCAG AA sul testo), senza cambiare carattere della palette.
  - Accent identico nelle due varianti; verificare contrasto testo/badge in entrambe.
- **`Controls.axaml`** — stili basati su classi:
  - `Button.primary`: gradiente accent, testo bianco, radius 8, hover più luminoso.
  - `Button.secondary`: outline neutro.
  - `Button.icon`: quadrato compatto, solo icona.
  - `TextBox`: radius 6, sfondo `Brush.FieldBackground`; variante `.error` (bordo/sfondo rosso da palette).
  - `ProgressBar`: sottile (6px), riempimento col gradiente accent.
  - `Border.card`: radius 12, sfondo `Brush.Card`, ombra leggera (`BoxShadow`).
  - Badge di stato: `Border.badge` + classi `success` / `warning` / `error` / `progress` / `neutral` (pillola con sfondo tinto e testo corrispondente). Mapping: Ready→`neutral`, Copying→`progress`, Success→`success`, Warning→`warning`, Error→`error`, Cancelled→`neutral`.
  - `DataGrid`: righe con hover, header sobri.

Icone: pacchetti NuGet `Projektanker.Icons.Avalonia` e `Projektanker.Icons.Avalonia.FontAwesome`; provider registrato in `Program.cs` (progetto Desktop) prima di `BuildAvaloniaApp`.

Finestra principale: 900×640 di default, minimo 640×480.

## 2. MainWindow

- Rimossi il `Menu` (voci `Open`/`Exit` senza handler: morte) e il `TabControl`.
- La finestra diventa una shell: contiene solo `CopyPairsView`.
- L'header con gradiente (icona app, titolo "File Explorer", bottone "＋ Aggiungi coppia" bianco semi-trasparente) sta **dentro `CopyPairsView`**, così `AddPairCommand` resta sul suo ViewModel senza rewiring.
- `FileBrowserView`/`FileBrowserViewModel` non più referenziati da alcuna vista; restano nel progetto per sviluppi futuri.

## 3. CopyPairsView (lista card)

Struttura: `DockPanel` → header gradiente in alto; sotto `ScrollViewer` + `ItemsControl`.

Ogni coppia è una card (`Border.card`):

1. Riga sorgente: icona (file/cartella), percorso in `TextBox` readonly stile pill, `Button.icon` "sfoglia".
2. Riga destinazione: idem.
3. Riga stato: `ProgressBar` gradiente + badge di stato colorato + "Avvia" (`Button.primary`) / "Annulla" (`Button.secondary`), abilitazioni come oggi (`CanStart` / `IsCopying`).
4. `Expander` "Mostra file da elaborare" ristilizzato; `DataGrid` con colonne definite — icona, nome, dimensione, ultima modifica — al posto di `AutoGenerateColumns` (che oggi mostra anche `FullPath` e `CheckSum` grezzi).

**Stato di presentazione**: nuova proprietà `StateKind` su `FolderFilePairViewModel`, enum `CopyStateKind { Ready, Copying, Success, Warning, Error, Cancelled }` (in `Models/`). Impostata da `CopyPairsViewModel` negli stessi punti in cui oggi si imposta `Status` (Warning = "checksum non corrisponde"). Nessun cambiamento di flusso: solo dato per pilotare classi/colori del badge.

**Empty state**: con `PathPairs` vuota, pannello centrale con icona grande, testo tipo "Nessuna coppia di copia" e bottone primario "Aggiungi la prima coppia" (stesso `AddPairCommand`).

## 4. SelectPathDialog

Interazione identica all'attuale: campo percorso editabile, Invio = "Vai", bottone indietro, doppio click (cartella = entra, file = seleziona), "Seleziona" = conferma elemento o cartella corrente.

Restyle:
- Barra in alto: `Button.icon` "←", `TextBox` arrotondato, "Vai".
- Lista: `DataGrid` con icone vettoriali (colonna template su `IsDirectory`) al posto delle emoji, righe con hover, selezione evidenziata con accent.
- Footer in basso a destra: **"Annulla"** (`Button.secondary`, nuovo: `Close(null)`, oggi esiste solo la X della finestra) e **"Seleziona"** (`Button.primary`, spostato dalla barra).
- Percorso inesistente: la classe `error` sul `TextBox` sostituisce l'attuale `Background = Brushes.Red/White` hardcoded nel code-behind (oggi rotto in tema scuro).

La proprietà emoji `FileSystemItem.Icon` resta (usata da `FileBrowserView` non referenziata); il dialogo non la usa più.

## 5. File coinvolti

| File | Intervento |
|---|---|
| `FileExplorer/Styles/Palette.axaml` | nuovo |
| `FileExplorer/Styles/Controls.axaml` | nuovo |
| `FileExplorer/App.axaml` | include stili |
| `FileExplorer/FileExplorer.csproj` | pacchetti icone |
| `FileExplorer.Desktop/Program.cs` | registrazione icon provider |
| `FileExplorer/Views/MainWindow.axaml` | shell senza menu/tab, dimensioni |
| `FileExplorer/Views/CopyPairsView.axaml` | header + card + empty state |
| `FileExplorer/Views/SelectPathDialog.axaml(.cs)` | restyle + footer + classe error |
| `FileExplorer/ViewModels/FolderFilePairViewModel.cs` | proprietà `StateKind` |
| `FileExplorer/ViewModels/CopyPairsViewModel.cs` | set di `StateKind` accanto a `Status` |
| `FileExplorer/Models/CopyStateKind.cs` | nuovo enum |
| `CLAUDE.md` | nota su `Styles/` e convenzioni classi |

## 6. Non-obiettivi

- Nessuna nuova funzionalità (niente tab Esplora funzionante, niente "Avvia tutte", niente breadcrumb).
- Nessun cambiamento a servizi, logica di copia, checksum, validazioni.
- Nessuna decorazione finestra custom.
- Nessun test automatico nuovo.

## 7. Verifica

- `dotnet build FileExplorer.sln`: 0 errori, 0 warning.
- Prova manuale (`dotnet run --project FileExplorer.Desktop`) in tema chiaro e scuro: lista card, avvio/annullamento copia, badge, dialogo con percorso valido e non valido, empty state.
