# Sbroglione

Applicazione desktop multipiattaforma (Avalonia UI, .NET 10) per esplorare, confrontare e sincronizzare cartelle. Nata come file explorer a doppio pannello, include copia massiva con verifica checksum, confronto directory, sincronizzazione automatica, ricerca duplicati, analisi dello spazio disco e accesso a server remoti FTP/SFTP.

## Funzionalità

L'interfaccia è organizzata in schede:

- **Copia** — code di coppie sorgente→destinazione con copia parallela, profili riutilizzabili, journal di ripresa, simulazione (dry-run), verifica checksum post-copia e throttling I/O in base al tipo di disco (HDD/SSD/NVMe).
- **Server remoto** — browser per server FTP e SFTP (FluentFTP / SSH.NET) con upload/download; le credenziali sono salvate nel keystore nativo del sistema (Windows Credential Manager, macOS Keychain, `secret-tool` su Linux).
- **Confronto** — confronto ricorsivo tra due directory (presenza, dimensione, checksum, confronto byte-a-byte) con esportazione del report.
- **Sync auto** — regole di sincronizzazione automatica ("watch folder"): al variare della cartella sorgente il contenuto viene riallineato sulla destinazione; le regole attive partono all'avvio dell'app.
- **Duplicati** — ricerca di file duplicati basata su dimensione e checksum.
- **Spazio disco** — analisi dell'uso del disco con visualizzazione treemap.
- **Impostazioni** — preferenze dell'app e temi: chiaro/scuro più temi custom creabili con un editor dedicato.

## Requisiti

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) o superiore
- Linux, Windows o macOS (desktop)

## Compilazione

```bash
dotnet build Sbroglione.sln
```

## Avvio

```bash
dotnet run --project Sbroglione.Desktop
```

## Test

```bash
dotnet test
```

I test (xunit) vivono in `Sbroglione.Tests`.

## Build distribuibile

### Windows (.exe)

Self-contained executable (include .NET runtime):
```bash
dotnet publish Sbroglione.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `Sbroglione.Desktop/bin/Release/net10.0/win-x64/publish/Sbroglione.Desktop.exe`

Framework-dependent (richiede .NET Runtime installato):
```bash
dotnet publish Sbroglione.Desktop -c Release -r win-x64 -p:PublishSingleFile=true
```

### Linux (.AppImage)

Prerequisiti: `appimagetool` installato e `wget`/`curl`.

```bash
# 1. Pubblica per Linux
dotnet publish Sbroglione.Desktop -c Release -r linux-x64 --self-contained

# 2. Prepara la struttura AppImage
APPDIR="Sbroglione.AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/pixmaps"

# 3. Copia l'eseguibile
cp -r Sbroglione.Desktop/bin/Release/net10.0/linux-x64/publish/* "$APPDIR/usr/bin/"

# 4. Crea il desktop entry
cat > "$APPDIR/usr/share/applications/Sbroglione.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Sbroglione
Exec=Sbroglione.Desktop
Icon=Sbroglione
Categories=Utility;
EOF

# 5. Crea l'AppImage
appimagetool "$APPDIR" "Sbroglione-x86_64.AppImage"
chmod +x Sbroglione-x86_64.AppImage
```

Output: `Sbroglione-x86_64.AppImage` (portable, eseguibile direttamente)

### macOS (.app)

```bash
dotnet publish Sbroglione.Desktop -c Release -r osx-x64 --self-contained
```

Avvolgi l'output in un bundle `.app` usando lo script ufficiale Avalonia (vedi docs).

## Struttura del progetto

```
Sbroglione.sln            Soluzione (a livello di root del repo)
Sbroglione/               Progetto core
  Models/                   Dati semplici (WatchRule, profili, ecc.)
  Services/                 Logica: file system, copia, checksum, FTP/SFTP, temi, watch folder
  ViewModels/               ReactiveUI (un ViewModel per vista)
  Views/                    XAML Avalonia + code-behind
  Converters/               Value converter per il binding
  Styles/                   Palette.axaml (brush a tema) e Controls.axaml (stili per classe)
Sbroglione.Desktop/       Entry point desktop (WinExe)
Sbroglione.Tests/         Test xunit
```

Architettura a livelli: `Views` → `ViewModels` → `Services` (statici) → `Models`. Nessun container DI: le viste dei tab creano il proprio ViewModel nel costruttore.

## Stack tecnologico

- [Avalonia UI](https://avaloniaui.net/) 11.2 (tema Fluent, font Inter)
- ReactiveUI per l'MVVM
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) e [SSH.NET](https://github.com/sshnet/SSH.NET) per i client remoti
- [Projektanker.Icons.Avalonia](https://github.com/Projektanker/Icons.Avalonia) (icone FontAwesome)

## Convenzioni

- Niente colori hardcoded nelle viste: usare sempre `{DynamicResource Brush.*}` definiti in `Styles/Palette.axaml`.
- Nuove chiavi colore vanno aggiunte in `Palette.axaml` (entrambe le varianti), `ThemeColorKeys` e `BuiltInThemes`.
- Lo stile del codice è definito in `.editorconfig` (`dotnet format whitespace`).
- Non committare direttamente su `main`: branch di feature + pull request.

## Licenza

[MIT](LICENSE)
