# UI Restyle "Refined Minimal" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sostituire lo stile "cartoon" (gradiente corallo→arancio, pillole, ombre rosate, beige) con la direzione approvata "C puro / Refined Minimal": accent teal `#0EA5A0`, neutri grigi puri, zero gradienti, card senza bordo con barra-stato laterale, header dei tab piatti.

**Architecture:** Solo styling: valori in `Styles/Palette.axaml` + `Services/BuiltInThemes.cs` (devono restare identici tra loro), classi in `Styles/Controls.axaml`, e ritocchi markup negli header delle view. Nessuna chiave nuova in `ThemeColorKeys`, nessun cambio di logica o ViewModel.

**Tech Stack:** Avalonia 11 (.NET 10), stili class-based, `DynamicResource Brush.*`, xunit.

**Spec:** Design approvato in chat (sessione 2026-08-21): mockup "C puro" in `.superpowers/brainstorm/44771-1787330927/content/a-vs-c.html`.

## Global Constraints

- Branch di lavoro: `ui/refined-minimal` creato da `origin/main`. MAI committare su `main`.
- Nessun colore hardcoded nelle view: sempre `{DynamicResource Brush.*}` (unica eccezione esistente: `Button.onaccent`, che questo piano elimina).
- `Palette.axaml` e `BuiltInThemes.cs` devono contenere hex IDENTICI per le varianti Light/Dark (il commento in `BuiltInThemes.cs` lo impone).
- Non aggiungere/rimuovere chiavi in `ThemeColorKeys` (i temi custom salvati devono continuare a caricare).
- Dopo ogni task: `dotnet build FileExplorer.sln` senza errori. Il PostToolUse hook esegue `dotnet format whitespace` da solo sui file editati.
- Non aggiungere Claude come co-author nei commit.
- Verifica finale: `dotnet test` (attesi 413 pass).

## Tavola colori (fonte di verità)

| Chiave | Light | Dark |
|---|---|---|
| Accent | `#0EA5A0` | `#0EA5A0` |
| AccentGradientStart | `#0EA5A0` | `#0EA5A0` |
| AccentGradientEnd | `#0EA5A0` | `#0EA5A0` |
| OnAccent | `#FFFFFF` | `#FFFFFF` |
| Surface | `#F7F8F8` | `#191B1E` |
| Card | `#FFFFFF` | `#212428` |
| CardBorder | `#E4E7E9` | `#2E3237` |
| Field | `#EFF1F2` | `#2C2F34` |
| TextPrimary | `#26292C` | `#E8EAEC` |
| TextMuted | `#6D7278` | `#9AA0A6` |
| SuccessBg | `#E3F2EB` | `#20362C` |
| SuccessFg | `#1D7F56` | `#34B27D` |
| WarningBg | `#F6EDD8` | `#3A3122` |
| WarningFg | `#8F6400` | `#E0B25C` |
| ErrorBg | `#F9E3E1` | `#3D2624` |
| ErrorFg | `#C23B2E` | `#F08080` |
| ProgressBg | `#DEF0EF` | `#1E3534` |
| ProgressFg | `#0B7D79` | `#2DD4CD` |
| NeutralBg | `#EBEDEE` | `#2C2F34` |
| NeutralFg | `#64696E` | `#A6ACB2` |
| Treemap1 | `#9DC3BC` | `#3E5A54` |
| Treemap2 | `#D3C089` | `#5A5133` |
| Treemap3 | `#A9C29A` | `#45543C` |
| Treemap4 | `#96B4CC` | `#3B4C5E` |
| Treemap5 | `#B5A7C9` | `#4C4258` |
| Treemap6 | `#C4AC9A` | `#574838` |
| SparklineLine | `#0B7D79` | `#2DD4CD` |
| SparklineFill | `#330EA5A0` | `#332DD4CD` |

Nota: `AccentGradient` resta una `LinearGradientBrush` (i test `ThemeServiceTests` la costruiscono da Start/End) ma con entrambi gli stop uguali → visivamente piatta. I `SparklineFill` hanno alpha `33` come oggi.

---

### Task 1: Palette — nuovi valori Light/Dark (model: haiku)

**Files:**
- Modify: `FileExplorer/Styles/Palette.axaml`
- Modify: `FileExplorer/Services/BuiltInThemes.cs`

**Interfaces:**
- Consumes: —
- Produces: le chiavi `Brush.*` esistenti con i nuovi valori; nessuna chiave aggiunta/rimossa.

- [ ] **Step 1: Aggiorna `Palette.axaml`**

Sostituisci SOLO i valori `Color=`/`GradientStop Color=` di entrambe le varianti con la Tavola colori sopra. Struttura, chiavi e ordine restano identici. Il gradiente diventa:

```xml
<LinearGradientBrush x:Key="Brush.AccentGradient" StartPoint="0%,0%" EndPoint="100%,0%">
  <GradientStop Color="#0EA5A0" Offset="0" />
  <GradientStop Color="#0EA5A0" Offset="1" />
</LinearGradientBrush>
```

