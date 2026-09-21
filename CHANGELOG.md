# Changelog

## v2.0.1 — CI and Repository Maintenance

### Fixed
- Aligned the deployment version test with the application release version.
- Restored successful deployment validation in GitHub Actions.

### Maintenance
- Updated the application and telemetry versions to 2.0.1.
- Excluded generated Python cache files and local debugging logs.
- Removed a committed debugging log.
- Updated release documentation.

### Application behavior
- No incident-management or dashboard behavior changes.

## v2.0.0 — Operations Command Center (implementation milestone 1)

- New tenant-safe dashboard overview API, angular command-center route and initial KPI/incident panels.
- Existing incident workflow preserved at /workspace; see docs/V2_DASHBOARD.md.
- Reapplies fixes discovered during Windows .NET 10 build and test validation.

## v1.0.0 — general availability source candidate (2026-09-21)

- Added production browser OIDC public SPA authorization-code + PKCE/S256 flow,
  one-use state verifier and guarded callback route. Access tokens reside in
  memory only; no browser-console token paste or persistent bearer storage.
- Added API `/api/auth/client-config`, exposed without credentials and limited
  to public issuer, SPA client ID and scope. Production startup requires the
  HTTPS issuer, API audience and SPA client ID. The demo token issuer remains
  strictly Development-only.
- Added sign-in/sign-out UI, route guard, 401 sign-out handling, and access
  token forwarding only to first-party API/SignalR.
- Wired public SPA client ID/scope through Compose, Kubernetes and Terraform;
  tightened Compose preflight for client ID and refresh-token scope.
- Added operator/admin/authentication guides, release acceptance checklist,
  and non-mutating two-tenant staging smoke tool. Added API client-config
  integration tests, browser PKCE transaction regression tests, and offline
  release checks. No EF schema changes.
- Important: end-to-end OIDC, recovery timing, container security, and live
  load performance require deployment-specific validation before production
  sign-off.


All notable changes follow semantic versioning.

## [0.9.0] - 2026-09-20 — Production hardening

- Added fail-fast configurable tenant-wide read/write, anonymous, real-time and development-token admission quotas, with 429 + Retry-After, no queueing and health-probe bypass.
- Bounded Kestrel request bodies, disabled caching of API responses and rejected unsafe client correlation identifiers.
- Fixed transactional-outbox retry starvation: query due rows before batch limit on PostgreSQL, and added outbox delivery/retry metrics.
- Added rate-limit isolation and retry/starvation C# integration tests, plus a bounded, read-only authenticated load harness and offline safety tests.
- Added NuGet/production JavaScript advisory checks to CI, the v0.9 hardening runbook and proposed (not measured) SLO acceptance thresholds.
- Retained single-API replica deployment constraint and explicit GA blocker for production browser OIDC sign-in. No EF schema migration in this release.

## [0.8.0] - 2026-09-20 — Production deployment reference

- Added multi-stage .NET 10 and Angular 22 production Docker images; the API image strips development-only credentials.
- Replaced database-only Compose with private PostgreSQL/API/web networking, 80-to-443 edge redirect, TLS mount and real database readiness check.
- Separated liveness (`/health/live`) from database readiness (`/health/ready`); kept legacy `/health` behavior.
- Added single-API-instance Kubernetes Deployments, Services, TLS ingress, startup/readiness/liveness probes, resource bounds, and external Secret references.
- Added Terraform existing-cluster reference module, operator deployment/upgrade guide, and static deployment checks.
- Preserved tenant isolation and retention default-off. Browser OIDC login, real deployment validation and horizontal API scaling remain outstanding.

## [0.7.0] - 2026-09-20 — Data protection and disaster recovery

- Added tenant-scoped retention policy, preview and explicitly confirmed, commander-only cleanup for old health samples and processed outbox messages.
- Protected incident records, audit, postmortems, alert idempotency, pending outbox, and reliability evidence from retention deletion.
- Added restricted PostgreSQL custom backup, SHA-256 manifest validation, guarded empty-target restore and disposable recovery drill CLI.
- Documented example RPO/RTO targets, backup security, offline retention controls and real-drill requirements.
- Added retention isolation integration tests and offline recovery safety tests; no EF schema change in this version.

