using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialStudentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfficialStudentNumber",
                table: "StudentRegistrations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfficialStudentNumberSetAtUtc",
                table: "StudentRegistrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OfficialStudentNumberSetByUserId",
                table: "StudentRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentRegistrations_OfficialStudentNumber",
                table: "StudentRegistrations",
                column: "OfficialStudentNumber",
                unique: true,
                filter: "[OfficialStudentNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentRegistrations_OfficialStudentNumber",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "OfficialStudentNumber",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "OfficialStudentNumberSetAtUtc",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "OfficialStudentNumberSetByUserId",
                table: "StudentRegistrations");
        }
    }
}
