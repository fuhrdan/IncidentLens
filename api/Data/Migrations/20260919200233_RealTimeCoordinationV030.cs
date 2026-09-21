using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentLens.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RealTimeCoordinationV030 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommandArguments",
                table: "TimelineEvents",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommandName",
                table: "TimelineEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MentionsJson",
                table: "TimelineEvents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertIngestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertIngestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertIngestions_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AggregateId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertIngestions_IdempotencyKey",
                table: "AlertIngestions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertIngestions_IncidentId",
                table: "AlertIngestions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertIngestions");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "CommandArguments",
                table: "TimelineEvents");

            migrationBuilder.DropColumn(
                name: "CommandName",
                table: "TimelineEvents");

            migrationBuilder.DropColumn(
                name: "MentionsJson",
                table: "TimelineEvents");
        }
    }
}
