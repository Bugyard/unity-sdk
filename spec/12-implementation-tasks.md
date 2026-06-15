# 12 Implementation Tasks (Execution-Ready)

This is the build-ready task list derived from docs `08`, `09`, and `11`, with the stack locked.
Each task is a self-contained ticket: **scope**, **depends on**, and **acceptance criteria (AC)**.
Tasks are ordered so each one is unblocked by the time you reach it. Build one vertical slice first.

## Locked stack

| Concern | Choice | Notes |
|---|---|---|
| Runtime / API | Node + TypeScript + **Fastify** | `@fastify/multipart`, `@fastify/rate-limit`, `@fastify/cors` |
| DB / ORM | PostgreSQL + **Drizzle** | `drizzle-orm` + `drizzle-kit` migrations |
| Dashboard auth | **Better Auth** | email/password + sessions, Drizzle adapter, **organization plugin** |
| SDK auth | Custom project API keys | `by_pk_{env}_…`, hashed, prefix lookup — hand-rolled |
| Storage | S3-compatible | MinIO local → Cloudflare R2 / S3 prod, AWS SDK v3 |
| Validation | **Zod** | metadata schema + env schema |
| Logging | **pino** | secret redaction enabled |

### Auth ownership boundary (important)

Better Auth's **organization plugin** generates and owns these tables: `user`, `session`,
`account`, `verification`, `organization`, `member`, `invitation`. **Do not** hand-write
`users` / `organizations` / `organization_members` from doc `02` — let Better Auth own them.
Your domain tables reference Better Auth's IDs:

- `projects.organization_id` → `organization.id`
- `reports.organization_id` → `organization.id`
- `api_keys` / `reports` / `attachments` / `integrations` → your own Drizzle schema

Better Auth IDs are text/string by default. Keep your domain PKs as UUID, but type the
`organization_id` / user FK columns to match Better Auth's column type (text). Confirm the type
when you run the Better Auth schema generator and align FKs accordingly.

---

# Milestone 0 — Skeleton & tooling

**Definition of done:** server boots, `GET /health` → 200, missing env fails startup, Postgres + MinIO run locally.

## T01 — Initialize Fastify + TypeScript project
**Depends on:** —
- Scaffold with pnpm; scripts: `dev` (tsx watch), `build` (tsup/tsc), `start`, `lint`, `test` (vitest).
- ESLint + Prettier; strict `tsconfig`.
- Folder layout per doc `01` (`src/{config,db,modules,shared}`, `app.ts`, `server.ts`).
- **AC:** `pnpm dev` starts a Fastify server; `pnpm build` + `pnpm lint` pass.

## T02 — Env validation
**Depends on:** T01
- Zod schema in `src/config/env.ts` covering the groups in doc `10`: server, postgres, storage, security, limits, rate limits.
- Parse once at boot; export typed `env`.
- **AC:** a missing required var aborts startup with a clear message; `env` is fully typed.

## T03 — Logger, error handler, request context
**Depends on:** T01
- pino logger with redaction of: `DATABASE_URL`, API keys, signed URLs, Discord webhooks, GitHub tokens, JWT/session secrets (doc `06`/`10`).
- Central error handler → structured `{ error, message, details? }` (doc `03`).
- Request ID + per-request child logger.
- **AC:** thrown app errors return structured JSON; secrets never appear in logs.

## T04 — Health endpoint
**Depends on:** T01
- `GET /health` → `{ "status": "ok" }`.
- **AC:** returns 200; suitable for deploy health checks.

## T05 — Local infra (Docker Compose)
**Depends on:** —
- Compose with `postgres:16` + `minio` per doc `10`.
- Bucket bootstrap script (create `bugyard-dev` bucket, keep private).
- **AC:** `docker compose up` gives reachable Postgres + MinIO; bucket exists.

---

# Milestone 1 — Database foundation

**Definition of done:** migrations run on an empty DB, app connects, DB resets cleanly, dev seed works.

