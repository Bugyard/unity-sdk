# Manual smoke test (release gate)

A short human pass before each release. It covers what automation cannot easily
assert: the overlay UX, the hotkey under a real input backend, and a live
auth + endpoint round-trip to the backend.

The automated [install verification](releasing.md#ci-gates) already proves the
package installs, compiles, and passes its test suites across the Unity-version
and Input-System matrix. This pass exists for the things a headless CI run can't
see — a key press opening a UI, a person filling in a form, and a report actually
arriving in the dashboard.

!!! note "When to run it"
    Run this immediately before tagging a release, after the pre-publish check is
    green (see [Releasing](releasing.md)). Record the results in the release PR /
    notes — the release is not done until every matrix cell passes.

## Procedure

Run these steps in **each** matrix cell below.

1. In a **clean Unity project**, open **Window → Package Manager → + → Add package
   from git URL…** and install this package.
2. Create a config asset via **Tools → Bugyard → Create Config Asset** and set its
   **API key** and **endpoint** (the backend base URL, no trailing `/v1`).
3. Import the **Basic Usage** sample from the package's page in Package Manager.
4. Enter **Play mode**.
5. Press the configured **hotkey** (default **F8**, set by `BugyardConfig.hotkey`)
   → the report overlay opens.
6. Fill in and submit a report → submission reports success.
7. Run **Tools → Bugyard → Send Test Report** → confirm the auth + endpoint
   round-trip succeeds (a success dialog, with a dashboard link when the backend
   returns one).

## Matrix

Run the procedure across the cross-product of:

- **Unity versions** — the declared floor **2021.3** and a current LTS. (These
  mirror the [CI install matrix](releasing.md#ci-gates).)
- **Input System** — present **and** absent. Mirrors the Phase 2.3 axis: the
  hotkey path differs between the legacy Input Manager and the new Input System,
  and only a real key press exercises it end to end.
- **At least one standalone platform target.**

That is four cells (2 Unity versions × 2 Input-System states), each on a
standalone target.

## Recording results

Copy the template below into the release PR description (or the release notes) and
fill it in. The raw source lives at
[`.github/release-smoke-test-template.md`](https://github.com/Bugyard/unity-sdk/blob/main/.github/release-smoke-test-template.md).

--8<-- ".github/release-smoke-test-template.md"

## Definition of done

- All steps pass on every matrix cell.
- Results are recorded in the release notes / PR using the template above.
