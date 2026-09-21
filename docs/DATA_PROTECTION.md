# Data protection and disaster recovery — v0.7.0

## Scope and non-negotiable safeguards

IncidentLens incident timelines, audit rows, postmortems and action items, alert
idempotency keys, unprocessed outbox messages, and reliability signals are
**never deleted by v0.7.0 retention**. There is no incident purge endpoint and
no automatic cleanup job. Backups include every tenant in a single database;
**a PostgreSQL backup is NOT a tenant export** and must be access-controlled
accordingly. Legal holds and archival/export-before-deletion are planned
separately; do not describe these as delivered.

### Tenant-aware retention

The server's `/api/retention/policy`, `/api/retention/preview`, and
`POST /api/retention/run` endpoints require an authenticated `Commander` with
a valid tenant claim. Tenant identity is not accepted from the request body.
The preview reports all eligible rows; each execution removes at most
`MaxRowsPerRun` health snapshots and `MaxRowsPerRun` processed outbox messages,
using tenant and age predicates again on delete. Multiple runs may be needed.
The default `AllowPurge=false` prevents **all** deletion even after explicit
confirmation. Cleanup is manual, not scheduled.

Environment configuration (normal .NET double-underscore nesting):

```text
Retention__AllowPurge=true
Retention__HealthSnapshotsDays=90
Retention__ProcessedOutboxDays=30
Retention__MaxRowsPerRun=250
Retention__Tenants__acme__HealthSnapshotsDays=180
Retention__Tenants__acme__ProcessedOutboxDays=60
```

Limits: snapshots 30–3650 days, delivered outbox 7–3650 days, batch 1–5000.
Defaults: 90 / 30 / 250. Per-tenant day overrides are read from **trusted
server configuration only**; tenants cannot change policy through HTTP.

```bash
curl -H 'Authorization: Bearer <COMMANDER_JWT>' https://HOST/api/retention/preview
curl -X POST -H 'Authorization: Bearer <COMMANDER_JWT>' \
  -H 'Content-Type: application/json' \
  -d '{"confirm":"PURGE_ELIGIBLE_DATA"}' https://HOST/api/retention/run
```

Keep at least 30 days of health samples because the reliability dashboard
computes 30-day metrics. Preview first and back up before the first run;
configuring retention is a policy decision. The execution log records tenant
and row counts, not full affected row IDs. Do not treat this as an immutable
audit ledger for retention operations.

## PostgreSQL backup and restore

Use Python 3.11+ and PostgreSQL client binaries (`pg_dump`, `pg_restore`,
`psql`, `createdb`, `dropdb`) compatible with your server. Set `PGDATABASE`
**explicitly** and use `PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD` or, preferably,
a supported secret mechanism such as `PGPASSFILE`; do not pass passwords as
CLI arguments or commit them to Git. For remote connections, configure TLS
(`PGSSLMODE=verify-full` plus appropriate CA material). `PGDATABASE` must be
a plain database name, not a connection URL.

```bash
export PGDATABASE=incidentlens
export PGHOST=db.example.internal
export PGUSER=incidentlens_backup
export PGPASSFILE=/secure/path/pgpass
mkdir -p /secure/incidentlens-backups
chmod 700 /secure/incidentlens-backups
python3 scripts/recovery.py backup --output-dir /secure/incidentlens-backups
python3 scripts/recovery.py verify --manifest /secure/incidentlens-backups/<BACKUP>.json
```

Backup outputs a custom-format `.dump` and a SHA-256 manifest. The backup
operation checks the `pg_restore` catalog. Verification checks hash, size,
and catalog; **verification alone is not proof of a successful restore**.
Backups are NOT encrypted by this tool. Encrypt at rest/in transfer using
trusted infrastructure, restrict permissions, and keep an off-host/off-site
copy; configure backup file lifecycle and monitoring externally. A backup
contains every tenant and must be treated as sensitive customer data.

Restore only to an **existing empty** database, never onto the original:

```bash
createdb incidentlens_recovery
python3 scripts/recovery.py restore \
  --manifest /secure/incidentlens-backups/<BACKUP>.json \
  --target-database incidentlens_recovery \
  --confirm RESTORE:incidentlens_recovery
```

