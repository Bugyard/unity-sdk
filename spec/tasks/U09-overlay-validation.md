# U09 — Overlay: input validation + length caps

**Status:** ✅ done · **Milestone:** M2 — Overlay UX · **Depends on:** U08

## Scope
- Enforce backend caps client-side: title ≤200, description ≤5000; require non-empty title (Send already gated on title).
- Show inline messaging when a limit is hit instead of letting the backend reject with `REQUEST_NOT_VALID`.

## Acceptance criteria
- Over-length input is prevented/trimmed in the form.
- Send is blocked until required fields are valid.
