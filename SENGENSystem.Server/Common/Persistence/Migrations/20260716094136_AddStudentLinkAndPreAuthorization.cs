using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentLinkAndPreAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPreAuthorized",
                table: "StudentRegistrations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreAuthorizedAtUtc",
                table: "StudentRegistrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreAuthorizedByUserId",
                table: "StudentRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "StudentRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentRegistrations_UserId",
                table: "StudentRegistrations",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentRegistrations_Users_UserId",
                table: "StudentRegistrations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentRegistrations_Users_UserId",
                table: "StudentRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_StudentRegistrations_UserId",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "IsPreAuthorized",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "PreAuthorizedAtUtc",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "PreAuthorizedByUserId",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "StudentRegistrations");
        }
    }
}
