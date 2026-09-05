# Sbroglione

Cross-platform desktop app (Avalonia UI, .NET 10) to explore, compare, and sync folders. Started as a dual-pane file explorer, it now includes bulk copy with checksum verification, directory comparison, automatic synchronization, duplicate finder, disk usage analysis, and access to remote FTP/SFTP servers.

## Features

The UI is organized into tabs:

- **Copy** — source→destination pair queue with parallel copying, reusable profiles, resumable journal, dry-run simulation, post-copy checksum verification, and I/O throttling based on disk type (HDD/SSD/NVMe).
- **Remote server** — browser for FTP and SFTP servers (FluentFTP / SSH.NET) with upload/download; credentials are stored in the OS-native keystore (Windows Credential Manager, macOS Keychain, `secret-tool` on Linux).
- **Compare** — recursive comparison between two directories (presence, size, checksum, byte-by-byte comparison) with report export.
- **Auto sync** — automatic sync rules ("watch folders"): when the source folder changes, its content is realigned to the destination; active rules start when the app launches.
- **Duplicates** — duplicate file search based on size and checksum.
- **Disk usage** — disk usage analysis with treemap visualization.
- **Settings** — app preferences and themes: light/dark plus custom themes creatable with a dedicated editor.

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later
- Linux, Windows, or macOS (desktop)
- For Android: `android` workload, JDK 17, and `ANDROID_HOME`/`JAVA_HOME` set (see [Android](#android) below)

## Build

```bash
dotnet build Sbroglione.sln
```

`Sbroglione.Android` is excluded from the solution's build (`.Build.0`), so this command never touches it — build it separately (see [Android](#android)).

## Run

```bash
dotnet run --project Sbroglione.Desktop
```

## Tests

```bash
dotnet test
```

Tests (xunit) live in `Sbroglione.Tests`.

## Distributable builds

### Windows (.exe)

Self-contained executable (includes .NET runtime):
```bash
dotnet publish Sbroglione.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `Sbroglione.Desktop/bin/Release/net10.0/win-x64/publish/Sbroglione.Desktop.exe`

Framework-dependent (requires .NET Runtime installed):
```bash
dotnet publish Sbroglione.Desktop -c Release -r win-x64 -p:PublishSingleFile=true
```

### Linux (.AppImage)

Prerequisites: `appimagetool` installed and `wget`/`curl`.

```bash
# 1. Publish for Linux
dotnet publish Sbroglione.Desktop -c Release -r linux-x64 --self-contained

# 2. Prepare the AppImage structure
APPDIR="Sbroglione.AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/pixmaps"

# 3. Copy the executable
cp -r Sbroglione.Desktop/bin/Release/net10.0/linux-x64/publish/* "$APPDIR/usr/bin/"

# 4. Create the desktop entry
cat > "$APPDIR/usr/share/applications/Sbroglione.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Sbroglione
Exec=Sbroglione.Desktop
Icon=Sbroglione
Categories=Utility;
EOF

# 5. Create the AppImage
appimagetool "$APPDIR" "Sbroglione-x86_64.AppImage"
chmod +x Sbroglione-x86_64.AppImage
```

Output: `Sbroglione-x86_64.AppImage` (portable, directly executable)

### macOS (.app)

```bash
dotnet publish Sbroglione.Desktop -c Release -r osx-x64 --self-contained
```

Wrap the output in a `.app` bundle using Avalonia's official script (see docs).

## Android

`Sbroglione.Android` (package `com.whitefeather.sbroglione`, min SDK 26 / target SDK 36) is a separate head project, excluded from `Sbroglione.sln`'s build — build and deploy it on its own.

### Setup

```bash
dotnet workload install android
```

Also needs a JDK 17 and `ANDROID_HOME`/`JAVA_HOME` pointing at it and at your Android SDK install.

### Build (debug, for an emulator/device)

```bash
dotnet build Sbroglione.Android/Sbroglione.Android.csproj -c Debug -f net10.0-android \
  -p:AndroidSdkDirectory="$ANDROID_HOME"
```

Output: `Sbroglione.Android/bin/Debug/net10.0-android/com.whitefeather.sbroglione-Signed.apk`

> **Note:** a plain Debug build uses Fast Deployment (assemblies are pushed to the device separately from the APK by IDE tooling); installing that APK with a bare `adb install` crashes at startup with `No assemblies found ... Assuming this is part of Fast Deployment`. For a command-line `adb install` workflow, embed the assemblies into the APK instead:
> ```bash
> dotnet build Sbroglione.Android/Sbroglione.Android.csproj -c Debug -f net10.0-android \
>   -p:AndroidSdkDirectory="$ANDROID_HOME" \
>   -p:EmbedAssembliesIntoApk=true -p:AndroidFastDeploymentType=None
> ```

### Install and run on an emulator/device

```bash
adb devices  # confirm the target is listed as "device"
adb install -r Sbroglione.Android/bin/Debug/net10.0-android/com.whitefeather.sbroglione-Signed.apk
adb shell monkey -p com.whitefeather.sbroglione -c android.intent.category.LAUNCHER 1
```

If you reinstall over a build that used Fast Deployment (or vice versa), uninstall first (`adb uninstall com.whitefeather.sbroglione`) rather than `-r`, since Android caches the previous deployment mode.

### First-run permission

The app needs "All files access" (`MANAGE_EXTERNAL_STORAGE`) to browse arbitrary paths — Android doesn't grant this at install time, it must be enabled manually after first launch: Settings → Apps → Sbroglione → Permissions → "Allow management of all files".

### Release build (signed APK)

```bash
dotnet build Sbroglione.Android/Sbroglione.Android.csproj -c Release -f net10.0-android \
  -p:ApplicationDisplayVersion=1.0.0
```

Output: `Sbroglione.Android/bin/Release/net10.0-android/com.whitefeather.sbroglione-Signed.apk`

## Project structure

```
Sbroglione.sln            Solution (at repo root)
Sbroglione/               Core project
  Models/                   Plain data (WatchRule, profiles, etc.)
  Services/                 Logic: file system, copy, checksum, FTP/SFTP, themes, watch folder
  ViewModels/               ReactiveUI (one ViewModel per view)
  Views/                    Avalonia XAML + code-behind
  Converters/               Value converters for binding
  Styles/                   Palette.axaml (theme-aware brushes) and Controls.axaml (class-based styles)
Sbroglione.Desktop/       Desktop entry point (WinExe)
Sbroglione.Android/       Android head project (excluded from Sbroglione.sln's build)
Sbroglione.Tests/         xunit tests
```

Layering: `Views` → `ViewModels` → `Services` (static) → `Models`. No DI container: tab views create their own ViewModel in the constructor.

## Tech stack

- [Avalonia UI](https://avaloniaui.net/) 11.2 (Fluent theme, Inter font)
- ReactiveUI for MVVM
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) and [SSH.NET](https://github.com/sshnet/SSH.NET) for remote clients
- [Projektanker.Icons.Avalonia](https://github.com/Projektanker/Icons.Avalonia) (FontAwesome icons)

## Conventions

- No hardcoded colors in views: always use `{DynamicResource Brush.*}` defined in `Styles/Palette.axaml`.
- New color keys must be added to `Palette.axaml` (both variants), `ThemeColorKeys`, and `BuiltInThemes`.
- Code style is defined in `.editorconfig` (`dotnet format whitespace`).
- Never commit directly to `main`: feature branch + pull request.

## Transparency

This project's code was implemented entirely by Claude (Anthropic). Ideas, requirements, and design decisions are by WhiteFeather.

## License

[MIT](LICENSE)
