# U11 — Overlay: input isolation while open

**Status:** ⬜ todo · **Milestone:** M2 — Overlay UX · **Depends on:** U08

## Scope
- Optionally block/consume gameplay input (and optionally pause `Time.timeScale`) while the overlay is open, restoring on close.

## Acceptance criteria
- Typing in the form does not leak to game controls.
- Original time scale restored on close/cancel.
