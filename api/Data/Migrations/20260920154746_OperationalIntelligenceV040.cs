using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentLens.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationalIntelligenceV040 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Postmortems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Owner = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RootCause = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Detection = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LessonsLearned = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postmortems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Postmortems_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceHealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailabilityPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    ErrorRatePercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    LatencyP95Milliseconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceHealthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceObjectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AvailabilityTargetPercent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    AcknowledgementTargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ResolutionTargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    MonthlyErrorBudgetMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceObjectives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostmortemActionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostmortemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Owner = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostmortemActionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostmortemActionItems_Postmortems_PostmortemId",
                        column: x => x.PostmortemId,
                        principalTable: "Postmortems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostmortemActionItems_Owner_Status_DueAt",
                table: "PostmortemActionItems",
                columns: new[] { "Owner", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostmortemActionItems_PostmortemId",
                table: "PostmortemActionItems",
                column: "PostmortemId");

            migrationBuilder.CreateIndex(
                name: "IX_Postmortems_IncidentId",
                table: "Postmortems",
                column: "IncidentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceHealthSnapshots_Service_CapturedAt",
                table: "ServiceHealthSnapshots",
                columns: new[] { "Service", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceObjectives_Service",
                table: "ServiceObjectives",
                column: "Service",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostmortemActionItems");

            migrationBuilder.DropTable(
                name: "ServiceHealthSnapshots");

            migrationBuilder.DropTable(
                name: "ServiceObjectives");

            migrationBuilder.DropTable(
                name: "Postmortems");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Incidents");
        }
    }
}
