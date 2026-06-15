# Diagnostic Snapshot — design & implementation plan

> **Decision:** Nahradit „raw memory dump" za **automatic diagnostic snapshot**.
> Ne raw memory dump, ne OS/process dump, ne Unity managed-heap dump (ten jen jako
> advanced dev-only, viz níž). Sbírat užitečný **game-state + runtime** snapshot zevnitř hry.

## Proč ne raw memory dump
- Unity managed heap ≠ native engine memory ≠ custom allocator hry — bez spolupráce hry stejně nevíš, co dumpnout.
- Raw dump může obsahovat tokeny, PII, save data, interní data → bezpečnostní riziko.
- Velké, pomalé, engine-specific, pro testera nepoužitelné.

## Architektura — hybrid (NE „všechno do jednoho zipu")

Dnešní SDK + backend posílají `screenshot` / `logs` / `events` / `save_state` jako
**samostatné typed attachmenty** a `context` v metadata. Dashboard je renderuje zvlášť.
**Tohle zachováváme** — jen dodáváme producenty. Bundlujeme jen to, co dnes nemá slot.

| Data | Kde skončí | Dnes | Akce |
|---|---|---|---|
| screenshot | `screenshot` attachment | ✅ | beze změny |
| recent logs | `logs` attachment (`player.log`) | ✅ ring buffer | beze změny |
| breadcrumbs | `events` attachment (`events.json`) | ⚠️ passthrough | **+ `Track()` producer** |
| game context | metadata `context` | ⚠️ jen per-Capture | **+ persistent `SetContext` store** |
| save state | `save_state` attachment | ⚠️ passthrough | **+ `RegisterSaveStateProvider`** |
| **runtime metrics + custom files** | **`diagnostic_snapshot.zip`** | ❌ | **NOVÝ slot (repurpose `memory_dump`)** |

### Co je uvnitř `diagnostic_snapshot.zip`
Jen věci bez prvotřídního slotu — aby dev nemusel rozzipovávat kvůli logu:
```
manifest.json          # sdk ver, build, scene, timestamp, co snapshot obsahuje
runtime_metrics.json   # ProfilerRecorder: alloc/reserved/GC mem, draw calls, tris, fps
custom/<name>          # výstupy z RegisterDiagnosticFileProvider (advanced)
```
ZIP, ne gzip — v C# je `System.IO.Compression.ZipArchive` jednodušší a drží víc souborů.
MIME `application/zip`.

## Nové public API (`Bugyard.cs`)

```csharp
// Persistent game context (vezme se aktuální stav při reportu → metadata.context)
Bugyard.SetContext(string key, object value);
Bugyard.RemoveContext(string key);
Bugyard.ClearContext();

// Breadcrumbs ring buffer (posledních ~200–500) → events.json
Bugyard.Track(string name, object payload = null);

// Dev hry dodá přesně ten save, který chce → save_state attachment
Bugyard.RegisterSaveStateProvider(Func<byte[]> provider); // + async varianta zvážit

// Advanced: libovolný custom diagnostic blob → diagnostic_snapshot.zip/custom/<name>
Bugyard.RegisterDiagnosticFileProvider(string name, Func<byte[]> provider);
```

Capture s automatickým snapshotem:
```csharp
Bugyard.Capture(new ReportInput {
    title = "Softlock after respawn",
    includeDiagnosticSnapshot = true,
});
```

Config:
```csharp
BugyardConfig.includeDiagnosticSnapshotByDefault = false; // on jen v dev buildech
BugyardConfig.maxDiagnosticSnapshotBytes = 25 * 1024 * 1024;
BugyardConfig.maxBreadcrumbs = 300;
BugyardConfig.maxContextBytes; // už existuje
```

## Overlay (F8) checkboxy
```
[x] Include screenshot      (default on)
[x] Include logs            (default on)
[x] Include game context    (default on)
[ ] Include save state      (default off)  -> jen když je registrován provider
[ ] Include diagnostic snapshot (default off; dev build on)
```
Pod snapshotem upozornění: *"May include game state or custom diagnostic data."*

## Unity built-ins, které použít (ne psát ručně)
- logs → `Application.logMessageReceived(Threaded)` ✅ už používáme
- runtime metrics → `Unity.Profiling.ProfilerRecorder` (alloc/reserved/GC mem, draw calls, tris)
- device/runtime → `SystemInfo`, `Application`, `Screen`, `QualitySettings` ✅ už používáme
- **advanced/dev-only:** `Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(...)` za
  `BugyardConfig.enableUnityMemoryProfilerSnapshot` → přibalit do `diagnostic_snapshot.zip/custom/`.
  Velký/pomalý/engine-specific → NIKDY default, jen explicitní dev opt-in.

Co Unity NEdá a musíme přes provider/SetContext: quest flags, inventory, checkpoint,
AI state, current wave, seed, save slot, match id — game-specific stav.

## Backend změny (malé)
- MIME allowlist: k `memory_dump`/novému slotu přidat `application/zip`.
- Pojmenování: v UI/SDK používat **`diagnostic_snapshot`**. DB enum může zůstat `memory_dump`
  (žádná migrace), NEBO čistší dlouhodobě:
  `screenshot | logs | events | save_state | diagnostic_snapshot | custom`.
