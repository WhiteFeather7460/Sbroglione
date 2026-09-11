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

Standard flow for any implementation or code change: **analysis → plan → branch → implementation → test/verify → commit → push → pull request.** A task is not complete until its PR is opened.

- Before writing a plan, read and understand the relevant existing code and how it fits the application's flow (layering, callers, side effects).
- For any non-trivial feature or change, always write an implementation plan first (superpowers writing-plans; plans live in `docs/superpowers/plans/`) and execute it with subagents (superpowers subagent-driven-development). After each completed task, mark it as done in the plan file before starting the next one.
- A plan must state: goal, approach, files/components involved, risks/impact, and verification/test strategy.
- All implementation happens on a dedicated feature branch — never directly on `main`/`master` or other shared branches.

### Model selection

Evaluate task complexity per plan task and pick the cheapest model that fits — do not default to the most powerful one. Re-evaluate if complexity shifts mid-task.

- `opus` — complex/architectural work, deep reasoning, hard debugging, changes with wide system impact.
- `sonnet` — standard implementation: medium features, multi-file changes, APIs/services/integrations, non-trivial bug fixes.
- `haiku` — mechanical/boilerplate work: docs, config, small fixes, simple tests.

Each plan task must declare its model, and the dispatcher must pass it to the Agent tool.

### Code simplicity

Prefer the simplest solution that correctly solves the problem. Avoid over-engineering, premature abstraction, unneeded design patterns, and clever-but-opaque code. Reuse existing project patterns where appropriate. Keep functions single-purpose. Do not add complexity for hypothetical future problems.

### Avoiding technical debt

Before adding code, check: does an equivalent solution already exist, would this duplicate logic, is a new abstraction actually needed, does it add complexity, will it stay understandable and modifiable later. Do not leave undocumented workarounds, TODOs, duplicated logic, dead code, or temporary hacks. If technical debt is unavoidable to complete a task, state it explicitly in the PR description with the reason and what would be needed to remove it.

### Documentation

Document a function when its role isn't obvious, covering as relevant: why it exists, when/from where it's called, inputs/outputs, side effects, and which parts of the system it touches. No line-by-line "what it does" comments — document the *why* and the function's place in the application flow.

### Scope

Keep changes focused on the task. No unrelated refactors, except when needed to correctly complete the task, avoid a concrete problem, or preserve code quality/maintainability. Report unrelated issues you notice instead of fixing them inline.

### Verification

Verify every implementation before opening the PR: run tests, lint, type-check, build, or whatever else applies to the project. Do not declare a task complete without having verified the changed behavior — if some verification couldn't be run, say so explicitly.

### Guiding principle

Given equal results, always prefer the solution that is simplest, most readable, most easily verifiable, most consistent with existing code, and carries the least technical debt.

## Communication style

Use caveman mode (terse, compressed) for chat responses during development work. Code, commit messages, and PR descriptions are written normally.

## Git

Do not add yourself (Claude) as co-author on commits.
Never commit directly to `main`: always work on a feature branch and open a pull request.
