# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Avalonia UI (.NET 8, MVVM/ReactiveUI) desktop app — a dual-pane file explorer/comparator. Two projects:
- `Sbroglione/` — core project (`Models/`, `Services/`, `ViewModels/`, `Views/`, `Converters/`, `Styles/`)
- `Sbroglione.Desktop/` — desktop entry point (`WinExe`)

`Sbroglione.sln` lives at repo root (not inside `Sbroglione/`).

Layering: `Views` (axaml + code-behind) → `ViewModels` (ReactiveUI) → `Services` (static: file system queries, copy, checksum) → `Models` (plain data). Tab views (`CopyPairsView`, `FileBrowserView`) create their own ViewModel in the constructor; `MainWindow` receives `MainWindowViewModel` from `App.OnFrameworkInitializationCompleted`, and `SelectPathDialog` receives a parameterized `SelectPathDialogViewModel` from its caller. There is no DI container.

Watch-folder: la tab "Sync auto" (`WatchFoldersView`) gestisce regole di sincronizzazione automatica (`WatchRule`/`WatchRuleStore`); i runner (`WatchFolderService`) delle regole attive partono in `App.OnFrameworkInitializationCompleted` e muoiono col processo (nessun handler di shutdown).

Styling: `Styles/Palette.axaml` holds all theme-aware brushes (`Brush.*`, light/dark via ThemeDictionaries); `Styles/Controls.axaml` holds class-based styles (`Button.primary/.secondary/.iconbtn/.onaccent`, `Border.card`, `Border.badge.*`, `TextBox.error`). Never hardcode colors in views — always `{DynamicResource Brush.*}`. Icons via Projektanker.Icons.Avalonia (`fa-*` FontAwesome).

Temi custom: `ThemeService` registra un ResourceDictionary per-tema come `ThemeVariant("Custom", base)` in `Application.Resources.ThemeDictionaries`; i valori built-in restano in `Palette.axaml` e fanno da fallback. Nuove chiavi colore vanno aggiunte in TUTTI e tre i posti: `Palette.axaml` (entrambe le varianti), `ThemeColorKeys`, `BuiltInThemes`.

## Build & run

```
dotnet build Sbroglione.sln
dotnet run --project Sbroglione.Desktop
```

Tests: `Sbroglione.Tests` (xunit) — run with `dotnet test`. No CI. `.editorconfig` defines code style; `dotnet format whitespace` runs automatically on edited `.cs`/`.axaml` files via a PostToolUse hook.

`Sbroglione.Android` (head project) is in the solution but excluded from `.Build.0` in `Sbroglione.sln`, so `dotnet build Sbroglione.sln`/`dotnet test` never build it. To build/deploy it explicitly you need the `android` workload (`dotnet workload install android`), a JDK 17, and `ANDROID_HOME`/`JAVA_HOME` set; then build it on its own with `dotnet build Sbroglione.Android/Sbroglione.Android.csproj`.

## Workflow

For any non-trivial feature or change, always write an implementation plan first (superpowers writing-plans; plans live in `docs/superpowers/plans/`) and execute it with subagents (superpowers subagent-driven-development). After each completed task, mark it as done in the plan file before starting the next one.

Each plan task must declare the most suitable model for its executing subagent (`haiku` for mechanical/boilerplate work, `sonnet` for standard implementation, `opus` for complex logic or security-sensitive code), and the dispatcher must pass that model to the Agent tool to save tokens.

## Git

Do not add yourself (Claude) as co-author on commits.
Never commit directly to `main`: always work on a feature branch and open a pull request.
