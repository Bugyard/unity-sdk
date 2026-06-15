# Diagnostic Snapshot — design & implementation plan

> **Decision:** Replace the "raw memory dump" with an **automatic diagnostic snapshot**.
> Not a raw memory dump, not an OS/process dump, not a Unity managed-heap dump (that
> only as an advanced dev-only option, see below). Collect a useful **game-state + runtime**
> snapshot from inside the game.

## Why not a raw memory dump
- Unity managed heap ≠ native engine memory ≠ the game's custom allocator — without the game's cooperation you don't know what to dump anyway.
- A raw dump can contain tokens, PII, save data, internal data → a security risk.
- Large, slow, engine-specific, and useless to a tester.

## Architecture — hybrid (NOT "everything into one zip")

Today's SDK + backend send `screenshot` / `logs` / `events` / `save_state` as
**separate typed attachments** and `context` in the metadata. The dashboard renders them
separately. **We keep this** — we only add producers. We bundle only what has no slot today.

| Data | Where it ends up | Today | Action |
|---|---|---|---|
| screenshot | `screenshot` attachment | ✅ | unchanged |
| recent logs | `logs` attachment (`player.log`) | ✅ ring buffer | unchanged |
| breadcrumbs | `events` attachment (`events.json`) | ⚠️ passthrough | **+ `Track()` producer** |
| game context | metadata `context` | ⚠️ per-Capture only | **+ persistent `SetContext` store** |
| save state | `save_state` attachment | ⚠️ passthrough | **+ `RegisterSaveStateProvider`** |
| **runtime metrics + custom files** | **`diagnostic_snapshot.zip`** | ❌ | **NEW slot (repurpose `memory_dump`)** |

### What's inside `diagnostic_snapshot.zip`
Only things without a first-class slot — so a dev doesn't have to unzip just to read a log:
```
manifest.json          # sdkVersion, engine(+Version), buildVersion, environment, platform, scene, capturedAtUtc, contents[]
runtime_metrics.json   # ProfilerRecorder: total/reserved/GC/system mem, drawCalls, setPassCalls, triangles, vertices
custom/<name>          # output from RegisterDiagnosticFileProvider (advanced)
```
ZIP, not gzip — in C# `System.IO.Compression.ZipArchive` is simpler and holds multiple files.
MIME `application/zip`.

## New public API (`Bugyard.cs`)

```csharp
// Persistent game context (its current state is taken at report time → metadata.context)
Bugyard.SetContext(string key, object value);
Bugyard.RemoveContext(string key);
Bugyard.ClearContext();

// Breadcrumbs ring buffer (the last ~200–500) → events.json
Bugyard.Track(string name, object payload = null);

// The game's dev supplies exactly the save they want → save_state attachment
Bugyard.RegisterSaveStateProvider(Func<byte[]> provider); // + consider an async variant

// Advanced: any custom diagnostic blob → diagnostic_snapshot.zip/custom/<name>
Bugyard.RegisterDiagnosticFileProvider(string name, Func<byte[]> provider);
```

Capture with an automatic snapshot:
```csharp
Bugyard.Capture(new ReportInput {
    title = "Softlock after respawn",
    includeDiagnosticSnapshot = true,
});
```

Config:
```csharp
BugyardConfig.includeDiagnosticSnapshotByDefault = false; // on only in dev builds
BugyardConfig.maxDiagnosticSnapshotBytes = 25 * 1024 * 1024;
BugyardConfig.maxBreadcrumbs = 300;
BugyardConfig.maxContextBytes; // already exists
```

## Overlay (F8) checkboxes
```
[x] Include screenshot      (default on)
[x] Include logs            (default on)
[x] Include game context    (default on)
[ ] Include save state      (default off)  -> only when a provider is registered
[ ] Include diagnostic snapshot (default off; dev build on)
```
Below the snapshot, a notice: *"May include game state or custom diagnostic data."*

## Unity built-ins to use (don't hand-write)
- logs → `Application.logMessageReceived(Threaded)` ✅ already used
- runtime metrics → `Unity.Profiling.ProfilerRecorder` (alloc/reserved/GC mem, draw calls, tris)
- device/runtime → `SystemInfo`, `Application`, `Screen`, `QualitySettings` ✅ already used
- **advanced/dev-only (NOT IMPLEMENTED, future proposal):** `Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(...)`
  behind a proposed `BugyardConfig.enableUnityMemoryProfilerSnapshot` → bundle into `diagnostic_snapshot.zip/custom/`.
  Large/slow/engine-specific → NEVER default, only explicit dev opt-in. Today the same can
  be covered via `RegisterDiagnosticFileProvider`. See "Explicitly out of scope" below.

What Unity does NOT give us and we must get via a provider/SetContext: quest flags, inventory,
checkpoint, AI state, current wave, seed, save slot, match id — game-specific state.

## Backend changes (small)
- MIME allowlist: add `application/zip` to the `memory_dump`/new slot.
- Naming: use **`diagnostic_snapshot`** in the UI/SDK. The DB enum can stay `memory_dump`
  (no migration), OR cleaner long-term:
  `screenshot | logs | events | save_state | diagnostic_snapshot | custom`.