The restore checks the manifest and catalog, refuses the configured source
name, refuses a nonempty destination, and restores in a single transaction
with `--exit-on-error`. Validate identity, migrations, incidents, audit,
postmortems, tenant isolation, and API health **before** any traffic cutover.
Never point the production API at a drill database without a deliberate
incident response and access-control procedure. Any backup/restore operation
needs a credential with the relevant privileges; the routine API credential
may not be appropriate for backup administration.

## Recovery drill

On an isolated PostgreSQL environment with privileges to create/drop a
**disposable database**:

```bash
python3 scripts/recovery.py drill --output-dir /secure/incidentlens-backups
```

The drill performs backup → checksum/catalog verification → creates a unique
`incidentlens_drill_*` database → restore → compares counts of incidents,
audit rows, timelines, postmortems, health snapshots, outbox and EF migrations.
After success it drops only the database it created. On failure it preserves
that database for investigation. **The drill uses a current live snapshot**;
for production, prefer running in a dedicated backup/test environment with
carefully scoped backup access. Row counts are only an initial sanity check:
application-level recovery testing and tenant-isolation validation remain
required. Treat a real successful, timed drill as evidence, not the existence
of this script.

## RPO and RTO policy, not a guarantee

- Suggested initial RPO target: **24 hours**, assuming successful daily backups
  and off-host replication. If 24 hours of potential data loss is unacceptable,
  configure more frequent backups and PostgreSQL WAL archiving/PITR externally.
  These are **not** provisioned by v0.7.0.
- Suggested initial RTO target: **4 hours** to bring up a known-good replacement
  and validate it. Measure this in a drill, including credential access,
  retrieval, restore, migration, smoke tests, and DNS/traffic routing.
- Assign owners for backup scheduling, success alerting, retention of encrypted
  copies, recovery drills, and emergency authorizations. The repo cannot
  guarantee either objective without the deployment-specific operations.

**Upgrade from v0.6.0:** No EF schema migration is required for v0.7.0:
retention settings live in deployment configuration. First take and verify
an off-host backup; deploy API; verify `/api/retention/preview`; leave
`AllowPurge=false` until operations approves a cleanup policy. Preserve the
v0.6.0 `demo` tenant backfill review in `docs/TENANCY.md`.

### Windows PowerShell quick reference

With Python and PostgreSQL client binaries installed, set connection variables
in the current PowerShell session and choose an NTFS directory accessible only
to your backup operators. Windows directory ACLs must be configured separately;
POSIX `chmod` does not apply to NTFS.

```powershell
$env:PGDATABASE = "incidentlens"
$env:PGHOST = "db.example.internal"
$env:PGUSER = "incidentlens_backup"
$env:PGPASSFILE = "C:\\secure\\pgpass.conf"
$env:PGSSLMODE = "verify-full"
py -3 scripts/recovery.py backup --output-dir C:\\secure\\incidentlens-backups
py -3 scripts/recovery.py verify --manifest C:\\secure\\incidentlens-backups\\BACKUP.json
```

An archive SHA-256 in a sidecar manifest detects accidental corruption only;
an attacker able to change **both** files can recompute it. Protect the manifest
with the same restricted storage and, where appropriate, independently signed
or immutable storage controls.

### Recovery drill limitations

The GitHub Actions recovery-drill job uses a deliberately small synthetic
PostgreSQL schema containing seven critical table names. It exercises real
`pg_dump`/`pg_restore` binaries, catalog/hash validation, empty-target restore,
row-count comparison, and disposal of the test database. It is **not** a
full production-schema, real-data, OIDC, application, or time-to-recovery
acceptance test. Run and record a real environment drill before claiming the
RPO/RTO targets above. Schedule backup and test jobs in external infrastructure;
this version does not yet provide a scheduling service or PostgreSQL PITR.

The custom-format backup contains database objects and rows, not cluster-wide
roles, access credentials, cloud storage, uploaded files outside PostgreSQL,
or identity-provider configuration. Provision those separately in a recovery
plan before applying traffic to a restored environment.
