# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Avalonia UI (.NET 8, MVVM/ReactiveUI) desktop app — a dual-pane file explorer/comparator. Two projects:
- `GetStartedApp/` — core project (ViewModels, Views, Utils)
- `GetStartedApp.Desktop/` — desktop entry point (`WinExe`)

`GetStartedApp.sln` lives at repo root (not inside `GetStartedApp/`).

## Build & run

```
dotnet build GetStartedApp.sln
dotnet run --project GetStartedApp.Desktop
```

No test project exists. No CI, no linter/formatter config (`dotnet format` works generically but isn't wired to any script).

## Git

Do not add yourself (Claude) as co-author on commits.
