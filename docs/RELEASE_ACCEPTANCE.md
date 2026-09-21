# IncidentLens v1.0.0 — release acceptance record

**Status: source candidate; do not assert certified production readiness until
these checks pass against the intended production-like environment.**

## Automated local/CI gates

- `python -m unittest discover -s tests/deployment -v`
- `python -m unittest discover -s tests/hardening -v`
- `python -m unittest discover -s tests/recovery -v`
- `python -m unittest discover -s tests/release -v`
- `cd web && npm ci && npm run check` (Node 24.15+ or Node 22.22.3+)
- `dotnet test tests/IncidentLens.Api.Tests/IncidentLens.Api.Tests.csproj`
- `dotnet tool restore` and idempotent PostgreSQL EF migration script review
- `docker compose config`, two production Docker image builds and container
  vulnerability scans; `terraform fmt -check` and `terraform validate`.

## Staging release gates — record date, environment and evidence

| Check | Expected evidence | Recorded result |
| --- | --- | --- |
| Browser OIDC login | Real issuer, correct SPA redirect, token-endpoint CORS, PKCE S256, no console paste | NOT RUN |
| Identity negative tests | Wrong role, missing tenant, invalid audience/issuer and expired token denied | NOT RUN |
| Cross-tenant isolation | A/B accounts: list, get, mutate, audit/export, SignalR rooms | NOT RUN |
| API and Angular | .NET suite green, Angular tests/build green, no critical runtime errors | NOT RUN |
| Database migration | Reviewed script and backfill; no legacy `demo` ownership error | NOT RUN |
| Deployment | Real TLS and ingress; single API replica; readiness and realtime reconnect | NOT RUN |
| Backup and recovery | Off-host SHA-256 verified backup; disposable restore with measured RPO/RTO | NOT RUN |
| Resilience | Database stop/restart, outbox retry fairness, concurrent incident conflict | NOT RUN |
| Performance | Read-only authorized benchmark with recorded p95 and error rate | NOT RUN |
| Dependencies | npm, NuGet and published image vulnerability scan | NOT RUN |
| Human review | Security review, accessibility/operator walk-through and sign-off | NOT RUN |

You can run the non-mutating, post-deployment identity/tenant smoke script:
`INCIDENTLENS_TOKEN_A=... INCIDENTLENS_TOKEN_B=... INCIDENTLENS_INCIDENT_A=...
 python scripts/ga_smoke.py --base-url https://YOUR_HOST`.
The script requires **two different tenant accounts** and an incident that
belongs to tenant A. It rejects HTTP except loopback and refuses redirects to
avoid exposing bearer tokens. It does not simulate mutation or SignalR and is
**not** a replacement for the full manual/CI checks above.

Release sign-off must include operator name, run date, exact image digests,
config hash (not secrets), database migration revision, test outputs and
recovery measurements. A ZIP integrity check is NOT a build or deployment test.
