using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnlistment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EnrolledCount",
                table: "Sections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Sections",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "SlotRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotRequests_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlotRequests_StudentRegistrations_StudentRegistrationId",
                        column: x => x.StudentRegistrationId,
                        principalTable: "StudentRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sections_EnrolledCount",
                table: "Sections",
                sql: "[EnrolledCount] >= 0 AND [EnrolledCount] <= [Capacity]");

            migrationBuilder.CreateIndex(
                name: "IX_SlotRequests_SectionId",
                table: "SlotRequests",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotRequests_Status",
                table: "SlotRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlotRequests_StudentRegistrationId_SectionId",
                table: "SlotRequests",
                columns: new[] { "StudentRegistrationId", "SectionId" },
                unique: true,
                filter: "[Status] IN ('Requested','Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sections_EnrolledCount",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "EnrolledCount",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Sections");
        }
    }
}
