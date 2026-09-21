-- Synthetic schema with the evidence tables checked by the actual PostgreSQL drill.
-- This fixture tests the restore workflow, not EF's production migration chain.
CREATE TABLE "Incidents" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "AuditRecords" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "Postmortems" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "TimelineEvents" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "ServiceHealthSnapshots" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "OutboxMessages" (id integer PRIMARY KEY, tenant_id text NOT NULL);
CREATE TABLE "__EFMigrationsHistory" (id integer PRIMARY KEY);
INSERT INTO "Incidents" VALUES (1, 'alpha'), (2, 'beta');
INSERT INTO "AuditRecords" VALUES (1, 'alpha'), (2, 'beta');
INSERT INTO "Postmortems" VALUES (1, 'alpha');
INSERT INTO "TimelineEvents" VALUES (1, 'alpha');
INSERT INTO "ServiceHealthSnapshots" VALUES (1, 'alpha'), (2, 'beta');
INSERT INTO "OutboxMessages" VALUES (1, 'alpha');
INSERT INTO "__EFMigrationsHistory" VALUES (1);
