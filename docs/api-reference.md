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
    memoryDump = gzippedMemoryDumpBytes,  // uploaded as memory_dump.gz
});
```

`context` is serialized into `metadata.context`. The binary attachments are added
as separate multipart files. Each is bounded by the matching config cap; oversized
context and binary attachments are dropped before upload.

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
| `memoryDump` | `byte[]` | `null` | Optional gzip attachment uploaded as `memory_dump.gz` with `application/gzip`. |

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
