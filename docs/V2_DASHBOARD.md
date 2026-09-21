# IncidentLens v2.0.0: dashboard implementation milestone 1

The root route `/` now displays the operations command center. The existing
incident-management application is preserved at `/workspace` (including its
incident dialogs, collaboration, SLO controls, and postmortems).

## Initial delivered scope

- New responsive dark Angular command-center shell with navigation and quick search.
- Five KPI tiles: active, critical, MTTA, MTTR, and services at risk.
- New tenant-scoped and role-protected `GET /api/dashboard/overview?days=7`.
- Twelve most recent active incident rows, with links to the full workspace.
- Small operational pulse and recent-declarations panels using real data.
- Demo adapter using the existing incident gateway; no hardcoded screenshot values.
- 30-second bounded polling, manual refresh, loading/empty/error presentation.
- Tenant-isolation integration test and demo adapter unit test.

The new endpoint reuses the established reliability analytics calculation. MTTA
and MTTR are drawn from the requested day window; open/critical totals cover
all unresolved incidents in the signed-in tenant. The row list is capped at 12.
No new database migration is required.

## Local development

Use .NET SDK 10 and the Node version required by `web/package.json`.

```powershell
dotnet test .\tests\IncidentLens.Api.Tests\IncidentLens.Api.Tests.csproj --configuration Release
cd .\api
dotnet run --launch-profile http
```

From a second terminal:

```powershell
cd .\web
npm ci
npm run check
npm start
```

`web/src/environments/environment.ts` defaults to self-contained demo mode.
Change `demoMode` to `false` to connect to the API at `http://localhost:5168/api`,
then use Development sign-in at `/login` before opening the command center.
Never enable demo mode in a production build.

## Remaining v2.0.0 scope

Full service-health sparklines; severity and historical trend charts; actual
live event feed; responder presence; postmortem action rollup; SignalR-driven
panel invalidation; full UI accessibility and responsive regression testing.
The first milestone deliberately does not fabricate event streams or responder
availability from incident declarations.

## Carry-forward corrections

The original supplied v1.0.0 ZIP predates the local fixes confirmed in the
Windows conversation. This package carries forward the lambda identifier,
EF migration attribute, missing test import, nullable pagination, runtime
rate-limit settings, demo-login test quota, and SQLite test-pool cleanup edits.
Your local validated source is the authority if you made additional changes
not present in the originally uploaded archive.
