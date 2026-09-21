# IncidentLens v0.9.0: hardening and release-validation runbook

**Scope:** a SINGLE ASP.NET Core API instance backed by PostgreSQL, a separate
Angular frontend, an HTTPS edge and an external identity provider. This release
adds application defenses and reproducible tests; it does **not** certify any
particular installation. Its reference configuration is NOT horizontally scaled.

## 1. Rate limiting and request admission

The built-in ASP.NET Core fixed-window limiter operates **after authentication**
and before authorization. The quotas are per minute, with no queuing:

| `RateLimiting` setting | Default | Partition |
| --- | ---: | --- |
| `ReadPerMinute` | 600 | validated tenant claim, all users in that tenant |
| `WritePerMinute` | 120 | validated tenant claim, all users in that tenant |
| `AnonymousPerMinute` | 60 | direct connection IP |
| `RealtimeConnectPerMinute` | 60 | tenant or direct connection IP |
| `DemoAuthPerMinute` | 10 | direct connection IP (Development only) |

Configure via `RateLimiting__ReadPerMinute=600`, etc. Values must be integers
1..100000; invalid settings stop API startup. `GET`, `HEAD` and `OPTIONS` use
read admission; mutating methods use write admission. `/api/auth` and
`/hubs/incidents` have dedicated quotas; `/health`, `/health/live` and
`/health/ready` are exempt at the API so health probes still work when requests are
rejected. The supplied web nginx configuration separately limits public
readiness requests to protect the database from externally amplified probes. A 429 response contains `Retry-After` (seconds) and `Cache-Control:
no-store`. Treat HTTP 429 as an explicit signal to back off, not as an
incident mutation that should be replayed automatically without checking its
idempotency guarantees.

**Trust boundary:** the tenant key comes from the validated JWT claim, never
`X-Tenant-ID`. Anonymous keys use the server connection IP, never client
`X-Forwarded-For`; behind Compose/nginx or an ingress, anonymous requests may
share a proxy IP. Put additional abuse protection at the edge with trusted
forwarded-header configuration appropriate for your own ingress. These quotas
are process-local. Do not raise API replicas above one until shared quota
coordination, SignalR backplane and outbox row leasing have been added.

Kestrel's request-body limit is 1 MiB, independent of the edge's 2 MiB gate.
Incident API responses and 429s set `Cache-Control: no-store`; API responses
carry `X-Content-Type-Options: nosniff`. Client-provided `X-Correlation-ID`
is honored only when 1..64 ASCII alphanumeric, underscore or hyphen characters;
otherwise the server generates its own identifier.

## 2. Security checklist

1. Confirm the identity provider's **HTTPS** issuer, expected audience,
   tenant claim and role mapping. Reject absent/invalid tenant claims.
2. Validate Commander/Viewer permissions and cross-tenant isolation for reads,
   writes, evidence export, audit, service objectives and SignalR room joins.
3. Test anonymous and authenticated 429s; verify a noisy tenant does not
   consume a second tenant's read or write quota.
4. Confirm `X-Tenant-ID` and `X-Forwarded-For` do not change the server's
   trusted tenant identity or client-IP partition.
5. Keep demonstration JWT issuance, development keys and sample data OUT of
   production images. Review the legacy `demo` tenant migration before reuse.
6. Keep retention deletion disabled until backup/restore has been practiced;
   exercise the v0.7.0 recovery procedure before a major upgrade.
7. Run dependency checks in CI. `npm audit --omit=dev --audit-level=high`
   blocks high/critical production JavaScript advisories; the NuGet JSON
   audit gate fails on **any** reported direct/transitive advisory, or an
   unavailable/unparseable report. Review exception handling with a human;
   do not silently disable these checks to pass a release.
8. Scan published API/web container images with an approved vulnerability
   scanner in your deployment pipeline and review base image digests.
   Image CVE scanning is **not** supplied as a passing automated gate here.

## 3. Load-test and platform SLO methodology

