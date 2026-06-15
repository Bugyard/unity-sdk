# API reference

Everything lives in the `BugyardSDK` namespace. The public surface is the static
`Bugyard` class plus plain data types.

## `Bugyard`

| Member | Signature | Description |
|--------|-----------|-------------|
| `Init` | `void Init(BugyardConfig config)` | Initialize once at startup with a config asset. Logs an error on a null config and warns on duplicate init. |
| `Init` | `void Init(string apiKey, string endpoint = null)` | Convenience initializer for prototypes. `endpoint` defaults to `https://api.bugyard.com`. |
| `Open` | `void Open()` | Open the built-in report overlay. No-op if already open. Logs an error if not initialized. |
| `Capture` | `void Capture(ReportInput report, Action<SendResult> onResult = null)` | Capture and send a report headless, bypassing the overlay. The callback receives the typed upload result. |
| `RegisterSaveStateProvider` | `void RegisterSaveStateProvider(SaveStateProvider provider)` | Register a callback that produces the current save/game-state blob on demand; invoked during capture when save-state inclusion is enabled. Pass `null` to clear. |
| `UnregisterSaveStateProvider` | `void UnregisterSaveStateProvider()` | Remove a registered save-state provider. No-op if none set. |
| `RegisterDiagnosticFileProvider` | `void RegisterDiagnosticFileProvider(string name, DiagnosticFileProvider provider)` | Register a named producer of a custom file embedded as `custom/<name>` in the diagnostic snapshot. Registering the same `name` replaces the prior provider. |
| `UnregisterDiagnosticFileProvider` | `void UnregisterDiagnosticFileProvider(string name)` | Remove a diagnostic-file provider by name. No-op if absent. |
| `SetContext` | `void SetContext(string key, object value)` | Set a persistent context value merged into every report's `metadata.context`. Per-report `context` overrides matching keys. Thread-safe. |
| `RemoveContext` | `void RemoveContext(string key)` | Remove a persistent context key set via `SetContext`. No-op if absent. |
| `ClearContext` | `void ClearContext()` | Clear all persistent context set via `SetContext`. |
| `Track` | `void Track(string name, object payload = null)` | Record a gameplay breadcrumb. The most recent breadcrumbs (up to `maxBreadcrumbs`) are attached to the next report as `events.json`. Thread-safe. |
| `Shutdown` | `void Shutdown()` | Tear down the SDK, unhook the log handler, and destroy the runtime. Safe to call when not initialized; mainly useful for tests and reinitialization. |
| `IsInitialized` | `bool` (get) | True after `Init` and before `Shutdown`. |
| `IsOverlayOpen` | `bool` (get) | True while the report overlay is open, including the brief screenshot-capture frame. |
| `IsInputBlocked` | `bool` (get) | True while the overlay is swallowing gameplay input according to `blockGameplayInput`. Gate raw input polling on this flag. |

## Programmatic triggers

You do not have to rely on the hotkey. Once `Init` has run, drive the SDK from
your own button, debug menu, or gameplay code.

Open the built-in overlay:

```csharp
myReportButton.onClick.AddListener(Bugyard.Open);
```

Send a report without showing the overlay:

```csharp
Bugyard.Capture(new ReportInput
{
    title = "I got stuck behind the bridge",
    description = "Could not move after jumping near the bridge.",
    expectedResult = "The player should slide back onto walkable ground.",
    severity = Severity.High,
    category = "bug",
    reporter = new ReporterInfo { name = "QA Bot" },
});
```

Handle the result:

```csharp
var report = new ReportInput
{
    title = "I got stuck behind the bridge",
    severity = Severity.High,
};

Bugyard.Capture(report, result =>
{
    if (result.success)
        Debug.Log($"Filed {result.reportId}: {result.dashboardUrl}");
    else if (result.queuedForRetry)
        Debug.LogWarning($"Report queued: {result.message}");
    else
        Debug.LogWarning($"Report failed: {result.message}");
});
```

