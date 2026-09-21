# IncidentLens v1.0.0 — administrator handbook

## Installation and access

Read `docs/DEPLOYMENT.md`, `docs/AUTHENTICATION.md`, `docs/TENANCY.md`,
`docs/DATA_PROTECTION.md` and `docs/RELEASE_ACCEPTANCE.md` before changing any
production resource. Prerequisites: HTTPS domain/cert, external OIDC issuer,
public SPA registration with PKCE and token-endpoint CORS, PostgreSQL, protected
backups, and a Docker Compose host OR an existing Kubernetes cluster. Never
use the development token issuer in production. Validate legacy `demo` tenant
backfill from v0.6.0 before exposing historical data to real organizations.

- Configure OIDC audience, issuer, SPA client ID, tenant and role access-token
  claims and exact callback URI. Avoid creating a client secret for the SPA.
- Compose: set `.env` based on `deploy/.env.example`; install real TLS files;
  run `python3 scripts/preflight.py`, inspect `docker compose config` (secret
  values may be printed), build and deploy. The app and PostgreSQL are private
  services behind HTTPS edge.
- Kubernetes: use external DB and Secrets, ingress TLS and pinned images;
  provide `oidc-client-id` and `oidc-scope` in ConfigMap along with the issuer,
  audience and claim mapping. Do not apply Terraform AND raw YAML to the same
  resources. Keep API replicas=1 with Recreate upgrades.
- Configure storage, encryption, access controls and off-host backup retention
  with organizational policy. The included backup CLI does not encrypt files.

## Upgrades, recovery and retention

Schedule maintenance. Verify and store a backup off-host, complete a restore
into an **empty disposable target**, and record actual restore time and lost
transactions against your own approved RPO/RTO. Stop the old API before
applying migrations; `MigrateAsync` executes before HTTP startup. New images
should not run simultaneously against the database. Roll back an application
image only when schemas remain compatible; otherwise restore into a **new**
empty DB following `docs/DATA_PROTECTION.md` or apply a reviewed forward repair.
Never overwrite the original DB as an ad-hoc rollback.

Retention preview is per tenant, Commander-only. Purge is OFF by default and
applies only to documented disposable data in batches. Incident/audit/evidence
records are protected. Retention, backups and exports require their own access
and privacy policies. `docs/DATA_PROTECTION.md` contains exact CLI procedures.

## Monitoring and response

- `/health/live` checks process liveness only; `/health/ready` checks database.
- Monitor HTTP failures, rate-limit 429s, outbox retries, and traces. The
  pre-v1.0 hardening runbook details candidate latency and error thresholds.
- Use `scripts/load_test.py` only against authorized staging targets, honoring
  the per-tenant rate quota. This does not prove horizontal scalability.
- When an incident requires recovery, first preserve logs and backup evidence;
  record UTC times and command outcomes. Verify tenant isolation and a complete
  login after restore. Report findings against the GA acceptance checklist.

## Known architecture limits

One API instance, process-local presence/rate limits and in-process outbox
without distributed leases. Scheduled SLO evaluation, integrations and
multi-replica delivery are post-v1.0 work. OIDC logout ends the local session,
not necessarily the provider session; short-lived tokens are recommended.
