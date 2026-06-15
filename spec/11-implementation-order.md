# 11 Implementation Order

This document defines the recommended backend implementation order for the Bugyard MVP.

The goal is to build one reliable vertical slice first:

```text
API key -> create report -> upload files -> view report -> open screenshot/logs
```

Do not start with dashboard UI, billing, Unity SDK, video, Jira, Linear, or enterprise permissions.

---

## 1. Backend skeleton

Goal: create a runnable backend with no product logic yet.

Implement:

- TypeScript project setup
- Fastify or NestJS server
- env validation
- logger
- error handler
- health endpoint

Endpoint:

```text
GET /health
```

Done when:

```text
Server starts locally.
GET /health returns 200 OK.
Missing required env variables fail startup.
```

---

## 2. Database foundation

Goal: migrations and DB connection work locally.

Implement tables:

- users
- organizations
- organization_members
- projects
- api_keys
- reports
- attachments
- integrations
- integration_deliveries

Done when:

```text
Migrations run on an empty database.
The app can connect to PostgreSQL.
The DB can be reset locally.
```

---

## 3. Organization and project CRUD

Goal: create the entities that reports belong to.

Implement:

```text
POST /organizations
GET /organizations/:organizationId

POST /projects
GET /projects
GET /projects/:projectId
PATCH /projects/:projectId
```

Done when:

```text
You can create an organization.
You can create a project inside that organization.
You can retrieve the project.
```

---

## 4. API key generation

Goal: allow game builds to authenticate against the ingestion API.

Implement:

```text
POST /projects/:projectId/api-keys
GET /projects/:projectId/api-keys
DELETE /api-keys/:apiKeyId
```

API key format:

```text
by_pk_test_xxxxxxxxxxxxxxxxxxxxxxxx
by_pk_live_xxxxxxxxxxxxxxxxxxxxxxxx
```

Store only:

```text
prefix
key_hash
project_id
environment
is_active
last_used_at
```

Done when:

```text
A project can generate an SDK API key.
The full key is shown only once.
Only the hash is stored in the database.
```

---

## 5. SDK auth middleware

Goal: authenticate ingestion requests using project API keys.

Implement middleware for SDK routes:

```http
Authorization: Bearer <sdk_api_key>
```

Middleware should resolve:

```text
organization_id
project_id
environment
api_key_id
```

Temporary test endpoint:

```text
POST /v1/test-auth
```

Response:

```json
{
  "organizationId": "...",
  "projectId": "...",
  "environment": "staging"
}
```

Done when:

```text
A valid SDK key authenticates successfully.
An invalid/revoked key is rejected.
last_used_at is updated.
```

---

## 6. JSON-only report ingestion

Goal: create reports before handling files.

Implement:

```text
POST /v1/reports
```

Initial content type:

```text
application/json
```

Request example:

```json
{
  "clientReportId": "7f917935-5052-4925-a74b-233e2d838030",
  "environment": "staging",
  "buildVersion": "0.4.12",
  "engine": "unity",
  "engineVersion": "6000.0.1",
  "sdkVersion": "0.1.0",
  "sceneName": "DesertArena",
  "playerPosition": {
    "x": 125,
    "y": 0,
    "z": -42
  },
  "report": {
    "title": "I got stuck behind the bridge",
    "description": "I could not move after jumping near the bridge.",
    "expectedResult": "I expected to walk away or jump out.",
    "severity": "high",
    "category": "bug"
  }
}
```

Add idempotency:

```text
UNIQUE(project_id, client_report_id)
```

Done when:

```text
A report can be created via curl/Postman.
Duplicate clientReportId returns the existing report instead of creating a duplicate.
```

---

## 7. Report read/update API

Goal: expose reports to the future dashboard.

Implement:

```text
GET /projects/:projectId/reports
GET /reports/:reportId
PATCH /reports/:reportId
```

PATCH should support:

```json
{
  "status": "in_progress"
}
```

Done when:

```text
You can create a report through /v1/reports.
You can list it through project reports.
You can open detail.
You can change status.
```

This is the first useful vertical slice.

---

## 8. Storage service abstraction

Goal: prepare file uploads without tying the whole app to one provider.

Implement interface:

```ts
interface StorageService {
  uploadObject(input: {
    key: string;
    body: Buffer | NodeJS.ReadableStream;
    contentType: string;
  }): Promise<{ bucket: string; key: string; sizeBytes: number }>;

  createSignedDownloadUrl(input: {
    key: string;
    expiresInSeconds: number;
  }): Promise<string>;
}
```

Start with one provider:

```text
Local MinIO/S3-compatible storage
```

Done when:

```text
Backend can upload a test file.
Backend can generate a temporary signed download URL.
```

---

