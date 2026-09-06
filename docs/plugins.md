# Writing a Sbroglione tab plugin

Sbroglione (desktop) can load third-party plugins that add a tab to the main
window at runtime. This document is for plugin authors — it covers the
contract, the manifest format, where to install a plugin, and version
compatibility rules.

Plugins are **desktop-only**: discovery is skipped entirely on Android (there
is no plugin install path or ALC-based isolation story on that runtime yet).

## 1. Implement `ITabPlugin`

Reference `Sbroglione.PluginContracts` (a small, dependency-light assembly —
its only dependency is `Avalonia.Controls` for `Control`) and implement:

```csharp
public interface ITabPlugin
{
    string Id { get; }
    string Header { get; }
    string IconGlyph { get; }
    Control CreateView();
    void OnUnload();
}
```

- `Id` — a unique, stable identifier for your plugin. It must match the `id`
  field in `plugin.json` (see below). Two installed plugins that declare the
  same `Id` are not both loaded — the second one found is skipped.
- `Header` — the text shown on the tab.
- `IconGlyph` — a FontAwesome glyph string in the same format as the host's
  built-in tabs, e.g. `"fa-solid fa-download"`.
- `CreateView()` — build and return the tab's root `Control`. The view is
  responsible for creating its own ViewModel in its constructor (no DI
  container in this app). For file/folder access use Avalonia's native
  `StorageProvider`, available on any `Control` — plugins have no access to
  the host's internal services.
- `OnUnload()` — called once, at app shutdown, so the plugin can stop
  threads/connections/timers or flush state. It is called from both the
  normal window-close path and from a self-update restart path, so it must be
  safe to call exactly once and must not throw (an exception here is caught
  and ignored by the host, but you should still clean up correctly).

Keep your plugin assembly's own dependencies (Avalonia excluded — you should
reference the same Avalonia version the host uses, or a compatible one) next
to the main plugin DLL. The host resolves a plugin's transitive
dependencies via `AssemblyDependencyResolver`, which reads the
`<YourMainAssembly>.deps.json` file next to your main DLL if one exists (this
is generated automatically for most project shapes when you publish/build
with `<GenerateDependencyFile>true</GenerateDependencyFile>`, or by default
for anything that produces a `.deps.json`). Without a `.deps.json`, only
assemblies already loaded by the host itself (e.g. `Avalonia.Controls`,
`Sbroglione.PluginContracts`) will resolve automatically.

## 2. Write `plugin.json`

A manifest sits next to your compiled plugin DLL, in the plugin's own
install folder:

```json
{
  "id": "youtubedownloader",
  "displayName": "YouTube Downloader",
  "version": "1.0.0",
  "contractVersion": "1.0.0",
  "mainAssemblyFileName": "YoutubeDownloaderPlugin.dll"
}
```

All five fields are required and must be non-empty strings:

| Field                  | Meaning                                                                 |
|-------------------------|--------------------------------------------------------------------------|
| `id`                   | Unique plugin identifier; should match `ITabPlugin.Id`.                  |
| `displayName`          | Human-readable plugin name (not currently shown in the UI, but kept for tooling/future use). |
| `version`              | Your plugin's own version (semver recommended), independent from `contractVersion`. |
| `contractVersion`      | The version of the `ITabPlugin` contract this plugin was built against (see compatibility rules below). |
| `mainAssemblyFileName` | The file name (not a path) of your plugin's main DLL, in the same folder as `plugin.json`. |

`mainAssemblyFileName` must be a bare file name: no `/` or `\`, and not `..`
or `.`. Anything else is rejected and the plugin is skipped — this prevents a
malicious manifest from pointing outside its own plugin folder.

Field names are matched case-insensitively.

## 3. Where to install a plugin

Each plugin lives in its own subfolder of the plugins root:

```
~/.config/Sbroglione/plugins/<plugin-id>/
    plugin.json
    YourMainAssembly.dll
    (any dependency DLLs your plugin needs, alongside the main DLL)
```

`<plugin-id>` is just the folder name — it does not have to match the
manifest's `id` field, but using the same value avoids confusion. On startup
the host enumerates every subfolder under this root, in a deterministic
(alphabetical, case-insensitive) order by folder name, and tries to load each
one independently.

## 4. Failure handling

Plugin loading is fully isolated per-plugin — nothing about a broken plugin
prevents the app from starting or other plugins from loading:

- Missing/unreadable/malformed `plugin.json` → plugin skipped.
- `contractVersion` incompatible with the host (see below) → plugin skipped.
- `mainAssemblyFileName` missing, unsafe (path traversal), or pointing at a
  file that doesn't exist → plugin skipped.
- DLL fails to load, has no type implementing `ITabPlugin`, or the
  constructor throws → plugin skipped.
- Duplicate `id` across two installed plugins → only the first one found
  (in folder-name order) is loaded; the rest are skipped.
- An exception thrown from `CreateView()`, `Header`, or `IconGlyph` while the
  host builds your tab → that tab is skipped, other plugins' tabs are
  unaffected.

None of the above ever produces a visible crash or error dialog; a broken
plugin is simply absent from the tab bar.

## 5. Contract version compatibility

The host currently supports **major version 1** of the `ITabPlugin`
contract (`PluginLoader.SupportedContractMajor`). Compatibility is
major-version-only (SemVer-style): a plugin declaring `contractVersion`
`"1.x.y"` (any minor/patch) is accepted; anything with a different major
(`"2.0.0"`, `"0.9.0"`, etc.) is rejected. A single-number version like `"1"`
is normalized to `"1.0"` before parsing.

The host will only make a breaking change to `ITabPlugin` alongside a major
version bump of the contract it supports — plan for `contractVersion` to
track that major number, not your plugin's own `version`.

## Manual verification (for testing your own plugin against a local build)

1. Build `Sbroglione.PluginContracts` (`dotnet build Sbroglione.PluginContracts`)
   and reference it from your plugin project (a local `ProjectReference` or a
   copy of the built DLL — there is no published package yet).
2. Build your plugin, then copy its main DLL, `Sbroglione.PluginContracts.dll`,
   and a `plugin.json` into `~/.config/Sbroglione/plugins/<plugin-id>/`.
3. Run `dotnet run --project Sbroglione.Desktop` and confirm your tab appears
   after the built-in tabs, with the right icon/text, and that its content
   works.
4. Add a visible side effect to `OnUnload()` (e.g. writing to a file) and
   confirm it runs when you close the app.
5. Test the failure modes: an incompatible `contractVersion`, and a missing
   `plugin.json` — in both cases the app should start normally with no crash
   and the tab simply absent.
