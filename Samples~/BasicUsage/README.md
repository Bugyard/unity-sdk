# Basic Usage sample

1. Create a config asset: **Tools → BugCapture → Create Config Asset**.
2. Select it and set `apiKey` (e.g. `bc_pk_test_xxx`) and `endpoint`.
3. Add an empty GameObject to your first scene, attach `BugCaptureBootstrap`, and
   assign the config asset.
4. Enter play mode and press **F8** to open the report overlay.

To trigger reports from code instead of the hotkey (see `BugCaptureBootstrap.cs`):

- `OpenReportOverlay()` — open the built-in overlay from a custom button
  (`BugCapture.Open()`).
- `ReportStuckPlayer()` — file a report headless, with no overlay, and read the
  result (`BugCapture.Capture(...)`).