## T06 — Drizzle setup + connection
**Depends on:** T02, T05
- `pg` pool + `drizzle-orm` client in `src/db/client.ts`; `drizzle.config.ts` for `drizzle-kit`.
- **AC:** app connects to Postgres; `drizzle-kit` can generate/apply migrations.

## T07 — Better Auth setup (owns user/org/session tables)
**Depends on:** T06
- Install Better Auth; configure with Drizzle adapter, Postgres, secret + baseURL from env.
- Enable email/password and the **organization plugin**.
- Run Better Auth schema generator; commit generated Drizzle schema; create migration.
- **AC:** Better Auth tables (`user`, `session`, `account`, `verification`, `organization`, `member`, `invitation`) exist after migration; column types noted for FK alignment.

## T08 — Domain schema (Drizzle)
**Depends on:** T07
- Define remaining tables from doc `02`, FKs aligned to Better Auth IDs: `projects`, `api_keys`, `reports`, `attachments`, `integrations`, `integration_deliveries`.
- Add all indexes from doc `02` (reports by project/created_at/status/severity/build/scene; attachments by report; api_keys by prefix).
- Keep `UNIQUE(project_id, client_report_id)` and `UNIQUE(project_id, slug)`.
- **AC:** migration applies on empty DB; indexes + unique constraints present.

## T09 — Migration tooling + dev seed
**Depends on:** T08
- `db:migrate`, `db:reset`, `db:seed` scripts.
- Seed: one user, one organization (+membership via Better Auth), one project, one API key (print raw key once).
- **AC:** fresh DB → seed → usable org/project/key for manual testing.

---

# Milestone 2 — Dashboard auth wiring + Org/Project CRUD

**Definition of done:** a logged-in user can create a project under their org; cross-org access is blocked.

## T10 — Mount Better Auth on Fastify + session middleware
**Depends on:** T07, T03
- Mount Better Auth handler routes on Fastify.
- Session middleware resolves `request.user` + active `organizationId` from session; 401 when absent.
- **AC:** sign-up / sign-in / sign-out work; authenticated routes can read `request.user`.

## T11 — Authorization helpers
**Depends on:** T10
- Helpers enforcing the doc `06` chain: user∈org, org owns project, project owns report, report owns attachment.
- Reusable guards for dashboard routes.
- **AC:** requests with IDs outside the user's org return 403/404; never trust client IDs.

## T12 — Project CRUD
**Depends on:** T11
- `POST /projects`, `GET /projects`, `GET /projects/:projectId`, `PATCH /projects/:projectId` (doc `03`/`11`).
- Validate `engine` enum; enforce unique slug per org.
- Org membership is handled by Better Auth's org plugin endpoints; add thin wrappers only if needed.
- **AC:** create/list/get/patch projects scoped to the caller's org; slug collisions rejected.

---

# Milestone 3 — SDK API keys + ingestion auth

**Definition of done:** a project can mint an SDK key (shown once); a valid key authenticates ingestion, a revoked one does not.

## T13 — Create SDK API key
**Depends on:** T12
- `POST /projects/:projectId/api-keys`: generate `by_pk_{test|live}_…`, store `prefix` + `key_hash` (sha256 with `SDK_API_KEY_SECRET`), never the raw key.
- Return full key **once** in response (doc `03`).
- **AC:** key created; DB holds only hash + prefix; raw key returned exactly once.

## T14 — List / revoke API keys
**Depends on:** T13
- `GET /projects/:projectId/api-keys`, `DELETE /api-keys/:apiKeyId` (sets `revoked_at`, `is_active=false`).
- **AC:** keys listable (masked); revoked key flagged inactive.

## T15 — SDK auth middleware
**Depends on:** T13
- Bearer extract → prefix lookup → constant-time hash compare → check `is_active` → load project + org → attach `{ organizationId, projectId, environment, apiKeyId }` to request.
- Update `last_used_at` (throttled write is fine).
- **AC:** valid key authenticates; invalid/revoked rejected with `UNAUTHORIZED`; `last_used_at` updates.

