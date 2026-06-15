# Bugyard Unity SDK

Capture screenshots, logs, scene name, player position, build version and device
info from your Unity playtests and send them to your
[Bugyard](https://github.com/bugyard/bugyard) dashboard — with one hotkey.

!!! warning "Status: alpha (0.1.x)"
    The API may change between minor versions. Intended for private alpha
    testing, not production.

<div class="grid cards" markdown>

-   :material-rocket-launch: **[Quick start](quick-start.md)**

    Install the package, create a config asset, and file your first report.

-   :material-code-braces: **[API reference](api-reference.md)**

    `Bugyard.Init` / `Open` / `Capture` and the data types they take.

-   :material-cog: **[Configuration](configuration.md)**

    Every field on the `BugyardConfig` asset.

-   :material-upload: **[What gets sent](what-gets-sent.md)**

    The exact payload, plus the offline/failure queue.

</div>

## What it does

A playtester presses **F8** in a Unity build, fills in a short form, and hits
**Send**. Each report bundles:

- a **screenshot** of the game frame (the overlay hides itself first),
- recent **console logs**,
- **scene name**, **player position**, **build/engine/SDK version**, and
- **device specs** and runtime info.

These are uploaded as a multipart `POST {endpoint}/v1/reports` and appear in your
Bugyard dashboard. You can also drive the SDK from your own UI or gameplay code —
see [Programmatic triggers](api-reference.md#programmatic-triggers).

## Concepts

| Piece | Role |
|-------|------|
| `BugyardConfig` | ScriptableObject holding API key, endpoint, hotkey and capture toggles. |
| `Bugyard` | Static entry point: `Init`, `Open`, `Capture`, `Shutdown`. |
| `BugyardRuntime` | Hidden MonoBehaviour created by `Init`; drives hotkey, log buffer, overlay, screenshot. |
| `BugyardClient` | Multipart upload with idempotent retries. |
| `MetadataCollector` | Builds the metadata payload from runtime state. |

## Requirements

- Unity **2021.3** or newer.
- The hotkey works under any **Active Input Handling** setting (Player Settings →
  Active Input Handling): the legacy Input Manager, the new Input System package,
  or **Both**. With no input backend wired up you can still call `Bugyard.Open()`
  yourself.

## License

MIT — see [`LICENSE`](https://github.com/bugyard/bugyard-unity/blob/main/LICENSE).