(in entrambe le varianti Light e Dark; `Brush.Sparkline.Fill` Light = `#330EA5A0`, Dark = `#332DD4CD`).

- [ ] **Step 2: Aggiorna `BuiltInThemes.cs`**

Stessi hex della tavola nei dizionari `Light.Colors` e `Dark.Colors` (formato `"#RRGGBB"` maiuscolo come oggi; i due SparklineFill con prefisso alpha `#33...`). Nessuna altra modifica al file.

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori.

- [ ] **Step 4: Test temi**

Run: `dotnet test --filter "FullyQualifiedName~Theme"`
Expected: tutti PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Styles/Palette.axaml FileExplorer/Services/BuiltInThemes.cs
git commit -m "style(theme): palette Refined Minimal teal, gradiente piatto"
```

---

### Task 2: Controls — card flat con barra-stato, badge squadrati, bottoni solidi (model: sonnet)

**Files:**
- Modify: `FileExplorer/Styles/Controls.axaml`

**Interfaces:**
- Consumes: chiavi `Brush.*` (Task 1).
- Produces: classi `Border.card` con modificatori di stato `success|warning|error|progress` (barra sinistra 3px) usate dal Task 4; classi esistenti ritoccate. `Button.onaccent` resta (rimosso nel Task 5).

- [ ] **Step 1: `Button.primary` piatto**

Nel blocco `Button.primary`: `Background` da `Brush.AccentGradient` a `{DynamicResource Brush.Accent}`; `CornerRadius` da `8` a `6`. Nel selettore `:pointerover` del primary: `Background` da `Brush.AccentGradient` a `{DynamicResource Brush.Accent}` (l'`Opacity 0.85` esistente resta). Blocco `:disabled` invariato.

- [ ] **Step 2: Radius 6 su secondary/iconbtn/onaccent**

`Button.secondary`, `Button.iconbtn`, `Button.onaccent`: `CornerRadius` da `8` a `6`. Nessun altro cambio qui.

- [ ] **Step 3: ProgressBar solida**

```xml
<Style Selector="ProgressBar">
  <Setter Property="MinHeight" Value="4" />
  <Setter Property="Height" Value="4" />
  <Setter Property="CornerRadius" Value="2" />
  <Setter Property="Foreground" Value="{DynamicResource Brush.Accent}" />
  <Setter Property="Background" Value="{DynamicResource Brush.Field}" />
</Style>
```

- [ ] **Step 4: Card senza bordo + modificatori stato**

Sostituisci il blocco `Border.card` e aggiungi i modificatori:

```xml
<Style Selector="Border.card">
  <Setter Property="Background" Value="{DynamicResource Brush.Card}" />
  <Setter Property="BorderBrush" Value="Transparent" />
  <Setter Property="BorderThickness" Value="0" />
  <Setter Property="CornerRadius" Value="8" />
  <Setter Property="Padding" Value="14" />
  <Setter Property="Margin" Value="0,6" />
  <Setter Property="BoxShadow" Value="0 1 3 0 #18000000" />
</Style>

<!-- Barra-stato laterale: 3px a sinistra, colore per stato -->
<Style Selector="Border.card.success">
  <Setter Property="BorderThickness" Value="3,0,0,0" />
  <Setter Property="BorderBrush" Value="{DynamicResource Brush.SuccessFg}" />
</Style>
<Style Selector="Border.card.warning">
  <Setter Property="BorderThickness" Value="3,0,0,0" />
  <Setter Property="BorderBrush" Value="{DynamicResource Brush.WarningFg}" />
</Style>
<Style Selector="Border.card.error">
  <Setter Property="BorderThickness" Value="3,0,0,0" />
  <Setter Property="BorderBrush" Value="{DynamicResource Brush.ErrorFg}" />
</Style>
<Style Selector="Border.card.progress">
  <Setter Property="BorderThickness" Value="3,0,0,0" />
  <Setter Property="BorderBrush" Value="{DynamicResource Brush.ProgressFg}" />
</Style>
```

- [ ] **Step 5: Badge squadrato**

Nel blocco base `Border.badge`: `CornerRadius` da `999` a `4`, `Padding` da `10,3` a `8,3`. I 4 varianti colore (`success/warning/error/progress`) restano invariate (i colori arrivano dalla nuova palette).

- [ ] **Step 6: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori.

- [ ] **Step 7: Commit**

```bash
git add FileExplorer/Styles/Controls.axaml
git commit -m "style(controls): card flat con barra-stato, badge squadrati, primary solido"
```

---

### Task 3: Header piatti nelle 7 view (model: sonnet)

**Files:**
- Modify: `FileExplorer/Views/CopyPairsView.axaml:15-35`
- Modify: `FileExplorer/Views/WatchFoldersView.axaml:9-22`
- Modify: `FileExplorer/Views/ComparisonView.axaml` (header, riga ~8)
- Modify: `FileExplorer/Views/DiskUsageView.axaml` (header, riga ~10)
- Modify: `FileExplorer/Views/DuplicatesView.axaml` (header, riga ~9)
- Modify: `FileExplorer/Views/SettingsView.axaml` (header, riga ~10)
- Modify: `FileExplorer/Views/RemoteBrowserView.axaml` (header, righe ~16-70)

**Interfaces:**
- Consumes: classi `primary`/`secondary` (Task 2), chiavi `Brush.*`.
- Produces: nessun uso residuo di `Brush.AccentGradient` e di `Foreground OnAccent` nelle view; `Classes="onaccent"` non più referenziato (prerequisito del Task 5).

Trasformazione identica per OGNI header (il `Border` in cima con commento `<!-- Header con gradiente -->`):

- [ ] **Step 1: Contenitore header**

Da:
```xml
<Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
```
A:
```xml
<Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Card}"
        BorderBrush="{DynamicResource Brush.CardBorder}" BorderThickness="0,0,0,1" Padding="20,14">
