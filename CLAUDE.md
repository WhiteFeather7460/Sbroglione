# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Avalonia UI (.NET 8, MVVM/ReactiveUI) desktop app — a dual-pane file explorer/comparator. Two projects:
- `FileExplorer/` — core project (`Models/`, `Services/`, `ViewModels/`, `Views/`, `Converters/`, `Styles/`)
- `FileExplorer.Desktop/` — desktop entry point (`WinExe`)

`FileExplorer.sln` lives at repo root (not inside `FileExplorer/`).

Layering: `Views` (axaml + code-behind) → `ViewModels` (ReactiveUI) → `Services` (static: file system queries, copy, checksum) → `Models` (plain data). Tab views (`CopyPairsView`, `FileBrowserView`) create their own ViewModel in the constructor; `MainWindow` receives `MainWindowViewModel` from `App.OnFrameworkInitializationCompleted`, and `SelectPathDialog` receives a parameterized `SelectPathDialogViewModel` from its caller. There is no DI container.

Styling: `Styles/Palette.axaml` holds all theme-aware brushes (`Brush.*`, light/dark via ThemeDictionaries); `Styles/Controls.axaml` holds class-based styles (`Button.primary/.secondary/.iconbtn/.onaccent`, `Border.card`, `Border.badge.*`, `TextBox.error`). Never hardcode colors in views — always `{DynamicResource Brush.*}`. Icons via Projektanker.Icons.Avalonia (`fa-*` FontAwesome).

## Build & run

```
dotnet build FileExplorer.sln
dotnet run --project FileExplorer.Desktop
```

Tests: `FileExplorer.Tests` (xunit) — run with `dotnet test`. No CI. `.editorconfig` defines code style; `dotnet format whitespace` runs automatically on edited `.cs`/`.axaml` files via a PostToolUse hook.

## Workflow

For any non-trivial feature or change, always write an implementation plan first (superpowers writing-plans; plans live in `docs/superpowers/plans/`) and execute it with subagents (superpowers subagent-driven-development). After each completed task, mark it as done in the plan file before starting the next one.

## Git

Do not add yourself (Claude) as co-author on commits.
Never commit directly to `main`: always work on a feature branch and open a pull request.
