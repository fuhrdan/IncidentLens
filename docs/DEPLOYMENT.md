# IncidentLens v1.0.0 deployment and upgrade guide (includes historical steps)

## Read this first

This is a **single-API-instance** reference deployment, not certification of
production readiness. v0.6.0 tenant boundaries and v0.7.0 recovery utilities
remain intact. Production requires an **existing HTTPS OpenID Connect issuer**
that supplies `tenant_id` and `Viewer`/`Commander` role claims. The v1.0 Angular frontend uses a public OIDC SPA client with authorization code
and PKCE S256. Follow `docs/AUTHENTICATION.md`: register an exact callback
`https://YOUR_HOST/auth/callback`, token-endpoint CORS, a public SPA client ID
and API-audience access-token tenant/role claims. Live identity validation is
still required before declaring a deployed instance operator-ready.

The API has an in-process outbox worker and SignalR presence. Do not scale the
API past one replica; multiple instances require distributed leases and a
SignalR backplane. In Kubernetes, `Recreate` intentionally causes API downtime
on upgrades; these are not zero-downtime manifests.

## Health behavior

- `/health/live`: returns 200 after the process starts, no database check.
- `/health/ready`: queries the database and returns 503 if it is unhealthy.
- `/health`: backward-compatible database-aware health check.
- PostgreSQL migration is awaited **before** the HTTP listener starts; first
  launch may be slow. Kubernetes startupProbe permits 5 minutes, tune to a
  measured migration window. Liveness never triggers a restart merely because
  PostgreSQL is unavailable.

`ReverseProxy__RedirectToHttps=false` is ONLY allowed when the API listens on
an isolated internal network and the edge/ingress enforces TLS for *every*
external path. No API or database host ports are exposed by Docker Compose.

## Option A: Docker Compose on one host

Prerequisites: Docker Engine and Compose v2, HTTPS certificate and private key,
an accessible OIDC provider, open incoming ports 80/443, and enough disk space
for the database **and an independent protected backup**.

1. `cp deploy/.env.example .env`, `chmod 600 .env` (on Unix), and fill in
   `POSTGRES_PASSWORD`, `OIDC_AUTHORITY`, `OIDC_AUDIENCE`, `OIDC_CLIENT_ID`, and optionally
   `TENANT_CLAIM_TYPE` and `ROLE_CLAIM_TYPE` to match your validated JWT claims. The OIDC authority **must be HTTPS**. The Compose
   example interpolates the database password into a libpq connection string:
   avoid semicolons in the password, or quote and escape the entire connection
   string explicitly. Never commit `.env` or put database secrets in an image.
2. Install your real certificate into `deploy/certs/fullchain.pem` and private
   key into `deploy/certs/privkey.pem`. Restrict key permissions. The included
   `scripts/create-local-cert.sh` is only for short-lived localhost testing;
   browsers will not trust its self-signed certificate.
3. Review `docs/TENANCY.md` **before** starting against any existing database.
   The original v0.6 migration assigns historical rows to `demo`.
4. Run `python3 scripts/preflight.py` to reject template secrets and invalid TLS
   key pairs. It does not prove the certificate is trusted by browsers or the
   issuer is reachable. Then run `docker compose config` to inspect interpolation without committing
   its output (it may print passwords). Run `docker compose build` and
   `docker compose up -d`.
5. Check `docker compose ps`, `docker compose logs --no-color api`, and
   `curl -f https://YOUR_HOST/health/ready`. Confirm HTTP redirects to HTTPS,
   the web UI loads, unauthenticated `/api/incidents` is denied, and a valid
   tenant-scoped bearer token accesses only its tenant.
6. Set up encrypted, access-restricted **off-host** backups and test a restore.
   The Compose database volume is **not a backup**. `docs/DATA_PROTECTION.md`
   describes the guarded recovery CLI. For backup access without a public DB
   port, run the CLI from an authorized host attached to a secure DB tunnel,
   or use a protected `docker compose exec -T postgres pg_dump ...` workflow
   and store/verify the resulting archive away from the host.

The production image strips `appsettings.Development.json`. Do **not** set
`ASPNETCORE_ENVIRONMENT=Development` in a publicly accessible environment.
Retention deletion remains disabled by default.

## Option B: Kubernetes on an EXISTING cluster

Prerequisites: an ingress-nginx-compatible controller, DNS, an external
PostgreSQL database (with backups), an OIDC provider, `kubectl`, and pushed
container images. These manifests do **not** provision a Kubernetes cluster,
a PostgreSQL server, DNS, or a certificate. Change all `REPLACE_ME` image
names in `deploy/k8s/api.yaml` and `deploy/k8s/web.yaml`; pin digests for
production. Change the host in `deploy/k8s/ingress.yaml` and verify ingress
class `nginx` matches your controller.

