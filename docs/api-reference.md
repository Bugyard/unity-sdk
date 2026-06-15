# API reference

Everything lives in the `BugyardSDK` namespace. The whole public surface is the
static `Bugyard` class plus a few plain data types.

## `Bugyard`

| Member | Signature | Description |
|--------|-----------|-------------|
| `Init` | `void Init(BugyardConfig config)` | Initialize once at startup with a config asset. No-op (warns) if already initialized; logs an error on a null config. |
| `Init` | `void Init(string apiKey, string endpoint = null)` | Convenience initializer for prototyping — builds a config in memory. `endpoint` defaults to `https://api.bugyard.com`. |
| `Open` | `void Open()` | Open the report overlay (the same form the hotkey opens). No-op if already open. Logs an error if not initialized. |
| `Capture` | `void Capture(ReportInput report, Action<SendResult> onResult = null)` | Capture and send a report headless, bypassing the overlay. The optional callback receives the typed `SendResult` when the upload finishes. |
| `Shutdown` | `void Shutdown()` | Tear down the SDK: unhook the log handler and destroy the runtime. Safe to call when not initialized; a later `Init` starts fresh. Mainly for tests and re-initialization. |
| `IsInitialized` | `bool` (get) | True once `Init` has run and before `Shutdown`. |
| `IsOverlayOpen` | `bool` (get) | True while the report overlay is open (including the brief frame it hides itself to grab the screenshot). |
| `IsInputBlocked` | `bool` (get) | True while an open overlay is swallowing gameplay input (config `blockGameplayInput`). Gate your own raw `Input.GetKey(...)` / Input System polling on this so typing into the form doesn't drive the game. |

## Programmatic triggers

You don't have to rely on the hotkey. Once `Init` has run you can drive the SDK
from your own button, debug menu, or gameplay code.

**Open the overlay** — e.g. wired to a "Report a bug" button:

```csharp
// Show the same form the hotkey opens; the user fills it in and hits Send.
myReportButton.onClick.AddListener(Bugyard.Open);
```

**Send a report headless** — no overlay involved, useful for auto-filing or a
one-click reporter. Works whether or not the overlay is open:

```csharp
Bugyard.Capture(new ReportInput
{
    title = "I got stuck behind the bridge",
    description = "Could not move after jumping near the bridge.",
    severity = Severity.High,
    category = "bug",                       // optional; defaults to config.defaultCategory
    reporter = new ReporterInfo { name = "QA Bot" },  // optional
});
```

Pass a callback to learn the outcome (success carries `reportId`/`dashboardUrl`,
failure a friendly `message`):

```csharp
Bugyard.Capture(report, result =>
{
    if (result.success)
        Debug.Log($"Filed {result.reportId}: {result.dashboardUrl}");
    else
        Debug.LogWarning($"Report failed: {result.message}");
});
```

Both work under any input backend (or none) — see `Bugyard.IsInitialized`
and `Bugyard.IsOverlayOpen` if you need to gate your UI.

## `ReportInput`

Caller-supplied content for `Capture`. Only `title` is really needed; everything
else is optional.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `title` | `string` | — | Short summary of the report. |
| `description` | `string` | — | Longer details / repro steps. |
| `expectedResult` | `string` | — | What the tester expected to happen. |
| `severity` | `Severity` | `Medium` | `Low` / `Medium` / `High` / `Critical`. |
| `category` | `string` | `config.defaultCategory` | e.g. `bug`, `crash`, `feedback`. |
| `reporter` | `ReporterInfo` | `null` | Optional tester identity; omitted from the payload when unset. |
| `playerPosition` | `Vector3?` | main camera position | Override for the reported player position. |

`ReporterInfo` carries optional `id`, `name` and `email` strings.

## `SendResult`

Outcome passed to the `Capture` callback (and surfaced in the overlay).

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Whether the report was accepted. |
| `httpStatus` | `long` | HTTP status of the final attempt, or `0` on a transport/network error. |
| `reportId` | `string` | Backend report id (on success). |
| `status` | `string` | `"created"` or `"already_exists"` (on success). |
| `dashboardUrl` | `string` | Link to the report in the dashboard (on success). |
| `errorCode` | `string` | Backend error code, e.g. `UNAUTHORIZED` (on failure; may be empty). |
| `message` | `string` | Human-friendly message, safe to show in UI. |
| `details` | `string` | Raw `details` field from the error body, for logging/diagnostics. |
| `queuedForRetry` | `bool` | True when a transient failure was persisted to the offline queue and will be retried automatically later. |
