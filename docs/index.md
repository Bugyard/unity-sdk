# Bugyard Unity SDK

Bugyard Unity SDK lets playtesters file a rich bug report from inside a Unity
build. A tester presses the configured hotkey, fills in the overlay, and the SDK
uploads a screenshot, recent logs, scene/player metadata, build/runtime details,
and optional diagnostic context to your Bugyard backend.

!!! warning "Status: alpha (0.1.x)"
    The SDK is suitable for internal playtests and QA builds. Public APIs and UI
    may change between minor versions, and the built-in overlay is intentionally
    minimal. For live production builds, pin the package, review the payload, and
    gate initialization to the builds where you want report capture enabled.

<div class="grid cards" markdown>

-   :material-rocket-launch: **[Quick start](quick-start.md)**

    Install the package, create a config asset, verify connectivity, and file
    your first report.

-   :material-code-braces: **[API reference](api-reference.md)**

    `Bugyard.Init`, `Open`, `Capture`, result handling, and report data types.

-   :material-cog: **[Configuration](configuration.md)**

    Every field on the `BugyardConfig` asset, including payload caps and input
    behavior.

-   :material-upload: **[What gets sent](what-gets-sent.md)**

    The exact multipart payload, optional attachments, retry behavior, and
    offline queue.

</div>

## What you get

- Built-in report overlay opened by **F8** or `Bugyard.Open()`.
- Headless reporting through `Bugyard.Capture(...)` for custom UI, debug menus,
  and automated gameplay reports.
- Screenshot capture after the overlay hides itself, so the image shows the game.
- Recent Unity console logs with a bounded ring buffer.
- Automatic metadata: scene name, player position, build version, Unity version,
  SDK version, device specs, locale, timezone, and estimated FPS.
- Optional reporter identity, free-form `context`, `events.json`, `save_state`,
  and `memory_dump.gz` attachments for deeper diagnostics.
- Client-side payload limits, retry handling, idempotent `clientReportId`s, and an
  offline queue for transient failures.
- Editor tools for creating config assets, validating common mistakes, syncing
  package version, and sending a real connectivity test report.

## First install path

1. Add the package through Unity Package Manager with
   `https://github.com/Bugyard/unity-sdk.git`.
2. Create a config asset with **Tools -> Bugyard -> Create Config Asset**.
3. Set `apiKey` and `endpoint` in the Inspector.
4. Initialize once at startup with `Bugyard.Init(config)`.
5. Run **Tools -> Bugyard -> Send Test Report** to prove auth and endpoint
   connectivity before giving the build to testers.
6. Press **F8** in play mode or in a build to open the overlay.

See the [Quick start](quick-start.md) for the full walkthrough and troubleshooting
table.

## Production readiness

This package is alpha, so treat it as an internal playtest/QA integration unless
you intentionally expose it in a live build.

- Pin the package to a commit or release tag for reproducible CI and release
  builds.
- Commit only test keys (`by_pk_test_*`) or empty config assets. Inject live keys
  at runtime or keep live-key config assets out of source control.
- Review [what gets sent](what-gets-sent.md), disable screenshots/logs if needed,
  and tune payload caps for your privacy and disk-budget requirements.
- Decide which builds call `Bugyard.Init(...)`; many teams enable it only for
  development, staging, closed beta, or QA builds.
- Gate raw input polling on `!Bugyard.IsInputBlocked` while the overlay is open.

## Concepts

| Piece | Role |
|-------|------|
| `BugyardConfig` | ScriptableObject holding API key, endpoint, hotkey, capture toggles, payload caps, and queue settings. |
| `Bugyard` | Static entry point: `Init`, `Open`, `Capture`, `Shutdown`. |
| `BugyardRuntime` | Hidden MonoBehaviour created by `Init`; drives hotkey, log buffer, overlay, screenshot capture, and queue flush. |
| `BugyardClient` | Multipart upload with retries, size clamping, and idempotent `clientReportId`s. |
| `MetadataCollector` | Builds the metadata payload from runtime state and caller-supplied report input. |

## Requirements

- Unity **2021.3** or newer.
- The hotkey works under any **Active Input Handling** setting: legacy Input
  Manager, new Input System package, or **Both**. With no input backend wired up
  you can still call `Bugyard.Open()` yourself.

## License

MIT - see [`LICENSE`](https://github.com/Bugyard/unity-sdk/blob/main/LICENSE).
