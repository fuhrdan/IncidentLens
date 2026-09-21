# API contract

Base URL: `http://localhost:5168/api`. `Viewer` reads incident, analytics, and reliability state; `Commander` manages incidents, objectives, signals, maintenance, postmortems, alerts, and evidence.

| Method | Route | Role | Purpose |
| --- | --- | --- | --- |
| `GET` | `/incidents` | Viewer | Filter and page the queue |
| `GET` | `/incidents/{id}` | Viewer | Read one incident |
| `POST` | `/incidents` | Commander | Declare an incident |
| `POST` | `/incidents/{id}/timeline` | Commander | Add a note or execute a command |
| `PATCH` | `/incidents/{id}/status` | Commander | Change status |
| `PATCH` | `/incidents/{id}/assignment` | Commander | Transfer command |
| `POST/DELETE` | `/incidents/{id}/responders` | Commander | Manage responders |
| `PUT` | `/incidents/{id}/tags` | Commander | Replace tags |
| `GET` | `/incidents/{id}/audit` | Commander | Read audit evidence |
| `POST` | `/alerts` | Commander | Ingest an idempotent alert |
| `GET` | `/analytics/overview?days=30` | Viewer | Read reliability and service-health analytics |
| `GET` | `/incidents/{id}/postmortem` | Viewer | Read the learning record |
| `PUT` | `/incidents/{id}/postmortem` | Commander | Create or update a versioned postmortem |
| `POST` | `/incidents/{id}/postmortem/actions` | Commander | Add an owned action item |
| `PATCH` | `/incidents/{id}/postmortem/actions/{actionId}` | Commander | Update action status, owner, or due date |
| `GET` | `/incidents/{id}/export?format=json\|csv` | Commander | Download incident evidence |
| `GET` | `/reliability/objectives` | Viewer | List service objectives and burn policy |
| `PUT` | `/reliability/objectives/{id}` | Commander | Update a versioned service objective |
| `GET/POST` | `/reliability/maintenance-windows` | Viewer/Commander | List or schedule planned suppression |
| `DELETE` | `/reliability/maintenance-windows/{id}` | Commander | Remove a versioned maintenance window |
| `GET` | `/reliability/signals` | Viewer | List or filter reliability signals |
| `POST` | `/reliability/evaluate` | Commander | Evaluate fast and slow SLO burn |
| `PATCH` | `/reliability/signals/{id}/acknowledge` | Commander | Acknowledge a versioned signal |
| `POST` | `/reliability/signals/{id}/promote` | Commander | Promote a signal into incident response |
| SignalR | `/hubs/incidents` | Viewer | Incident updates and presence |

Every response includes `X-Correlation-ID`. A caller may supply that header to preserve correlation across systems; otherwise the API uses the active trace ID.

## Queue filtering

`GET /incidents` accepts `page`, `pageSize`, `query`, `severity`, `status`, and `ownerTeam`. Page size is limited to 100. The response contains `items`, `total`, `page`, `pageSize`, and `totalPages`.

## Commands and concurrency

Every mutation submits `expectedVersion`. A stale write returns `409 Conflict` with the complete current incident. The timeline endpoint accepts ordinary notes or:

- `/status Monitoring`
- `/assign Elena Ortiz`
- `/note Waiting for @Maya`

Events return structured `command` and `mentions` fields. Invalid commands return a validation problem without changing the incident.

## Alert ingestion

Send a stable `Idempotency-Key` header with each delivery:

```json
{
  "title": "Regional synthetic checks failing",
  "summary": "Three probes report elevated connection failures.",
  "severity": "SEV-1",
  "service": "Edge Gateway",
  "ownerTeam": "Traffic Engineering",
  "assignee": "Dan Fuhr",
  "affectedCustomers": 82,
  "tags": ["synthetic", "edge"]
}
```

The first delivery returns `201` and `replayed: false`. A duplicate returns `200`, `replayed: true`, and the original incident without repeating timeline, audit, or outbox writes.

## SignalR and outbox

Call `JoinIncident("INC-1042")` after connecting. Clients receive `IncidentUpdated` invalidations and `PresenceChanged` room membership. Each mutation writes an outbox record in the same commit as the incident, timeline, and audit records. Failed publishes retain their error and retry with bounded exponential backoff.

## Analytics and postmortems

Analytics accepts a window from 1 to 365 days. It returns fleet reliability metrics plus each configured service's SLO target, current availability, error-budget consumption, incident/customer counts, breaches, health classification, and trend points.

Postmortem writes use a separate `expectedVersion`. A stale edit returns `409` with code `postmortem_version_conflict` and the current learning record. Every postmortem and action-item mutation also increments the parent incident, appends timeline/audit evidence, and emits an outbox event atomically.

## Reliability automation

Evaluation calculates error-budget burn as observed error percentage divided by the objective's allowed error percentage. It evaluates 60-minute fast-burn and 360-minute slow-burn windows. Breaches are deduplicated by service, window, and UTC hour.

An active maintenance window changes a detected signal to `Suppressed` and records the reason. Open signals may be acknowledged or promoted. Promotion opens a normal incident inside a database transaction and links the signal to its incident. Suppressed signals cannot be promoted. Objective, maintenance, and signal mutations use `expectedVersion`; stale writes return `reliability_version_conflict`.

## Data protection (v0.7.0)

All three endpoints require `IncidentCommander` and a valid tenant identity:

- `GET /api/retention/policy` — effective configuration and permanent evidence exclusions.
- `GET /api/retention/preview` — counts and cutoffs for the authenticated tenant.
- `POST /api/retention/run` — body `{ "confirm": "PURGE_ELIGIBLE_DATA" }`; 409 when disabled and 400 without exact confirmation.

Only expired health snapshots and successfully processed outbox rows are
eligible. The endpoint cannot delete incident or audit evidence. Operators
control policy through deployment configuration, **not** request input.

## Production request admission (v0.9.0)

Successful authentication is required before a tenant-scoped quota can apply.
Default per-minute budgets: 600 reads, 120 writes per validated tenant;
60 anonymous requests per direct connection IP; 60 real-time connects per
minute; 10 demo-auth requests per minute in Development. Settings can be
overridden with the `RateLimiting__*` environment configuration. On rejection,
expect `429 Too Many Requests`, `Retry-After` (seconds) and
`Cache-Control: no-store`. `/health`, `/health/live` and `/health/ready` are
exempt. See `docs/HARDENING.md` for validation and deployment boundaries.

## Public web authentication configuration (v1.0.0)

`GET /api/auth/client-config` is anonymous and returns only public `mode`,
`authority`, `clientId` and `scope`. In development with no external authority,
mode is `development`; production always requires HTTPS OIDC issuer, audience
and a public SPA client ID. The API DOES NOT exchange browser authorization
codes or issue production tokens; the SPA exchanges its code with the identity
provider using S256 PKCE and the provider's CORS-enabled token endpoint. All
incident endpoints continue to enforce JWT role and tenant policies.
