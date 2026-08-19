# Temi personalizzabili — Design

**Data:** 2026-08-19 · **Stato:** approvato (approccio A: ThemeVariant custom + store JSON)

## Obiettivo

Permettere agli utenti di creare, salvare, modificare, importare/esportare temi colore completi
(ogni brush della palette), mantenendo i temi built-in (Chiaro/Scuro/Sistema) non modificabili.

## Requisiti

1. Editor palette completo: ogni colore del tema è modificabile (superficie, card, testo, badge,
   accent + gradiente, treemap, sparkline).
2. Temi nominati salvabili: l'utente crea combinazioni proprie partendo da un tema esistente
   (duplica → modifica).
3. Temi built-in Chiaro e Scuro non modificabili né eliminabili; "Sistema/Chiaro/Scuro" restano
   come oggi quando nessun tema custom è attivo.
4. Import/export dei temi custom come file JSON.
5. Anteprima live durante la modifica; nessun riavvio richiesto.
6. File tema corrotto o mancante → fallback pulito al tema base, mai crash.

## Architettura (approccio A)

- **`Models/ColorTheme.cs`** — tema nominato: `Id` (GUID), `Name`, `BaseVariant` ("Light"|"Dark"),
  `Colors: Dictionary<string,string>` (chiave logica → hex `#RRGGBB`/`#AARRGGBB`).
  `IsBuiltIn` solo runtime (non serializzato).
- **`Models/ThemeColorKeys.cs`** — elenco canonico delle chiavi logiche (mirror di Palette.axaml,
  più `AccentGradientStart`/`AccentGradientEnd` che compongono `Brush.AccentGradient`).
- **`Services/BuiltInThemes.cs`** — "Chiaro" e "Scuro" generati in codice con i valori attuali di
  `Styles/Palette.axaml`; fungono anche da fallback per chiavi mancanti.
- **`Services/ThemeStore.cs`** — pattern `AppSettingsStore`: JSON in
  `AppData/FileExplorer/themes/<id>.json`, un file per tema, scrittura atomica, load tollerante,
  sanitizzazione (hex invalidi/chiavi mancanti → fallback dal built-in del `BaseVariant`,
  chiavi sconosciute scartate). Import assegna sempre un nuovo Id.
- **`Services/ThemeService.cs`** — applica un tema a runtime: costruisce un `ResourceDictionary`
  di brush dal `ColorTheme`, lo registra in `Application.Resources.ThemeDictionaries` con chiave
  `new ThemeVariant("Custom", baseVariant)` e imposta `RequestedThemeVariant`. Le chiavi non
  coperte risalgono per ereditarietà alla variante base in Palette.axaml. `UpdateColor` muta i
  brush attivi per l'anteprima live dell'editor. `Revert` rimuove la variante custom.
- **`Styles/Palette.axaml`** — Accent/AccentGradient/OnAccent spostati dentro le
  ThemeDictionaries (duplicati in Light e Dark) così anche l'accent è per-tema.
- **`AppSettings.CustomThemeId`** (nullable) — se valorizzato e il tema esiste, all'avvio viene
  applicato; altrimenti vale `ThemeVariant` come oggi.
- **UI** — card "Temi" in `SettingsView` (lista temi, Applica, Nuovo da…, Modifica, Elimina,
  Esporta, Importa); `ThemeEditorWindow` + `ThemeEditorViewModel` (pattern
  `ProfileEditorWindow`) con `ColorPicker` (pacchetto `Avalonia.Controls.ColorPicker`) per ogni
  chiave, raggruppate; anteprima live; Salva/Annulla (Annulla ripristina lo stato precedente).

## Fuori scope (YAGNI)

Editor gradiente multi-stop, temi per-pannello, marketplace/condivisione online, anteprima
miniaturizzata dei temi nella lista.

## Testing

xunit su: completezza chiavi dei built-in, round-trip store, sanitizzazione, import/export,
`BuildDictionary` (tutte le chiavi brush presenti, gradiente a 2 stop), logica
`SettingsViewModel`/`ThemeEditorViewModel` (selezione, duplica, elimina, salva/annulla).
Parti dipendenti da `Application.Current` protette da null-guard (già pattern del repo).