- Limit: `MAX_DIAGNOSTIC_SNAPSHOT_BYTES` (keep it in sync with the SDK's `maxDiagnosticSnapshotBytes`).
  > ⚠️ Note: the SDK limit MUST be ≤ the backend limit, otherwise the SDK sends a blob the backend rejects with 413.

## Implementation order (phases)
1. ✅ **Context store** — `SetContext/RemoveContext/ClearContext`, persistent in `BugyardRuntime`, merged with `ReportInput.context` at Capture (per-report wins), bounded by `maxContextBytes`. **DONE.**
2. ✅ **Breadcrumbs** — `Track()` ring buffer (`maxBreadcrumbs` default 300), serialized → `events.json` (when the caller supplies no `events` of its own). The overlay/F8 now sends them automatically. **DONE.**
3. ✅ **Snapshot builder** — `ZipArchive`: `manifest.json` + `runtime_metrics.json` (ProfilerRecorder) + `custom/*`. Repurpose the `memory_dump` slot → `diagnostic_snapshot.zip`, MIME `application/zip`. **DONE** (`Runtime/DiagnosticSnapshot.cs`).
4. ✅ **Save state provider** — `RegisterSaveStateProvider`, called at Capture when `includeSaveState`. **DONE (P1)**.
5. ✅ **Diagnostic file provider** — `RegisterDiagnosticFileProvider(name, provider)` → `custom/*` in the zip. **DONE**.
6. ✅ **Overlay checkboxes** + defaults — "Include save state" (gated on a provider) + "Include diagnostic snapshot" (always), seeded from the config. **DONE**.
7. ✅ **Backend** — `application/zip` added to the `memory_dump` MIME allowlist + `application/zip → zip` extension mapping; the enum/slot stays `memory_dump` (no migration). The limit runs under the existing `MAX_MEMORY_DUMP_BYTES` (100 MB ≥ SDK 25 MB), a separate env variable isn't needed. **DONE**.
8. **Tests** — ✅ context merge + breadcrumb cap + save-state resolver (Editmode) + zip contents/sanitization/determinism (`DiagnosticSnapshotTests`) + multipart field/cap (`BugyardClientTests`). Remaining: provider invocation and ProfilerRecorder sampling are runtime/PlayMode (unverified without the Unity editor); e2e against the backend is blocked by a pre-existing 401 in `report-upload.test.ts` (missing test auth/DB in the sandbox).

### P0 status (phases 1–2)
Changed/new files:
- `Runtime/Bugyard.cs` — public `SetContext/RemoveContext/ClearContext/Track`
- `Runtime/BugyardRuntime.cs` — context store + breadcrumb buffer, wired into `CaptureRoutine`, cleaned up in `Teardown`
- `Runtime/Breadcrumbs.cs` — **new** `BreadcrumbBuffer` (bounded FIFO, JSON array → `events.json`)
- `Runtime/ContextJson.cs` — `SerializeValue(object)` for a top-level array
- `Runtime/MetadataCollector.cs` — `Build(..., persistentContext)` merge
- `Runtime/BugyardConfig.cs` — `maxBreadcrumbs`
- `Tests/Editor/ContextMergeTests.cs`, `Tests/Editor/BreadcrumbBufferTests.cs` — **new**

> ⚠️ The tests haven't run in Unity yet (no editor here). Run the Editmode tests in the Unity
> Test Runner before merging.

## Where P1/P2 fit (file-level)

### P1 — save state provider → purely SDK, **0 backend changes**
| Piece | File |
|---|---|
| `RegisterSaveStateProvider(Func<byte[]>)` | `Runtime/Bugyard.cs` (next to `SetContext`/`Track`) |
| store the provider + call it at Capture | `Runtime/BugyardRuntime.cs` — `_saveStateProvider`, artifacts block in `CaptureRoutine`: `saveState = input.saveState ?? (include ? provider() : null)` |
| `includeSaveState` flag | `Runtime/ReportModels.cs` → `ReportInput` |
| overlay checkbox | `Runtime/BugyardRuntime.cs` → `DrawForm` + `Submit` |

The `save_state` slot already exists on the backend (MIME `octet-stream`/`json`, enum, tests) → backend unchanged.

### P2 — diagnostic snapshot → SDK + **1 mandatory line in the backend**
| Piece | File |
|---|---|
| `RegisterDiagnosticFileProvider(name, Func<byte[]>)` | `Runtime/Bugyard.cs` |
| providers + ProfilerRecorder + zip build | `Runtime/BugyardRuntime.cs` |
| zip builder (manifest + runtime_metrics + custom/*) | **new** `Runtime/DiagnosticSnapshot.cs` |
| repurpose `memoryDump` → `diagnosticSnapshot` | `Runtime/ReportModels.cs` (`ReportInput`/`ReportArtifacts`) |
| filename/MIME `memory_dump.gz`/gzip → `diagnostic_snapshot.zip`/zip | `Runtime/BugyardClient.cs` (`SendWire`, currently ~lines 194–196) |
| **`application/zip` to the MIME allowlist** | `apps/backend/.../reports/report-upload.ts` (`memory_dump` allowlist) |

> ⚠️ **Cross-repo trap:** today the backend allows only
> `['application/gzip','application/octet-stream']` for `memory_dump`. Until `application/zip`
> is added, the SDK sends a zip and **the backend rejects it with 415 at runtime** (not at
> compile time). The MIME line in the backend and the filename/MIME change in `BugyardClient`
> must ship **together**.

## Explicitly out of scope (for now)
- Raw memory/arena dump as a default
- OS/process dump
- Unity Memory Profiler snapshot as a default (only an advanced opt-in in step 3/custom)
- Async save provider (consider it, not required for the MVP)

## Definition of done
A tester hits F8 → checks the snapshot → the report in the dashboard has separate logs/events/context/save_state
+ a `diagnostic_snapshot.zip` with runtime metrics; the dev could read the logs without unzipping.
