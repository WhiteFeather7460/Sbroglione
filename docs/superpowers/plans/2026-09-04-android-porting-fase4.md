# Android Porting Fase 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `WatchFolderForegroundService` (crash safety, explicit stop path reachable from the UI, Start/Stop race guard, no Activity-context leak) so the watch-folder foreground service is ready for the Android porting's final manual verification pass.

**Architecture:** All changes are confined to `Sbroglione.Android/WatchFolderForegroundService.cs`, `Sbroglione.Android/MainActivity.cs`, `Sbroglione/App.axaml.cs` (new platform seam) and `Sbroglione/ViewModels/WatchFoldersViewModel.cs` (wiring the new stop seam where the last rule gets disabled). No new files, no changes to `IFileSystemAccessor`, copy/checksum/compare services, or the 3 dialogs (already done — see spec correction).

**Tech Stack:** .NET 8 (Sbroglione.Android targets `net10.0-android`), Avalonia, xunit (`Sbroglione.Tests`, desktop-only — cannot reference Android types).

**Spec:** `docs/superpowers/specs/2026-09-04-android-porting-fase4-design.md`

## Global Constraints

- Never hardcode colors in views; not applicable to this plan (no view/style changes).
- `dotnet build Sbroglione.sln` must stay green; `Sbroglione.Android` is excluded from `.Build.0` so it is **not** built by that command — build it explicitly via `dotnet build Sbroglione.Android/Sbroglione.Android.csproj` after each Android-side change.
- `Sbroglione.Tests` cannot reference Android types (`Sbroglione.Android` is not a project reference) — no unit test can exercise `WatchFolderForegroundService`, `MainActivity`, or any `Android.*` API directly. Where a step below has no automated test, say so explicitly and rely on `dotnet build` + the final manual verification pass (out of scope for this plan).
- Comments in this codebase explain *why*, never *what* — match existing style (see current XML doc comments in the touched files) when adding any.
- Never commit directly to `main`: work on a feature branch, e.g. `android-porting-fase4`.

---

### Task 1: `WatchFolderForegroundService` — crash-safe `StartForeground` + fix Activity-context leak

**Files:**
- Modify: `Sbroglione.Android/WatchFolderForegroundService.cs:52-91` (`OnStartCommand`)
- Modify: `Sbroglione.Android/MainActivity.cs:28-48` (`CustomizeAppBuilder`), `:75-79` (`StartWatchFolderForegroundService`)

**Interfaces:**
- Consumes: nothing new from other tasks.
- Produces: `MainActivity.StartWatchFolderForegroundService` becomes a `static` method taking no captured `this` — Task 2 adds a symmetric `static void StopWatchFolderForegroundService()` next to it, so the naming/signature convention set here (static, `Application.Context`-based `Intent`) must be followed there too.

This task fixes two of the four concerns: `StartForeground` can throw
(`ForegroundServiceDidNotStartInTimeException` or a system refusal under
battery restrictions) and currently crashes the process; and the
`App.StartBackgroundWatchHost` delegate is a bound method group on `this`
(the `MainActivity` instance), leaking the Activity for the lifetime of the
static field.

- [ ] **Step 1: Wrap `StartForeground` in try/catch in `WatchFolderForegroundService.OnStartCommand`**

Replace:

```csharp
        // Il tipo esplicito esiste da Android 10; sotto, startForeground non lo accetta.
        // Guardia con OperatingSystem e non con Build.VERSION.SdkInt: solo la prima è
        // riconosciuta dall'analyzer di compatibilità piattaforma (CA1416).
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);
```

with:

