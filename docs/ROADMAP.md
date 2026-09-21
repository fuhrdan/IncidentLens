# IncidentLens roadmap

## Delivered: v0.6.0 — tenant boundaries and production auth policy

- Tenant-owned entities and query filters, including audit/evidence and reliability data
- Per-tenant unique incident numbers, objectives, alert keys, and signal fingerprints
- Missing/invalid tenant claims fail authorization; cross-tenant writes are blocked
- Tenant-scoped SignalR rooms with membership checks and tenant-specific outbox delivery
- PostgreSQL backfill migration (legacy data assigned to `demo`; review before production)
- Development-only demo seeding / local JWT issuance, production HTTPS OIDC requirement
- Production frontend build disables demo mode; configurable CORS
- Isolation regression tests and `docs/TENANCY.md` upgrade/runbook

## Delivered: v0.7.0 — data protection and recovery

- Tenant-aware, commander-only retention preview, explicit cleanup, and conservative protected evidence classes
- Deployment-controlled retention periods and bounded batches; deletion is OFF by default
- PostgreSQL custom-format backup with SHA-256 manifest, catalog verification, guarded empty-target restore and recovery drill (synthetic-schema CI drill; production drill still required)
- Sample RPO/RTO targets and detailed operations runbook; no claim of achieved recovery times until tested in a real environment

## Delivered: v0.8.0 — deployment

- Multi-stage Angular and .NET container builds, single-node Docker Compose with PostgreSQL and HTTPS edge
- Database-aware readiness, dependency-free liveness, controlled HTTPS redirection behind trusted ingress
- Single-API-instance Kubernetes manifests, ingress TLS, protected secret references and resource/probe configuration
- Terraform reference for existing Kubernetes clusters (namespace, workloads, services, ingress); external DB/secrets/certificates remain prerequisites
- Guarded upgrade and rollback runbook, manifest regression tests and container build CI jobs
- Browser OIDC sign-in was deferred to v1.0; the v0.8 deployment alone was not operator-ready

## Delivered: v0.9.0 — production hardening

- Per-tenant authenticated read/write budgets, anonymous and real-time connection guards; health endpoints bypass admission
- 429 with Retry-After, bounded request bodies, no-store API responses and sanitized correlation IDs
- Due-first PostgreSQL outbox dispatch, retry telemetry and failure/starvation regression tests
- Reproducible read-only load harness, explicit candidate SLO thresholds and staging resilience drill checklist
- Dependency advisory checks in CI and hardening runbook. Live benchmark results, container CVE scans, full .NET integration validation and an independent security review remain needed before GA

## v1.0.0 — GA source candidate (deployment requires live acceptance)

- Browser OIDC authorization-code/PKCE sign-in and local-only development sign-in
- Public, validated SPA configuration supplied by API; access tokens only held in memory
- Operator/admin/authentication runbooks and explicit release acceptance gates
- Preflight check for client ID and smoke-check harness for tenant-isolated access
- Live issuer, database recovery, container scans and performance validation remain required before declaring a deployed instance production-ready

## Beyond v1.0

- SignalR backplane and leased outbox processing for horizontal scale
- External alert-provider and issue-tracker integrations
- Scheduled evaluation workers and telemetry-provider rollups