- Limit: `MAX_DIAGNOSTIC_SNAPSHOT_BYTES` (sjednotit se SDK `maxDiagnosticSnapshotBytes`).
  > ⚠️ Pozor: SDK limit MUSÍ být ≤ backend limit, jinak SDK pošle blob, co backend odmítne 413.

## Implementační pořadí (fáze)
1. ✅ **Context store** — `SetContext/RemoveContext/ClearContext`, persistent v `BugyardRuntime`, merge s `ReportInput.context` při Capture (per-report vyhrává), bounded `maxContextBytes`. **HOTOVO.**
2. ✅ **Breadcrumbs** — `Track()` ring buffer (`maxBreadcrumbs` default 300), serialize → `events.json` (když caller nedodá vlastní `events`). Overlay/F8 je teď posílá automaticky. **HOTOVO.**
3. **Snapshot builder** — `ZipArchive`: `manifest.json` + `runtime_metrics.json` (ProfilerRecorder) + `custom/*`. Repurpose `memory_dump` slot → `diagnostic_snapshot.zip`, MIME `application/zip`.
4. **Save state provider** — `RegisterSaveStateProvider`, zavolat při Capture když `includeSaveState`.
5. **Diagnostic file provider** — `RegisterDiagnosticFileProvider` → `custom/*` v zipu.
6. **Overlay checkboxy** + defaults (dev build snapshot on).
7. **Backend** — `application/zip` MIME (+ příp. enum rename/migrace), limit env.
8. **Testy** — ✅ context merge + breadcrumb cap (Editmode); zbývá zip obsah/limit, provider volání, e2e proti backendu.

### Stav P0 (fáze 1–2)
Změněné/nové soubory:
- `Runtime/Bugyard.cs` — public `SetContext/RemoveContext/ClearContext/Track`
- `Runtime/BugyardRuntime.cs` — context store + breadcrumb buffer, zapojení do `CaptureRoutine`, úklid v `Teardown`
- `Runtime/Breadcrumbs.cs` — **nový** `BreadcrumbBuffer` (bounded FIFO, JSON array → `events.json`)
- `Runtime/ContextJson.cs` — `SerializeValue(object)` pro top-level array
- `Runtime/MetadataCollector.cs` — `Build(..., persistentContext)` merge
- `Runtime/BugyardConfig.cs` — `maxBreadcrumbs`
- `Tests/Editor/ContextMergeTests.cs`, `Tests/Editor/BreadcrumbBufferTests.cs` — **nové**

> ⚠️ Testy zatím neproběhly v Unity (tady není editor). Spustit Editmode testy v Unity
> Test Runneru před mergem.

## Kam P1/P2 zapadnou (file-level)

### P1 — save state provider → čistě SDK, **0 změn v backendu**
| Kus | Soubor |
|---|---|
| `RegisterSaveStateProvider(Func<byte[]>)` | `Runtime/Bugyard.cs` (vedle `SetContext`/`Track`) |
| uložení provideru + volání při Capture | `Runtime/BugyardRuntime.cs` — `_saveStateProvider`, artifacts blok v `CaptureRoutine`: `saveState = input.saveState ?? (include ? provider() : null)` |
| `includeSaveState` flag | `Runtime/ReportModels.cs` → `ReportInput` |
| overlay checkbox | `Runtime/BugyardRuntime.cs` → `DrawForm` + `Submit` |

Slot `save_state` na backendu už existuje (MIME `octet-stream`/`json`, enum, testy) → backend beze změny.

### P2 — diagnostic snapshot → SDK + **1 povinný řádek v backendu**
| Kus | Soubor |
|---|---|
| `RegisterDiagnosticFileProvider(name, Func<byte[]>)` | `Runtime/Bugyard.cs` |
| providery + ProfilerRecorder + zip build | `Runtime/BugyardRuntime.cs` |
| zip builder (manifest + runtime_metrics + custom/*) | **nový** `Runtime/DiagnosticSnapshot.cs` |
| repurpose `memoryDump` → `diagnosticSnapshot` | `Runtime/ReportModels.cs` (`ReportInput`/`ReportArtifacts`) |
| filename/MIME `memory_dump.gz`/gzip → `diagnostic_snapshot.zip`/zip | `Runtime/BugyardClient.cs` (`SendWire`, dnes ř. ~194–196) |
| **`application/zip` do MIME allowlistu** | `apps/backend/.../reports/report-upload.ts` (`memory_dump` allowlist) |

> ⚠️ **Cross-repo past:** backend dnes pro `memory_dump` povoluje jen
> `['application/gzip','application/octet-stream']`. Dokud se nepřidá `application/zip`,
> SDK pošle zip a **backend ho odmítne 415 až v runtime** (ne při kompilaci). MIME řádek
> v backendu a změna filename/MIME v `BugyardClient` musí jít **spolu**.

## Explicitně mimo scope (zatím)
- Raw memory/arena dump jako default
- OS/process dump
- Unity Memory Profiler snapshot jako default (jen advanced opt-in v kroku 3/custom)
- Async save provider (zvážit, ne nutné pro MVP)

## Definice hotovo
Tester dá F8 → zaškrtne snapshot → report v dashboardu má samostatné logs/events/context/save_state
+ `diagnostic_snapshot.zip` s runtime metrikami; dev viděl logy bez rozzipovávání.
