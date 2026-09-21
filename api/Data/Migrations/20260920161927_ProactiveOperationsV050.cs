using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentLens.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProactiveOperationsV050 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "ServiceObjectives",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FastBurnThreshold",
                table: "ServiceObjectives",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 14m);

            migrationBuilder.AddColumn<string>(
                name: "OwnerTeam",
                table: "ServiceObjectives",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "Reliability Engineering");

            migrationBuilder.AddColumn<decimal>(
                name: "SlowBurnThreshold",
                table: "ServiceObjectives",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 6m);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "ServiceObjectives",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("""
                UPDATE "ServiceObjectives"
                SET "OwnerTeam" = CASE "Service"
                    WHEN 'Payments API' THEN 'Payments Platform'
                    WHEN 'Catalog Search' THEN 'Discovery Engineering'
                    WHEN 'Event Delivery' THEN 'Integration Platform'
                    WHEN 'Edge Gateway' THEN 'Traffic Engineering'
                    ELSE "OwnerTeam"
                END;
                """);

            migrationBuilder.CreateTable(
                name: "MaintenanceWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Service = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReliabilitySignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Service = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuggestedSeverity = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    EvaluationWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    BurnRate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ObservedAvailabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    TargetAvailabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuppressionReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReliabilitySignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReliabilitySignals_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceWindows_Service_StartsAt_EndsAt",
                table: "MaintenanceWindows",
                columns: new[] { "Service", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReliabilitySignals_Fingerprint",
                table: "ReliabilitySignals",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReliabilitySignals_IncidentId",
                table: "ReliabilitySignals",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliabilitySignals_Status_LastObservedAt",
                table: "ReliabilitySignals",
                columns: new[] { "Status", "LastObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceWindows");

            migrationBuilder.DropTable(
                name: "ReliabilitySignals");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "ServiceObjectives");

            migrationBuilder.DropColumn(
                name: "FastBurnThreshold",
                table: "ServiceObjectives");

            migrationBuilder.DropColumn(
                name: "OwnerTeam",
                table: "ServiceObjectives");

            migrationBuilder.DropColumn(
                name: "SlowBurnThreshold",
                table: "ServiceObjectives");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ServiceObjectives");
        }
    }
}