```sh
kubectl apply -f deploy/k8s/namespace.yaml
cp deploy/k8s/config.example.yaml deploy/k8s/config.yaml
# Edit issuer and audience, then:
kubectl apply -f deploy/k8s/config.yaml
# Put ONLY the connection string in a protected local file, not CLI history:
kubectl -n incidentlens create secret generic incidentlens-secrets \
  --from-file=postgres-connection=/secure/path/postgres-connection
kubectl -n incidentlens create secret tls incidentlens-tls \
  --cert=/secure/path/fullchain.pem --key=/secure/path/privkey.pem
kubectl apply -f deploy/k8s/api.yaml -f deploy/k8s/web.yaml -f deploy/k8s/ingress.yaml
kubectl -n incidentlens rollout status deployment/api
kubectl -n incidentlens rollout status deployment/web
```

Kubernetes Secrets are not encrypted at rest unless your cluster is configured
for that. Secure access to etcd, backups, namespace permissions, and admission
policies. The API does not expose NodePort or LoadBalancer; use ingress TLS.

The included Terraform reference under `infra/terraform/kubernetes/` is an
**alternative** to applying raw Kubernetes manifests, not an additional layer
to apply over the same objects. It manages namespace, public OIDC config,
deployments, services and ingress on an existing cluster. It deliberately
references external Secrets instead of storing DB credentials in Terraform
state. To use it on a fresh namespace, edit/copy
`terraform.tfvars.example` to `terraform.tfvars`, run `terraform init`, then
`terraform apply -target=kubernetes_namespace_v1.incidentlens` to create the
namespace, create the DB/TLS secrets using `kubectl` as above, then run
`terraform apply` with edited `terraform.tfvars`. Targeted apply is ONLY the
one-time bootstrap of external Secrets; ordinary updates use a full plan/apply.
Do not apply both the YAML and Terraform approaches to the same objects.

## Safe upgrade from v0.7.0

1. Schedule a maintenance window and notify operators. Confirm the identity
   issuer, audience and immutable tenant/role claims before changing traffic.
2. Perform a restricted **off-host** PostgreSQL backup, SHA-256 validation and
   a disposable restore drill using `scripts/recovery.py`. Set an explicit RPO
   and RTO and record actual drill measurements, not merely targets.
3. Check v0.6 legacy-tenant ownership; do not expose `demo` data to real
   tenants by changing claims or skipping the migration review.
4. Generate/review the EF PostgreSQL migration script. v0.8 introduces **no
   new EF migration**, but earlier migrations may still be pending. Production
   startup performs `MigrateAsync`, so only start ONE API instance and do not
   deploy another version concurrently during migrations.
5. Rebuild/push versioned images; update Compose or Kubernetes/Terraform image
   references. Use Kubernetes `Recreate` to prevent simultaneous API versions.
6. Verify readiness, authorization, tenant isolation, SignalR reconnect and
   evidence export. Check logs for outbox/reliability evaluation errors.
7. If the new version fails, stop the API first. Application-image rollback
   does **not** roll back EF migrations. Restore the database into a new empty
   target per `docs/DATA_PROTECTION.md`, or use a reviewed forward repair.
   Never overwrite the production database as an ad-hoc rollback.

## Release validation and limitations

Run `python -m unittest discover -s tests/deployment -v`,
`python -m unittest discover -s tests/recovery -v`,
`npm ci && npm run check` in `web/`, and
`dotnet test tests/IncidentLens.Api.Tests/IncidentLens.Api.Tests.csproj`.
With tools installed, also run `docker compose config`, two image builds,
`terraform fmt -check`, `terraform validate`, and a real TLS/identity/database
smoke test. Manifest lint does not prove real cluster functionality.

**Historical note for v0.8.0:** rate limiting and browser OIDC were added in
v0.9/v1.0 respectively; container security review, real recovery/load
measurements, provider-managed certificates and distributed outbox/presence
remain deployment-specific or post-v1.0 work.

## Upgrade from v0.9.0 to v1.0.0

Back up and rehearse restoration. Register the OIDC public SPA with the exact
`https://YOUR_HOST/auth/callback` URI and CORS origin. Configure `OIDC_CLIENT_ID`
and public scope alongside the existing authority and audience; production now
rejects missing client IDs at startup. Kubernetes/Terraform ConfigMap requires
`oidc-client-id` and `oidc-scope`. No EF database migration is introduced in
v1.0. Upgrade the API and web together. Run the gates in
`docs/RELEASE_ACCEPTANCE.md` before traffic cutover. Do not call this ZIP an
independently certified production deployment.

## Upgrade from v0.8.0 to v0.9.0

No new EF migration is required. Back up and verify PostgreSQL first, then
replace the API/web images with the v0.9.0 tags or reviewed immutable digests.
Review the default `RateLimiting` configuration (including anonymous quotas
behind a reverse proxy), retain a single API replica, confirm the web nginx
readiness limit, and observe `incidentlens.http.rate_limit_rejections` and
outbox publish/retry counters after deployment. Run the authentication,
tenancy, outbox and load checks in `docs/HARDENING.md` before a traffic cutover.
Do not assert achievement of the starting SLO candidates until a real staging
benchmark and failure/recovery drill have been recorded. Browser OIDC sign-in is implemented in v1.0 but must be proven against the
selected provider in staging.