```csharp
        // Il tipo esplicito esiste da Android 10; sotto, startForeground non lo accetta.
        // Guardia con OperatingSystem e non con Build.VERSION.SdkInt: solo la prima è
        // riconosciuta dall'analyzer di compatibilità piattaforma (CA1416).
        //
        // StartForeground può lanciare (ForegroundServiceDidNotStartInTimeException, o un
        // rifiuto di sistema sotto restrizioni batteria): senza try/catch abbatterebbe il
        // processo. Se fallisce, il service si ferma da solo invece di restare in uno stato
        // a metà (avviato ma senza notifica, che il sistema tratterebbe come ANR).
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
            else
                StartForeground(NotificationId, notification);
        }
        catch (Exception)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }
```

- [ ] **Step 2: Build to verify the change compiles**

Run: `dotnet build Sbroglione.Android/Sbroglione.Android.csproj`
Expected: build succeeds, no new warnings on the touched lines.

No automated test for this step: `StartForeground` and `StartCommandResult` are
Android runtime APIs, unreachable from `Sbroglione.Tests`. Covered by the final
manual verification pass (spec section 3): confirm the service still starts
normally, and that a forced-failure path (not reproducible on a normal device)
is accepted as untestable outside a lab setup.

- [ ] **Step 3: Make `MainActivity.StartWatchFolderForegroundService` static and use `Application.Context`**

In `Sbroglione.Android/MainActivity.cs`, replace:

```csharp
    private void StartWatchFolderForegroundService()
    {
        var intent = new Intent(this, typeof(WatchFolderForegroundService));
        StartForegroundService(intent);
    }
```

with:

```csharp
    /// <summary>
    /// Static e su <see cref="global::Android.App.Application.Context"/> invece che su
    /// <c>this</c>: <see cref="App.StartBackgroundWatchHost"/> è un campo statico registrato
    /// una volta in <see cref="CustomizeAppBuilder"/> e vissuto per tutta la vita del processo,
    /// quindi non deve mai catturare l'Activity corrente (che può essere distrutta e ricreata
    /// più volte in quella finestra di tempo).
    /// </summary>
    private static void StartWatchFolderForegroundService()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(WatchFolderForegroundService));
        context.StartForegroundService(intent);
    }
```

Update the registration in `CustomizeAppBuilder` (line 34) — it already reads
`App.StartBackgroundWatchHost = StartWatchFolderForegroundService;`, which now
binds to the static method instead of an instance method; no textual change
needed there, but verify it still compiles (a static method group conversion
to `Action` is valid with no `this` capture).

- [ ] **Step 4: Build to verify**

Run: `dotnet build Sbroglione.Android/Sbroglione.Android.csproj`
Expected: build succeeds. No automated test possible (Android Activity/Context
APIs); covered by manual verification pass (confirm the foreground service
still starts after an Activity recreation, e.g. rotation, without the app
crashing or losing the watch-folder notification).

- [ ] **Step 5: Commit**

```bash
git add Sbroglione.Android/WatchFolderForegroundService.cs Sbroglione.Android/MainActivity.cs
git commit -m "fix(android): crash-safe StartForeground, drop Activity leak from watch-folder seam"
```

---

### Task 2: Explicit stop path + Start/Stop race guard

**Files:**
- Modify: `Sbroglione.Android/WatchFolderForegroundService.cs:42-109` (`_runnersStarted` field, `OnStartCommand`, `OnDestroy`)
- Modify: `Sbroglione.Android/MainActivity.cs` (add `StopWatchFolderForegroundService`, register a new seam)
- Modify: `Sbroglione/App.axaml.cs:28` (add `StopBackgroundWatchHost` seam next to `StartBackgroundWatchHost`)
- Modify: `Sbroglione/ViewModels/WatchFoldersViewModel.cs:210-254` (`ApplyRuleStateAsync` or equivalent — call the new stop seam when no rule stays enabled)

**Interfaces:**
- Consumes: `MainActivity.StartWatchFolderForegroundService` static/`Application.Context` pattern from Task 1 — the new `StopWatchFolderForegroundService` must follow the same shape (`static`, `Application.Context`-based `Intent`).
- Produces: `App.StopBackgroundWatchHost` (`public static Action? StopBackgroundWatchHost { get; set; }`), `null` on desktop exactly like `StartBackgroundWatchHost`. Nothing later in this plan consumes it (last task).

