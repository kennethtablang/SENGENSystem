using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyCollectionControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SurveyInvitations_UserId",
                table: "SurveyInvitations");

            migrationBuilder.AddColumn<string>(
                name: "InvitedBy",
                table: "SurveyInvitations",
                type: "nvarchar(201)",
                maxLength: 201,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SurveyInvitations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAtUtc",
                table: "SurveyInvitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "SurveyInvitations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SurveyCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    TargetResponses = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastChangedBy = table.Column<string>(type: "nvarchar(201)", maxLength: 201, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyCampaigns", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SurveyCampaigns",
                columns: new[] { "Id", "ClosedAtUtc", "IsOpen", "LastChangedBy", "OpenedAtUtc", "TargetResponses" },
                values: new object[] { new Guid("5e2f0a10-0000-4000-8000-000000000001"), null, true, "System", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 30 });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyInvitations_UserId",
                table: "SurveyInvitations",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SurveyCampaigns");

            migrationBuilder.DropIndex(
                name: "IX_SurveyInvitations_UserId",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "InvitedBy",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "NotifiedAtUtc",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "SurveyInvitations");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyInvitations_UserId",
                table: "SurveyInvitations",
                column: "UserId");
        }
    }
}
