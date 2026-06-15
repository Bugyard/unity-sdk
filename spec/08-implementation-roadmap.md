# 08 Implementation Roadmap

## Phase 0: Project setup

Goal: runnable backend skeleton.

Tasks:

- Create TypeScript backend project.
- Add Fastify or NestJS.
- Add PostgreSQL.
- Add Prisma or Drizzle.
- Add env validation.
- Add health endpoint.
- Add basic logging.

Done when:

```text
GET /health returns 200 OK
DB connection works
migrations run locally
```

---

## Phase 1: Core domain

Goal: create organizations, projects and API keys.

Tasks:

- Add users table.
- Add organizations table.
- Add organization_members table.
- Add projects table.
- Add api_keys table.
- Implement project creation.
- Implement API key generation.
- Implement API key hashing.
- Implement SDK auth middleware.

Done when:

```text
You can create a project and generate an SDK key.
The raw SDK key is shown once.
SDK auth middleware can authenticate it.
```

---

## Phase 2: Report ingestion

Goal: receive bug reports from SDK/curl.

Tasks:

- Add reports table.
- Add attachments table.
- Implement metadata Zod schema.
- Implement multipart upload endpoint.
- Validate file sizes.
- Save report row.
- Upload screenshot/logs/events to storage.
- Save attachment rows.
- Add idempotency through clientReportId.

Done when:

```text
POST /v1/reports creates a report with screenshot and logs.
Duplicate clientReportId returns existing report.
```

---

## Phase 3: Dashboard report API

Goal: make reports visible to dashboard.

Tasks:

- Add user auth middleware.
- Add organization authorization checks.
- Implement report list endpoint.
- Implement report detail endpoint.
- Implement status update endpoint.
- Implement signed attachment download URL endpoint.

Done when:

```text
Dashboard can list reports, open detail, change status and view screenshot/logs.
```

---

## Phase 4: Discord integration

Goal: notify game team when new report arrives.

Tasks:

- Add integrations table.
- Add integration_deliveries table.
- Add create Discord integration endpoint.
- Send Discord webhook after report creation.
- Store delivery status.
- Do not fail report creation if webhook fails.

Done when:

```text
New report sends a Discord message with report link.
```

---

## Phase 5: GitHub issue export

Goal: allow manual export to GitHub Issues.

Tasks:

- Add GitHub integration config.
- Add endpoint to create GitHub issue from report.
- Store external issue URL.
- Show delivery status.

Done when:

```text
User can click Create GitHub Issue and get a linked issue.
```

---

## Phase 6: Hardening

Goal: make MVP safe enough for first external users.

Tasks:

- Add rate limiting.
- Add monthly report quotas.
- Add request body limits.
- Add storage cleanup for failed uploads.
- Add structured error responses.
- Add server-side logs.
- Add basic monitoring.
- Add API docs.

Done when:

```text
The backend can handle real testers without obvious abuse or data leaks.
```

---

## Phase 7: Unity SDK integration

Goal: connect real Unity plugin.

Tasks:

- Create sample Unity project.
- Send metadata.
- Send screenshot.
- Send logs.
- Generate clientReportId.
- Handle retry.
- Handle success/failure response.

Done when:

```text
Pressing F8 in Unity creates a report visible in dashboard.
```

---

# Suggested MVP release criteria

Release MVP only when:

- SDK key auth works.
- Report upload works.
- Screenshot/log storage works.
- Dashboard can show reports.
- Status update works.
- Discord notification works.
- Attachments are private.
- Signed URLs expire.
- File size limits exist.
- API keys can be revoked.
