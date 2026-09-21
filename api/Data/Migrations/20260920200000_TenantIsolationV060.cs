using IncidentLens.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentLens.Api.Data.Migrations;

/// <summary>Preserves all existing rows under the explicitly documented legacy demo tenant.</summary>
public partial class TenantIsolationV060 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "Incidents",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "TimelineEvents",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "Responders",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "Tags",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "AuditRecords",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "AlertIngestions",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "OutboxMessages",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ServiceObjectives",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ServiceHealthSnapshots",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "Postmortems",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "PostmortemActionItems",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "MaintenanceWindows",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ReliabilitySignals",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "demo");
        // The default above backfills legacy rows only. Never permit future
        // inserts to silently enter the legacy tenant through a DB default.
        migrationBuilder.Sql("ALTER TABLE \"Incidents\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"TimelineEvents\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"Responders\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"Tags\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"AuditRecords\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"AlertIngestions\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"OutboxMessages\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"ServiceObjectives\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"ServiceHealthSnapshots\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"Postmortems\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"PostmortemActionItems\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"MaintenanceWindows\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE \"ReliabilitySignals\" ALTER COLUMN \"TenantId\" DROP DEFAULT;");

        migrationBuilder.DropIndex(name: "IX_Incidents_Sequence", table: "Incidents");
        migrationBuilder.CreateIndex(name: "IX_Incidents_TenantId_Sequence",
            table: "Incidents", columns: new[] { "TenantId", "Sequence" }, unique: true);
        migrationBuilder.DropIndex(name: "IX_AlertIngestions_IdempotencyKey", table: "AlertIngestions");
        migrationBuilder.CreateIndex(name: "IX_AlertIngestions_TenantId_IdempotencyKey",
            table: "AlertIngestions", columns: new[] { "TenantId", "IdempotencyKey" }, unique: true);
        migrationBuilder.DropIndex(name: "IX_ServiceObjectives_Service", table: "ServiceObjectives");
        migrationBuilder.CreateIndex(name: "IX_ServiceObjectives_TenantId_Service",
            table: "ServiceObjectives", columns: new[] { "TenantId", "Service" }, unique: true);
        migrationBuilder.DropIndex(name: "IX_ReliabilitySignals_Fingerprint", table: "ReliabilitySignals");
        migrationBuilder.CreateIndex(name: "IX_ReliabilitySignals_TenantId_Fingerprint",
            table: "ReliabilitySignals", columns: new[] { "TenantId", "Fingerprint" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_ReliabilitySignals_TenantId_Fingerprint", table: "ReliabilitySignals");
        migrationBuilder.CreateIndex(name: "IX_ReliabilitySignals_Fingerprint", table: "ReliabilitySignals", column: "Fingerprint", unique: true);
        migrationBuilder.DropIndex(name: "IX_ServiceObjectives_TenantId_Service", table: "ServiceObjectives");
        migrationBuilder.CreateIndex(name: "IX_ServiceObjectives_Service", table: "ServiceObjectives", column: "Service", unique: true);
        migrationBuilder.DropIndex(name: "IX_AlertIngestions_TenantId_IdempotencyKey", table: "AlertIngestions");
        migrationBuilder.CreateIndex(name: "IX_AlertIngestions_IdempotencyKey", table: "AlertIngestions", column: "IdempotencyKey", unique: true);
        migrationBuilder.DropIndex(name: "IX_Incidents_TenantId_Sequence", table: "Incidents");
        migrationBuilder.CreateIndex(name: "IX_Incidents_Sequence", table: "Incidents", column: "Sequence", unique: true);
        migrationBuilder.DropColumn(name: "TenantId", table: "ReliabilitySignals");
        migrationBuilder.DropColumn(name: "TenantId", table: "MaintenanceWindows");
        migrationBuilder.DropColumn(name: "TenantId", table: "PostmortemActionItems");
        migrationBuilder.DropColumn(name: "TenantId", table: "Postmortems");
        migrationBuilder.DropColumn(name: "TenantId", table: "ServiceHealthSnapshots");
        migrationBuilder.DropColumn(name: "TenantId", table: "ServiceObjectives");
        migrationBuilder.DropColumn(name: "TenantId", table: "OutboxMessages");
        migrationBuilder.DropColumn(name: "TenantId", table: "AlertIngestions");
        migrationBuilder.DropColumn(name: "TenantId", table: "AuditRecords");
        migrationBuilder.DropColumn(name: "TenantId", table: "Tags");
        migrationBuilder.DropColumn(name: "TenantId", table: "Responders");
        migrationBuilder.DropColumn(name: "TenantId", table: "TimelineEvents");
        migrationBuilder.DropColumn(name: "TenantId", table: "Incidents");
    }
}