## 9. Multipart report ingestion

Goal: support the real SDK payload.

Upgrade:

```text
POST /v1/reports
```

to accept:

```text
multipart/form-data
```

Fields:

```text
metadata: JSON string, required
screenshot: file, optional
logs: file, optional
events: JSON string/file, optional
```

Flow:

```text
1. Authenticate SDK API key
2. Parse multipart payload
3. Validate metadata
4. Enforce file size limits
5. Create or reuse report by clientReportId
6. Upload attachments to storage
7. Create attachment rows
8. Return reportId
```

Done when:

```text
curl can upload metadata + screenshot + logs.
Metadata is stored in PostgreSQL.
Files are stored in object storage.
Attachment rows are created.
```

---

## 10. Attachment API and signed URLs

Goal: dashboard can show screenshots and logs safely.

Implement:

```text
GET /reports/:reportId/attachments
GET /reports/:reportId/attachments/:attachmentId/download-url
```

Response:

```json
{
  "url": "https://temporary-signed-url"
}
```

Done when:

```text
Screenshot and logs can be opened through temporary signed URLs.
Storage objects remain private.
```

---

## 11. Discord integration

Goal: notify teams when a new report arrives.

Implement:

```text
POST /projects/:projectId/integrations/discord
GET /projects/:projectId/integrations
DELETE /integrations/:integrationId
```

On report creation:

```text
Send Discord webhook message.
```

Important rule:

```text
Do not fail report creation if Discord fails.
```

Done when:

```text
A new report creates a Discord notification.
A failed Discord notification is stored as failed delivery.
```

---

## 12. Integration delivery logs

Goal: make integrations debuggable.

Use table:

```text
integration_deliveries
```

Track:

```text
report_id
integration_id
status
attempt_count
last_error
external_url
created_at
updated_at
```

Done when:

```text
Successful and failed integration attempts are visible in the DB/API.
```

---

## 13. GitHub issue export

Goal: allow manual export of useful reports to GitHub Issues.

Implement:

```text
POST /reports/:reportId/export/github
```

Do not auto-create GitHub issues for every report.

Done when:

```text
A dashboard action can create a GitHub Issue from a report.
The GitHub issue URL is stored in integration_deliveries.external_url.
```

---

## 14. Rate limits and quotas

Goal: protect infrastructure costs.

Implement:

- per API key rate limit
- per project report rate limit
- monthly organization report quota
- max metadata size
- max screenshot size
- max logs size
- max events size

Suggested MVP limits:

```text
metadata: 256 KB
screenshot: 5 MB
logs: 2 MB
events: 512 KB
video: disabled
```

Done when:

```text
Oversized files are rejected.
Too many requests are rate limited.
Monthly quota can block free-plan overuse.
```

---

## 15. Dashboard MVP

Goal: build only the screens needed to inspect reports.

Implement screens:

- login
- organization/project selection
- project settings
- API keys
- report list
- report detail
- integrations

Report detail should show:

- title
- description
- expected result
- severity
- category
- status
- build version
- scene
- player position
- device/runtime metadata
- screenshot
- logs
- integration deliveries

Done when:

```text
A developer can create a project, copy SDK key, receive a report, inspect it, and change its status.
```

---

## 16. Unity SDK

Goal: integrate with the game only after backend contract is stable.

Implement SDK modules:

- Bugyard.Init
- API key config
- report hotkey
- overlay form
- screenshot capture
- Unity logs capture
- metadata collector
- upload client

Done when:

```text
Pressing F8 in a Unity build sends a report with screenshot, logs, scene name, player position and build version.
```

---

# Recommended first milestone

The first backend milestone is complete when this works:

```bash
curl -X POST http://localhost:3000/v1/reports \
  -H "Authorization: Bearer by_pk_test_xxx" \
  -F 'metadata={"clientReportId":"...","report":{"title":"I got stuck","severity":"high","category":"bug"}}' \
  -F 'screenshot=@screenshot.png' \
  -F 'logs=@player.log'
```

Expected result:

```text
Report metadata is stored in PostgreSQL.
Screenshot and logs are stored in object storage.
Report appears in GET /projects/:projectId/reports.
Report detail works through GET /reports/:reportId.
Screenshot/logs are accessible only through signed URLs.
```

---

# Final implementation order summary

```text
1. Backend skeleton
2. Database foundation
3. Organization and project CRUD
4. API key generation
5. SDK auth middleware
6. JSON-only report ingestion
7. Report read/update API
8. Storage service abstraction
9. Multipart report ingestion
10. Attachment API and signed URLs
11. Discord integration
12. Integration delivery logs
13. GitHub issue export
14. Rate limits and quotas
15. Dashboard MVP
16. Unity SDK
```
