# v0.6.0 tenant security and upgrade guide

## Boundary and identity

Every persisted incident, timeline event, responder, tag, audit record, alert receipt,
outbox message, service objective, service-health snapshot, maintenance window,
reliability signal, postmortem, and action item has a non-null `TenantId`.

**Only the validated JWT claim** `tenant_id` selects a tenant. The API disregards
`X-Tenant-ID`, query strings, and payload tenant fields. The configured OIDC
issuer must issue a stable, immutable organization identifier for every user,
and must issue a suitable `Viewer` or `Commander` role. `TenantClaimType` and
`RoleClaimType` are configurable for providers using different claim names.
Do not permit end users to edit their tenant claim in your identity provider.

All API policies require a valid tenant and role. EF Core global filters apply
to *all* tenant-owned records, including direct child queries, exports, audit,
analytics, objectives, snapshots, and signals. SaveChanges fills missing tenant
IDs and refuses to modify records belonging to another tenant. The outbox
worker reads across tenants but publishes each event only to its own
`tenant:<id>:incident:<id>` SignalR room; joining requires that the incident
exist in the authenticated user's tenant.

This is application-level isolation. Database row-level security and stronger
FK tenant integrity are valuable additional defense-in-depth for a later release.

## Existing PostgreSQL database upgrade

1. Back up the database *before* applying the migration. Do not upgrade a
   shared multi-tenant database from a version that had no tenant identity.
2. The migration `20260920200000_TenantIsolationV060` explicitly assigns all
   pre-v0.6.0 rows to tenant `demo`. If those rows are real production records,
   decide their correct organization first, update the migration's `defaultValue`
   for **all 13 tables**, then review the generated SQL before applying it.
   Do not expose real legacy data to tokens with tenant `demo`.
3. Apply reviewed migrations via `dotnet ef database update --project api
   --startup-project api` with `ConnectionStrings__PostgreSql` set for the
   intended PostgreSQL database. EF scripting uses an isolated design-time
   context so production startup does not require OIDC settings at design time.
   The API also applies
   migrations on startup, but a controlled migration window is preferred.
4. Validate counts and relational ownership after migration. Tenant identifiers
   are immutable application-level values, not display names.
5. For existing local SQLite demo databases only, delete `incidentlens.db` before
   the first v0.6.0 Development launch so EnsureCreated can create the new
   schema; this **destroys local data**. Back up or export anything needed first.

## Production configuration

`ASPNETCORE_ENVIRONMENT=Production` requires PostgreSQL, HTTPS
`Authentication__Authority`, and `Authentication__Audience`. It does not mount
a development token endpoint, seed demo incidents, or accept a local HMAC token.
Never deploy with `ASPNETCORE_ENVIRONMENT=Development` on a public host.

Example application settings supplied by your deployment's secret/config manager:

```text
Database__Provider=PostgreSql
ConnectionStrings__PostgreSql=Host=<host>;Database=incidentlens;Username=<user>;Password=<secret>
Authentication__Authority=https://<identity-provider>/
Authentication__Audience=incidentlens-api
Authentication__TenantClaimType=tenant_id
Authentication__RoleClaimType=http://schemas.microsoft.com/ws/2008/06/identity/claims/role
Cors__AllowedOrigins__0=https://<incidentlens-ui-host>
```

The frontend production build (`npm run build`) uses relative `/api` and the
real API gateway, with no embedded demo auth or in-memory incident data.
Serve the frontend and API from the same origin or configure a reverse proxy;
set allowed origins explicitly for separate origins. **The frontend does not
implement a browser OIDC login flow yet**: supply a valid token to the
existing session-token gateway, or use a trusted frontend integration before
releasing to operators.

## Validation

Run `dotnet test tests/IncidentLens.Api.Tests/IncidentLens.Api.Tests.csproj`
and `npm ci && npm run check && npm run build` from `web/`. The tenant regression
suite checks missing tenant claims, forged headers, reads, writes, child
records, evidence exports, independent alert keys, and database write guards.

## Known limitation

Single-instance SignalR presence and the non-leased outbox remain the v1+ scale
work. Tenant filtering is defense-in-depth but is not proof of complete
production security without a live OIDC configuration, database migration
rehearsal, and a real penetration test.