## T16 — Temp auth-check endpoint
**Depends on:** T15
- `POST /v1/test-auth` → echoes resolved `{ organizationId, projectId, environment }` (doc `11`). Remove before release.
- **AC:** returns resolved context for a valid key.

---

# Milestone 4 — Report ingestion (JSON first) + read/update

**Definition of done:** create a report via curl/Postman; duplicates dedupe; list/detail/patch work. **First useful vertical slice.**

## T17 — Report metadata Zod schema
**Depends on:** T01
- Schema per doc `04`: required `clientReportId`, `report.title` (≤200), optional description (≤5000), severity/category enums, scene/build caps, nested `playerPosition`/`device`/`runtime`.
- Map nested metadata → normalized report columns + keep `raw_metadata_json`.
- **AC:** invalid metadata → `REQUEST_NOT_VALID` with `details[]` path/message.

## T18 — `POST /v1/reports` (JSON) + idempotency
**Depends on:** T15, T17, T08
- Validate, create `reports` row (normalized + raw json), set `organization_id`/`project_id` from auth context.
- Idempotency via `UNIQUE(project_id, client_report_id)`: duplicate → return existing with `status: "already_exists"`.
- Response includes `reportId`, `status`, `dashboardUrl`.
- **AC:** new report created; duplicate `clientReportId` returns existing, no second row.

## T19 — Report read/update API
**Depends on:** T18, T11
- `GET /projects/:projectId/reports` — cursor pagination + filters (status, severity, category, environment, buildVersion, sceneName, search), `hasScreenshot`/`hasLogs` flags.
- `GET /reports/:reportId` — detail + attachment list (doc `03`).
- `PATCH /reports/:reportId` — status/severity/category/title.
- All behind authorization helpers (T11).
- **AC:** can list (filtered + paginated), open detail, change status — all org-scoped.

---

# Milestone 5 — Storage, multipart, attachments

**Definition of done:** curl uploads metadata + screenshot + logs; files land in object storage; signed URLs work; bucket stays private.

## T20 — Storage service abstraction
**Depends on:** T02
- `StorageService` interface (doc `11`): `uploadObject`, `createSignedDownloadUrl`. S3 impl via AWS SDK v3 against MinIO.
- Key format: `organizations/{orgId}/projects/{projectId}/reports/{reportId}/{name}` (doc `05`).
- **AC:** can upload a test object and generate a working short-lived signed URL.

## T21 — Multipart parsing + file limits
**Depends on:** T01, T02
- `@fastify/multipart`; enforce per-field byte limits (metadata 256KB, screenshot 5MB, logs 2MB, events 512KB) and allowed MIME (`image/png`, `image/jpeg`, `text/plain`, `application/json`).
- Reject video; reject oversized early → `PAYLOAD_TOO_LARGE`.
- **AC:** oversized/disallowed files rejected with structured errors before storage.

## T22 — Upgrade `POST /v1/reports` to multipart
**Depends on:** T18, T20, T21
- Accept `metadata` (JSON string, required) + `screenshot`/`logs`/`events` (optional).
- Flow per doc `04`: auth → parse → validate → limits → create/reuse report → upload files → create `attachments` rows → return.
- Duplicate `clientReportId` does **not** re-upload files.
- Best-effort cleanup of uploaded objects if attachment-row write fails (full transactionality deferred).
- **AC:** the doc `11` milestone curl works end-to-end; attachment rows created; dedupe skips re-upload.

## T23 — Attachment list + signed download URL
**Depends on:** T22, T11
- `GET /reports/:reportId/attachments`, `GET /reports/:reportId/attachments/:attachmentId/download-url` → `{ url, expiresInSeconds: 300 }`.
- Verify report + attachment ownership; bucket never public.
- **AC:** screenshot/logs open only via temporary signed URLs that expire (300s).