The bounded standard-library tool issues **read-only** authenticated GETs to
`/api/incidents`, and reports throughput, p50/p95/p99 latency, status counts
and non-200 rate. It deliberately does not generate alerts or incidents. It
refuses remote hosts unless `--allow-remote` is explicitly supplied, refuses
plain HTTP with a remote bearer token, and never follows redirects with the
bearer token. Use ONLY a target you are authorized to load-test.

```bash
export INCIDENTLENS_TOKEN='short-lived-viewer-token'  # never commit this
python3 scripts/load_test.py --base-url http://127.0.0.1:8080 \
  --requests 100 --concurrency 5 --max-p95-ms 1000 \
  --max-error-percent 1 --output ./load-results.json
```

For an authorized remote staging environment, use an HTTPS URL plus
`--allow-remote`. Warm up first. Increase concurrency in bounded steps and
compare results across the SAME infrastructure and incident dataset. Ensure
a request budget sufficient for the test's intended rate; otherwise count
429s separately as expected admission rejection, not application failures.
The CLI treats all non-200 responses as errors to avoid accidentally passing
a benchmark consisting mainly of 429s. Never store access tokens in
benchmark artifacts.

**Proposed starting acceptance thresholds, not achieved measurements:** at
100 GETs/concurrency 5, p95 ≤ 1000 ms and non-200 ≤ 1%. For production,
establish separately observed targets for availability, mutation latency,
read latency, error ratios, outbox delivery delay and database recovery.
Set burn-rate alerting on collected telemetry and measure across a defined
window before claiming an achieved SLO. An idle workstation smoke test is
NOT a capacity assessment.

## 4. Resilience drills in a disposable staging environment

- **Database interruption:** temporarily stop the staging database. `/health/live`
  should remain healthy while `/health/ready` fails; restore PostgreSQL and
  confirm readiness and incident queries recover. Do not use the liveness
  endpoint to force pod restarts on database outages.
- **API restart after mutation:** create a disposable test incident, restart
  the single API replica, and confirm incident/audit/outbox persistence.
  Verify SignalR reconnects and reloads the authoritative incident record.
- **Outbox publish failure:** force a transient publisher failure; the row
  must stay pending, increment attempts and retry after backoff. A failed
  message must not starve ready messages farther down the queue. This is
  covered by `OutboxResilienceTests`, which still requires execution in CI.
- **Concurrent mutation:** two commanders updating the same version must not
  silently overwrite each other. One stale update must receive 409 Conflict
  and the latest incident. Existing workflow tests cover this behavior.
- **Tenant isolation:** use separate issued identities for two organizations,
  and repeat reads, writes, evidence and SignalR access checks.
- **Recovery:** perform a full backup + verified isolated restore and measure
  actual data loss/time-to-recover against your explicitly approved RPO/RTO.

Outbox semantics remain **at least once**: if publication succeeds but
persistence of `ProcessedAt` fails, a notification may be delivered again.
Consumers must tolerate duplicates; real incident state remains in PostgreSQL.
The local SQLite development path scans pending outbox rows in memory to
support DateTimeOffset comparisons; production PostgreSQL filters due rows
before sorting and limiting the batch. An additional single-API restriction
remains in force until a distributed lease is implemented.

## 5. Validation commands

```bash
python3 -m unittest discover -s tests/hardening -v
python3 -m unittest discover -s tests/recovery -v
python3 -m unittest discover -s tests/deployment -v

dotnet restore tests/IncidentLens.Api.Tests/IncidentLens.Api.Tests.csproj
dotnet test tests/IncidentLens.Api.Tests/IncidentLens.Api.Tests.csproj -c Release

cd web && npm ci && npm test -- --watch=false && npm run build
```

For Docker image builds, Terraform validation and the live PostgreSQL drill,
follow `docs/DEPLOYMENT.md` and `docs/DATA_PROTECTION.md`. A passing static
check does not establish a passing .NET build, container deployment, load
benchmark, security audit or recovery time. Store staging measurements and
run logs alongside the v1.0 release evidence.

**Known GA blocker:** production browser OIDC sign-in has not been integrated.
A backend configured with an external issuer can validate tokens, but the
production Angular operator portal still needs an end-to-end login/logout and
refresh flow before it can be called ready for general availability.