Today the only way to stop the foreground service's runners is `OnDestroy`,
called by the system (process kill) — there is no UI-reachable stop, and once
one exists it can race with a sticky-restarted `OnStartCommand` on another
thread.

- [ ] **Step 1: Guard `_runnersStarted` with a lock in `WatchFolderForegroundService`**

Replace the field and its two usages. Current field (line 48):

```csharp
    private bool _runnersStarted;
```

New:

```csharp
    /// <summary>
    /// Protegge <c>_runnersStarted</c>: con uno stop raggiungibile dalla UI (oltre al riavvio
    /// sticky del sistema), OnStartCommand e OnDestroy possono correre su thread diversi nello
    /// stesso momento — senza lock una race lascerebbe i runner avviati ma il flag a false (o
    /// viceversa), disallineando lo stato del service da quello reale di WatchFolderService.
    /// </summary>
    private readonly object _runnersLock = new();
    private bool _runnersStarted;
```

In `OnStartCommand`, replace:

```csharp
        if (!_runnersStarted)
        {
            _runnersStarted = true;

            // Fuori dal main thread: StartAllEnabledRules legge il file delle regole e crea
            // i FileSystemWatcher, e il main thread qui è quello della UI dell'app.
            _ = Task.Run(() =>
            {
                try
                {
                    WatchFolderService.StartAllEnabledRules();
                }
                catch (Exception)
                {
                    // StartAllEnabledRules non lancia; difesa in profondità: un'eccezione su
                    // un thread di pool abbatterebbe il processo, notifica inclusa.
                }
            });
        }
```

with:

```csharp
        bool shouldStartRunners = false;
        lock (_runnersLock)
        {
            if (!_runnersStarted)
            {
                _runnersStarted = true;
                shouldStartRunners = true;
            }
        }

        if (shouldStartRunners)
        {
            // Fuori dal main thread: StartAllEnabledRules legge il file delle regole e crea
            // i FileSystemWatcher, e il main thread qui è quello della UI dell'app.
            _ = Task.Run(() =>
            {
                try
                {
                    WatchFolderService.StartAllEnabledRules();
                }
                catch (Exception)
                {
                    // StartAllEnabledRules non lancia; difesa in profondità: un'eccezione su
                    // un thread di pool abbatterebbe il processo, notifica inclusa.
                }
            });
        }
```

In `OnDestroy`, replace:

```csharp
        try
        {
            WatchFolderService.StopAll();
        }
        catch (Exception)
        {
            // Lo shutdown non deve mai lanciare fuori da OnDestroy.
        }

        _runnersStarted = false;
        base.OnDestroy();
```

with:

```csharp
        try
        {
            WatchFolderService.StopAll();
        }
        catch (Exception)
        {
            // Lo shutdown non deve mai lanciare fuori da OnDestroy.
        }

        lock (_runnersLock)
        {
            _runnersStarted = false;
        }

        base.OnDestroy();
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build Sbroglione.Android/Sbroglione.Android.csproj`
Expected: build succeeds. No automated test possible — `lock` around a private
field guarding Android-only `Task.Run`/`Service` calls cannot be isolated into
`Sbroglione.Tests` without dragging in Android types; covered by manual
verification (rapid enable/disable of watch rules while the service is live,
watching for duplicate runners or a stuck notification).

- [ ] **Step 3: Add `App.StopBackgroundWatchHost` seam**

In `Sbroglione/App.axaml.cs`, right after the existing `StartBackgroundWatchHost`
property (line 28), add:

```csharp
    /// <summary>
    /// Seam piattaforma: ferma l'host di background avviato da
    /// <see cref="StartBackgroundWatchHost"/>, quando l'ultima regola watch-folder abilitata
    /// viene disabilitata — su desktop i runner restano semplicemente in-process finché il
    /// processo vive, quindi non serve stop esplicito e questo resta <c>null</c> lì.
    /// </summary>
    public static Action? StopBackgroundWatchHost { get; set; }
```

