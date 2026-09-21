# IncidentLens web client

Angular 22 standalone application for the IncidentLens command dashboard.

```bash
npm install
npm start
```

The default environment uses `DemoIncidentGateway`, so every visible workflow works without backend infrastructure. Set `demoMode` to `false` in `src/environments/environment.ts` to use the included ASP.NET Core API.

## Commands

| Command | Purpose |
| --- | --- |
| `npm start` | Development server at `http://localhost:4200` |
| `npm run test` | Run the Vitest unit suite once |
| `npm run build` | Create an optimized production build |
| `npm run check` | Run tests followed by the production build |