```
Aggiorna anche il commento in `<!-- Header piatto -->`.

- [ ] **Step 2: Colori dei contenuti header (in tutte le view)**

Dentro il solo Border header:
- `i:Icon` del titolo: `Foreground` da `Brush.OnAccent` a `{DynamicResource Brush.Accent}`.
- `TextBlock` del titolo: `Foreground` da `Brush.OnAccent` a `{DynamicResource Brush.TextPrimary}`.
- Ogni altro `TextBlock`/`i:Icon` con `Foreground="{DynamicResource Brush.OnAccent}"` (es. "MB/s" e icona gauge in CopyPairsView, label in RemoteBrowserView): → `{DynamicResource Brush.TextMuted}`.

- [ ] **Step 3: Bottoni header**

- CopyPairsView `AddPairCommand`, WatchFoldersView `AddRuleCommand`: `Classes="onaccent"` → `Classes="primary"`.
- ComparisonView/DiskUsageView/DuplicatesView/SettingsView: se l'header contiene bottoni `onaccent`, l'azione principale → `primary`, le altre → `secondary`.
- RemoteBrowserView (5 bottoni `onaccent`): `OnConnectClick` → `primary`; `OnNewProfileClick`, `OnManageProfilesClick`, `OnDeleteProfileClick`, `OnDisconnectClick` → `secondary`.

- [ ] **Step 4: Verifica nessun residuo**

Run: `grep -rn "AccentGradient\|onaccent" FileExplorer/Views/`
Expected: nessun risultato. (Se `Brush.OnAccent` resta usato altrove fuori header, es. dentro elementi con sfondo accent, va bene: verifica solo che non sia più su sfondo chiaro.)

- [ ] **Step 5: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori.

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Views/
git commit -m "style(views): header piatti senza gradiente, azioni primary/secondary"
```

---

### Task 4: Barra-stato sulle card di Copia e Sync auto (model: sonnet)

**Files:**
- Modify: `FileExplorer/Views/CopyPairsView.axaml:87` (Border della card nel DataTemplate)
- Modify: `FileExplorer/Views/WatchFoldersView.axaml:46` (Border della card nel DataTemplate)

**Interfaces:**
- Consumes: modificatori `Border.card.success|warning|error|progress` (Task 2); converter `EnumEquals` già presente nelle Resources di CopyPairsView; proprietà esistenti `StateKind` (CopyPairs) ed `Enabled` (WatchRule).
- Produces: —

- [ ] **Step 1: CopyPairsView — card con stato**

Il `<Border Classes="card">` del DataTemplate (riga ~87) diventa:

```xml
<Border Classes="card"
        Classes.success="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Success}"
        Classes.warning="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Warning}"
        Classes.error="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Error}"
        Classes.progress="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Copying}">
```

(stessi binding già usati dal badge più sotto nello stesso template — copiali da lì.)

- [ ] **Step 2: WatchFoldersView — card attiva evidenziata**

Il `<Border Classes="card">` del DataTemplate (riga ~46) diventa:

```xml
<Border Classes="card" Classes.progress="{Binding Enabled}">
```

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori.

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Views/CopyPairsView.axaml FileExplorer/Views/WatchFoldersView.axaml
git commit -m "style(views): barra-stato laterale sulle card di copia e sync"
```

---

### Task 5: Cleanup onaccent + verifica completa (model: haiku)

**Files:**
- Modify: `FileExplorer/Styles/Controls.axaml` (rimozione blocchi `Button.onaccent`)

**Interfaces:**
- Consumes: Task 3 completato (nessun uso residuo di `onaccent`).
- Produces: stile morto eliminato.

- [ ] **Step 1: Verifica precondizione**

Run: `grep -rn "onaccent" FileExplorer/Views/`
Expected: nessun risultato. Se ci sono risultati, FERMATI e segnala (Task 3 incompleto).

- [ ] **Step 2: Rimuovi stile**

In `Controls.axaml` elimina i due blocchi `Button.onaccent` e `Button.onaccent:pointerover` (incluso il commento `<!-- Bottone bianco semi-trasparente per l'header col gradiente -->`).

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori.

- [ ] **Step 4: Suite completa**

Run: `dotnet test`
Expected: 413/413 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Styles/Controls.axaml
git commit -m "style(controls): rimuove Button.onaccent ormai inutilizzato"
```
