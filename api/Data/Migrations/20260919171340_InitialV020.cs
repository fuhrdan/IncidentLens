using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentLens.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialV020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Severity = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Service = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OwnerTeam = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Assignee = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AffectedCustomers = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IncidentVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditRecords_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Responders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Role = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Responders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Responders_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimelineEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEvents_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_IncidentId_OccurredAt",
                table: "AuditRecords",
                columns: new[] { "IncidentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Sequence",
                table: "Incidents",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Responders_IncidentId_Name",
                table: "Responders",
                columns: new[] { "IncidentId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_IncidentId_Value",
                table: "Tags",
                columns: new[] { "IncidentId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_IncidentId_OccurredAt",
                table: "TimelineEvents",
                columns: new[] { "IncidentId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords");

            migrationBuilder.DropTable(
                name: "Responders");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "TimelineEvents");

            migrationBuilder.DropTable(
                name: "Incidents");
        }
    }
}