Send deeper diagnostics when you have them:

```csharp
Bugyard.Capture(new ReportInput
{
    title = "Quest state is blocked",
    severity = Severity.High,
    context = new Dictionary<string, object>
    {
        { "questId", "bridge_intro" },
        { "checkpoint", "desert_arena_entry" },
        { "inventory", new[] { "sword", "shield" } },
    },
    events = recentEventsJsonBytes,       // uploaded as events.json
    saveState = currentSaveBytes,         // uploaded as save_state.bin
    saveStateIsJson = false,
    includeDiagnosticSnapshot = true,     // SDK builds diagnostic_snapshot.zip
});
```

`context` is serialized into `metadata.context`. The binary attachments are added
as separate multipart files. Each is bounded by the matching config cap; oversized
context and binary attachments are dropped before upload.

## Save-state provider

Instead of passing `saveState` bytes on every report, register a provider once and
let the SDK pull the current save when a report is captured. The provider runs on the
main thread during capture, so keep it fast; return `SaveState.None` when there is
nothing to send.

```csharp
Bugyard.RegisterSaveStateProvider(() =>
    SaveState.Json(Encoding.UTF8.GetBytes(SaveSystem.SerializeCurrentSlot())));
```

Inclusion is opt-in per report: set `ReportInput.includeSaveState`, or flip
`BugyardConfig.includeSaveStateByDefault` to attach it by default. When a provider is
registered, the overlay shows an "Include save state" checkbox seeded from that
default. An explicit `ReportInput.saveState` always wins over the provider, and a
provider that throws degrades to a report without save state (logged, never fatal).

`SaveState` carries the bytes and an `isJson` flag; use `SaveState.Json(bytes)` or
`SaveState.Binary(bytes)` to construct one, or `SaveState.None` for nothing.

## Diagnostic snapshot

When a report is captured with the diagnostic snapshot included, the SDK builds a
`diagnostic_snapshot.zip` (uploaded with `application/zip`, riding the backend
`memory_dump` slot) containing:

- `manifest.json` — sdk/engine/build/environment/platform, active scene, capture
  time, and a listing of the zip's contents;
- `runtime_metrics.json` — memory and render counters sampled from
  `Unity.Profiling.ProfilerRecorder` (memory counters on all builds; draw-call /
  triangle / vertex counters in development builds and the editor);
- `custom/<name>` — one file per registered diagnostic-file provider.

Register providers for game-specific state Unity can't surface on its own:

```csharp
Bugyard.RegisterDiagnosticFileProvider("ai_state.json", () =>
    Encoding.UTF8.GetBytes(EnemyDirector.DumpStateJson()));
```

Inclusion is opt-in per report (`ReportInput.includeDiagnosticSnapshot`) or globally
(`BugyardConfig.includeDiagnosticSnapshotByDefault`, recommended on for dev builds);
the overlay shows an "Include diagnostic snapshot" checkbox seeded from that default.
A provider runs on the main thread during capture, so keep it fast and return
null/empty to contribute nothing; one that throws is logged and skipped. An explicit
`ReportInput.diagnosticSnapshot` (prebuilt zip bytes) overrides the SDK builder.

## Context and breadcrumbs

Build up reproduction state as the player progresses, so every report carries it
without you wiring it through each `Capture` call.

Persistent context is merged into every report's `metadata.context`. Set, replace,
and remove keys as game state changes:

```csharp
Bugyard.SetContext("checkpoint", "desert_arena_entry");
Bugyard.SetContext("inventory", new[] { "sword", "shield" });
Bugyard.RemoveContext("checkpoint");
Bugyard.ClearContext();
```

Values may nest (dictionaries/lists/primitives) and are bounded by
`BugyardConfig.maxContextBytes`. Per-report `ReportInput.context` overrides matching
keys. All four calls are safe from any thread.