## [0.6.0] - 2026-09-20 — Production security and tenant isolation

- Added server-derived tenant identity to all 13 persisted entity types and fail-closed query filters.
- Added tenant-safe incident numbers, alert idempotency keys, objectives, and signal fingerprints.
- Added authenticated ownership checks for SignalR rooms and tenant-targeted outbox publishing.
- Added PostgreSQL backfill migration and explicit legacy-data upgrade warning.
- Required HTTPS OIDC authority, audience, and PostgreSQL in production; demo tokens and seed data are development only.
- Added production Angular environment, configurable CORS, and tenant isolation regression suite.
- Documented identity-provider claims, local SQLite rebuild caveat, and deferred UI OIDC login.

## [0.5.0] - 2026-09-20

### Added

- Versioned service-objective administration with owning teams and response targets
- Fast and slow SLO burn thresholds evaluated across 60-minute and 360-minute windows
- Deduplicated reliability signal inbox with severity suggestions and burn evidence
- Maintenance-window scheduling and automatic signal suppression
- Signal acknowledgement and controlled promotion into a standard audited incident
- Reliability evaluation, signal creation, and promotion OpenTelemetry counters
- PostgreSQL migration that safely backfills v0.4.0 service objectives
- API and Angular tests for objectives, evaluation, suppression, acknowledgement, promotion, and maintenance

### Changed

- The Angular dashboard now includes a responsive proactive-operations center
- Service objectives are configurable through both demo and HTTP transports
- OpenTelemetry service and meter versions now report 0.5.0

## [0.4.0] - 2026-09-20

### Added

- Reliability overview with MTTA, MTTR, recurrence, and customer-impact minutes
- Service objectives, health snapshots, SLO breaches, error-budget consumption, and seven-day trends
- Versioned postmortems with accountable owners and action-item lifecycle tracking
- JSON and CSV incident evidence packages containing audit and postmortem records
- OpenTelemetry traces, metrics, structured correlation, and optional OTLP export
- PostgreSQL migration for the operational-intelligence schema
- Integration and UI tests for analytics, correlation, postmortems, and evidence export

### Changed

- Status transitions now record acknowledgement and resolution timestamps
- Demo data now includes reliability objectives and representative service-health history
- The Angular dashboard now exposes operational analytics and learning workflows in the incident workspace

## [0.3.0] - 2026-09-20

### Added

- Authenticated SignalR incident rooms, presence, and reconnect handling
- Transactional outbox delivery with bounded exponential retries
- Idempotent alert ingestion using the `Idempotency-Key` header
- Structured `/status`, `/assign`, and `/note` commands with `@mentions`
- Paginated server-side queue filtering, including owning-team filters
- Integration coverage for presence, alert replay, outbox records, commands, and paging

### Changed

- Real-time notifications now invalidate and reload authoritative incident state
- Incident lists now return pagination metadata
- CI migration validation now covers the v0.3.0 PostgreSQL migration

## [0.2.0] - 2026-09-19

### Added

- Incident response-team roster with named roles
- Incident-command transfer and owning-team visibility
- Normalized incident tags with API validation
- Application-managed optimistic concurrency across SQLite and PostgreSQL
- Explicit `409 Conflict` response carrying the current incident
- Append-only audit records for every mutation
- PostgreSQL-first EF Core migration and local tool manifest
- External OIDC authority configuration with a Development-only local issuer
- Authenticated API integration tests covering RBAC, team workflow, audits, and stale writes
- Responsive team-workflow controls in the Angular dashboard

### Changed

- Production now defaults to PostgreSQL; Development remains zero-setup SQLite
- All mutation contracts now require `expectedVersion`
- CI validates Angular, API integration tests, and idempotent PostgreSQL migration scripts

## [0.1.0] - 2026-09-18

### Added

- Responsive Angular incident command dashboard
- Search, severity filter, status filter, and derived incident metrics
- Incident declaration, status changes, notes, and response timeline
- Demo gateway for a zero-infrastructure portfolio experience
- HTTP gateway for the included REST API
- ASP.NET Core minimal API with JWT role policies
- SQLite and configurable PostgreSQL persistence
- OpenAPI metadata, database health check, and problem details
- Angular unit tests, cross-stack CI, architecture notes, API documentation, and roadmap
