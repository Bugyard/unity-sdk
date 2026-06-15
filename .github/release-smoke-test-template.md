<!--
  Phase 4 release gate: copy this whole block into the release PR description (or
  the release notes) and fill it in. The release is not done until every cell
  passes. See docs/manual-smoke-test.md for the full procedure and rationale.
-->

## Release smoke-test record — vX.Y.Z

Tester(s): @your-handle · Date: YYYY-MM-DD

Per-cell procedure (tick each step as it passes):

| Unity | Input System | Platform | Install + sample | Hotkey opens overlay | Submit report | Send Test Report | Result |
|-------|--------------|----------|:---------------:|:--------------------:|:-------------:|:----------------:|--------|
| 2021.3 (floor) | absent  | _e.g. StandaloneWindows64_ | ☐ | ☐ | ☐ | ☐ | ☐ pass / ☐ fail |
| 2021.3 (floor) | present | _e.g. StandaloneWindows64_ | ☐ | ☐ | ☐ | ☐ | ☐ pass / ☐ fail |
| _current LTS_  | absent  | _e.g. StandaloneWindows64_ | ☐ | ☐ | ☐ | ☐ | ☐ pass / ☐ fail |
| _current LTS_  | present | _e.g. StandaloneWindows64_ | ☐ | ☐ | ☐ | ☐ | ☐ pass / ☐ fail |

Step legend (per cell):

1. **Install + sample** — clean project, install via Package Manager → *Add package
   from git URL*, then import the **Basic Usage** sample. Enters Play mode with no
   console errors.
2. **Hotkey opens overlay** — pressing the configured hotkey (default **F8**) opens
   the report overlay.
3. **Submit report** — fill in and submit a report from the overlay; submission
   reports success.
4. **Send Test Report** — **Tools → Bugyard → Send Test Report** confirms the
   auth + endpoint round-trip succeeds.

Notes / deviations:

- _Record any warnings, the exact LTS patch version tested, and the dashboard link
  for a delivered test report here._

Sign-off: ☐ All cells pass — cleared to tag `vX.Y.Z`.
