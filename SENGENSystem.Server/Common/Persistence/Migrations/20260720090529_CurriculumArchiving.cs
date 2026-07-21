using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                table: "Curricula",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "Curricula",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Curricula",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                table: "Curricula");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Curricula");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Curricula");
        }
    }
}
