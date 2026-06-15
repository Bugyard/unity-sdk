# Basic Usage sample

This sample shows the minimum runtime integration:

- initialize Bugyard from a `BugyardConfig` asset,
- open the built-in overlay from code, and
- file a headless report with optional context.

## Setup

1. Create a config asset with **Tools -> Bugyard -> Create Config Asset**.
2. Select it and set:
   - `apiKey`: your project key, for example `by_pk_test_xxx`.
   - `endpoint`: your backend base URL, for example `https://api.bugyard.com`.
     Do not include `/v1`.
3. Add an empty GameObject to your first scene.
4. Attach `BugyardBootstrap`.
5. Assign the config asset to the `config` field.
6. Run **Tools -> Bugyard -> Send Test Report** to verify the backend connection.
7. Enter play mode and press **F8** to open the report overlay.

## Script examples

`BugyardBootstrap.cs` includes:

- `Awake()` -> `Bugyard.Init(config)`.
- `Update()` -> example `Bugyard.IsInputBlocked` guard for your own input code.
- `OpenReportOverlay()` -> open the built-in overlay from a custom button.
- `ReportStuckPlayer()` -> file a report headless with `Bugyard.Capture(...)`,
  including a small free-form `context` snapshot.
