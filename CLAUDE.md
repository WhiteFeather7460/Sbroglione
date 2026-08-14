# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Avalonia UI (.NET 8, MVVM/ReactiveUI) desktop app — a dual-pane file explorer/comparator. Two projects:
- `FileExplorer/` — core project (`Models/`, `Services/`, `ViewModels/`, `Views/`)
- `FileExplorer.Desktop/` — desktop entry point (`WinExe`)

`FileExplorer.sln` lives at repo root (not inside `FileExplorer/`).

Layering: `Views` (axaml + code-behind) → `ViewModels` (ReactiveUI) → `Services` (static: file system queries, copy, checksum) → `Models` (plain data). Tab views (`CopyPairsView`, `FileBrowserView`) create their own ViewModel in the constructor; `MainWindow` receives `MainWindowViewModel` from `App.OnFrameworkInitializationCompleted`, and `SelectPathDialog` receives a parameterized `SelectPathDialogViewModel` from its caller. There is no DI container.

## Build & run

```
dotnet build FileExplorer.sln
dotnet run --project FileExplorer.Desktop
```

No test project exists. No CI. `.editorconfig` defines code style; `dotnet format whitespace` runs automatically on edited `.cs`/`.axaml` files via a PostToolUse hook.

## Git

Do not add yourself (Claude) as co-author on commits.