---

# Milestone 6 — Integrations

**Definition of done:** new report posts to Discord; failures are recorded, not fatal; manual GitHub export works.

## T24 — Discord integration CRUD
**Depends on:** T12
- `POST /projects/:projectId/integrations/discord`, `GET /projects/:projectId/integrations`, `DELETE /integrations/:integrationId`.
- Store webhook URL as secret (encrypt at rest or treat as secret; never log).
- **AC:** integration created/listed/deleted, project-scoped; webhook never logged.

## T25 — Discord delivery on report creation
**Depends on:** T22, T24
- After report create, send Discord message (title, project, build, scene, severity, status, dashboard link — doc `07`).
- Write `integration_deliveries` row; on failure set `status=failed` + `last_error`; **never** fail report creation.
- MVP: synchronous or simple in-process; ≤3 retries.
- **AC:** new report → Discord message; forced failure → `failed` delivery row, report still created.

## T26 — GitHub manual issue export
**Depends on:** T24, T19
- `POST /reports/:reportId/export/github` (manual, not automatic) using a fine-grained PAT.
- Issue title/body per doc `07`; store issue URL in `integration_deliveries.external_url`.
- **AC:** action creates a GitHub issue; URL stored; no auto-export on ingestion.

---

# Milestone 7 — Hardening

**Definition of done:** safe enough for first external testers — rate limits, quotas, body limits, clean errors, basic monitoring + docs.

## T27 — Rate limiting
**Depends on:** T15
- `@fastify/rate-limit` on SDK ingestion: per-key 60/min, per-project 300/hour (doc `06`). Over limit → 429.
- **AC:** exceeding limits returns 429; dashboard routes unaffected.

## T28 — Monthly org report quota
**Depends on:** T18
- Enforce `monthly_report_limit` vs `current_period_report_count`; increment on create; over limit → `REPORT_LIMIT_EXCEEDED`.
- **AC:** free-plan overuse blocked with the documented error code.

## T29 — Request body limits + error-contract audit
**Depends on:** T03, T21
- Global request body cap; confirm every error path matches doc `03` shape and the documented codes.
- **AC:** oversized requests rejected; error responses consistent across endpoints.

## T30 — Storage cleanup, monitoring hooks, API docs
**Depends on:** T22
- Cleanup job/path for orphaned objects from failed uploads.
- Minimal monitoring (request metrics / error logging) + OpenAPI/Swagger for the documented endpoints.
- **AC:** orphan cleanup exists; endpoints documented; basic metrics emitted.

---

# Out-of-MVP-backend tracks (separate, after the slice)

- **Dashboard UI** (doc `11` §15) — login, project/API-key management, report list/detail, integrations.
- **Unity SDK** (doc `11` §16) — F8 hotkey → screenshot + logs + scene + position + build → `POST /v1/reports`.

These depend on a stable backend contract (through Milestone 5/6) and are intentionally deferred.

---

# Critical path (first milestone)

```
T01 → T02 → T06 → T07 → T08 → T13 → T15 → T17 → T18 → T20 → T21 → T22 → T23
```

Everything needed for the doc `11` milestone curl (report stored in PG, files in object storage,
visible via list/detail, private signed URLs) is on this path. T19 makes it browsable;
Better Auth wiring (T10–T12) is only required once a human needs to log in — it can run in
parallel and is **not** a blocker for the first ingestion slice.

# Dependency notes

- **Milestones 0–1** are foundational and mostly sequential.
- **Milestone 2 (dashboard auth)** and **Milestone 3 (SDK keys)** are independent of each other — parallelizable.
- **Milestone 4** needs Milestone 3 (SDK auth) but only the schema from Milestone 1.
- **Milestone 5** needs Milestone 4 + the storage service.
- **Milestones 6–7** layer on top and can be interleaved.
```
