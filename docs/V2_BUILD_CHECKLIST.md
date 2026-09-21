# Windows validation checklist — v2.0.0 dashboard milestone 1

Extract this ZIP into a **new folder**. The previous v1.0.0 source on your PC
has local fixes that were not uploaded; compare before overwriting anything.

From the project root:

```powershell
dotnet --version
dotnet build .\api\IncidentLens.Api.csproj --configuration Release
dotnet test .\tests\IncidentLens.Api.Tests\IncidentLens.Api.Tests.csproj --configuration Release
```

In terminal 1:

```powershell
cd .\api
dotnet run --launch-profile http
```

In terminal 2, from the project root:

```powershell
cd .\web
node --version
npm ci
npm run check
npm start
```

Visit `http://localhost:4200` for the command center and
`http://localhost:4200/workspace` for the old full-function application.
The demo mode in `web/src/environments/environment.ts` is true by default;
for real API data, change it to false, restart `npm start`, and sign in.

Manual acceptance: cross-tenant dashboard isolation, reader access, incident
creation in workspace reflected after refresh, no anonymous API access, mobile
layout, error state, empty-incident display, and completed full .NET + Angular
test suites. Do not mark these accepted until run on your machine.