- [ ] **Step 4: Add `MainActivity.StopWatchFolderForegroundService` and register it**

In `Sbroglione.Android/MainActivity.cs`, add next to `StartWatchFolderForegroundService`
(Task 1's version):

```csharp
    /// <summary>
    /// Simmetrico di <see cref="StartWatchFolderForegroundService"/>: ferma il foreground
    /// service quando l'utente disabilita l'ultima regola watch-folder attiva, invece di
    /// aspettare che sia il sistema a distruggerlo.
    /// </summary>
    private static void StopWatchFolderForegroundService()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(WatchFolderForegroundService)));
    }
```

In `CustomizeAppBuilder`, right after the existing
`App.StartBackgroundWatchHost = StartWatchFolderForegroundService;` (line 34), add:

```csharp
        App.StopBackgroundWatchHost = StopWatchFolderForegroundService;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build Sbroglione.Android/Sbroglione.Android.csproj`
Expected: build succeeds.

- [ ] **Step 6: Wire the stop seam into `WatchFoldersViewModel` when no rule stays enabled**

Read `Sbroglione/ViewModels/WatchFoldersViewModel.cs` around lines 205-254
(the method that calls `WatchFolderService.Stop`/`Start` per rule, currently
invoking `App.StartBackgroundWatchHost?.Invoke()` after a successful `Start`).
After that block, once the per-rule Start/Stop has been applied, add a check
against the full rule set: if no rule in `Rules` has `Model.Enabled == true`,
call `App.StopBackgroundWatchHost?.Invoke()`. Concretely, replace the closing
of the method (after the `if (rule.Model.Enabled && ...)` block ends, i.e.
right after line 253's closing `}`) by appending:

```csharp

        // Nessuna regola abilitata rimasta: ferma il foreground service invece di lasciarlo
        // vivo con una notifica persistente e nulla da sincronizzare. Null su desktop.
        if (!Rules.Any(r => r.Model.Enabled))
        {
            App.StopBackgroundWatchHost?.Invoke();
        }
```

Add `using Sbroglione;` at the top of the file if `App` is not already
resolvable (check the existing `using` block first — `App.StartBackgroundWatchHost`
is already called a few lines above, so the namespace is already in scope; do
not add a duplicate `using`).

- [ ] **Step 7: Build and run the desktop test suite**

Run: `dotnet build Sbroglione.sln && dotnet test Sbroglione.Tests`
Expected: build succeeds (main solution excludes `Sbroglione.Android`, so this
also confirms the `WatchFoldersViewModel` change alone doesn't break desktop);
all existing tests still pass — `App.StopBackgroundWatchHost` is `null` on
desktop, so the new call is a no-op there, same pattern as
`StartBackgroundWatchHost` already exercised by existing `WatchFoldersViewModel`
tests. No new test is added for the Android-only stop path itself: covered by
manual verification (disable the last enabled rule from the UI, confirm the
foreground service's notification disappears).

- [ ] **Step 8: Commit**

```bash
git add Sbroglione.Android/WatchFolderForegroundService.cs Sbroglione.Android/MainActivity.cs Sbroglione/App.axaml.cs Sbroglione/ViewModels/WatchFoldersViewModel.cs
git commit -m "feat(android): stop watch-folder foreground service when last rule is disabled"
```

---

## After this plan

Once both tasks are done and merged into the porting branch, update `IDEE.md`
point 26 to reflect: Fase 4 code complete (foreground service hardened, SAF
dropped as unnecessary, metadata/dialogs confirmed already done), and move to
the final manual verification pass (spec section 3: layout, FTP/SFTP, tema,
foreground service on-device, all dialogs, hardware Back button) before
opening the single porting PR.