Breadcrumbs record the sequence of actions leading to a bug. The most recent
breadcrumbs (up to `BugyardConfig.maxBreadcrumbs`, default 300) are serialized to the
next report's `events.json`:

```csharp
Bugyard.Track("StartedBossFight");
Bugyard.Track("LoadedCheckpoint", new Dictionary<string, object> { { "id", "arena_2" } });
```

The optional `payload` is serialized verbatim. `Track` is safe from any thread. An
explicit `ReportInput.events` takes precedence over the breadcrumb buffer for that
report.

## `ReportInput`

Caller-supplied content for `Bugyard.Capture(...)`. The overlay internally builds
the same kind of report from the user's form input.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `title` | `string` | `(no title)` | Short report summary. Empty or whitespace titles are replaced with `(no title)`. |
| `description` | `string` | `null` | Longer details or repro steps. |
| `expectedResult` | `string` | `null` | What the tester expected to happen. |
| `severity` | `Severity` | `Medium` | `Low`, `Medium`, `High`, or `Critical`. Sent to the backend in lowercase. |
| `category` | `string` | `config.defaultCategory` | Report category, for example `bug`, `crash`, or `feedback`. |
| `reporter` | `ReporterInfo` | `null` | Optional tester identity. Omitted from the payload when unset. |
| `playerPosition` | `Vector3?` | main camera position, else `Vector3.zero` | Override for the reported player position. |
| `context` | `Dictionary<string, object>` | `null` | Free-form app state serialized into `metadata.context`. Supports nested dictionaries, lists, strings, numbers, booleans, and nulls. |
| `events` | `byte[]` | `null` | Optional JSON attachment uploaded as `events.json` with `application/json`. |
| `saveState` | `byte[]` | `null` | Optional save/game-state attachment uploaded as `save_state.bin` by default. |
| `saveStateIsJson` | `bool` | `false` | When true, uploads `saveState` as `save_state.json` with `application/json`. |
| `includeSaveState` | `bool?` | `null` | Whether to invoke the registered save-state provider for this report. `null` defers to `config.includeSaveStateByDefault`. Ignored when `saveState` is set or no provider is registered. |
| `includeDiagnosticSnapshot` | `bool?` | `null` | Whether the SDK builds and attaches a diagnostic snapshot for this report. `null` defers to `config.includeDiagnosticSnapshotByDefault`. Ignored when `diagnosticSnapshot` is set. |
| `diagnosticSnapshot` | `byte[]` | `null` | Optional prebuilt zip uploaded as `diagnostic_snapshot.zip` with `application/zip`. Overrides the SDK's snapshot builder. |

`ReporterInfo` carries optional `id`, `name`, and `email` strings.

## `SendResult`

Outcome passed to the `Capture` callback and surfaced in the overlay.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Whether the report was accepted. |
| `httpStatus` | `long` | HTTP status of the final attempt, or `0` on a transport/network error. |
| `reportId` | `string` | Backend report id on success. |
| `status` | `string` | `created` or `already_exists` on success. |
| `dashboardUrl` | `string` | Link to the report in the dashboard on success. |
| `errorCode` | `string` | Backend error code on failure, for example `UNAUTHORIZED`. May be empty for transport errors. |
| `message` | `string` | Human-friendly message safe to show in UI. |
| `details` | `string` | Raw backend `details` field for logging/diagnostics. |
| `queuedForRetry` | `bool` | True when a transient failure was persisted to the offline queue and will be retried automatically later. |

## Initialization notes

- Call `Bugyard.Init(...)` once, before opening the overlay or sending reports.
- Duplicate `Init` calls are ignored with a warning.
- `Bugyard.Open()` and `Bugyard.Capture(...)` log an error and do nothing if the
  SDK is not initialized.
- `Bugyard.Shutdown()` is safe to call when not initialized and allows a later
  `Init` to start fresh.
- If your game polls raw `Input.GetKey(...)` or the new Input System directly,
  gate that code on `!Bugyard.IsInputBlocked` while the overlay is open.
