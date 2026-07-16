using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSectionSemester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassSections_ProgramCode_YearLevel_SectionName",
                table: "ClassSections");

            migrationBuilder.AddColumn<Guid>(
                name: "SemesterId",
                table: "ClassSections",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_SemesterId_ProgramCode_YearLevel_SectionName",
                table: "ClassSections",
                columns: new[] { "SemesterId", "ProgramCode", "YearLevel", "SectionName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSections_Semesters_SemesterId",
                table: "ClassSections",
                column: "SemesterId",
                principalTable: "Semesters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClassSections_Semesters_SemesterId",
                table: "ClassSections");

            migrationBuilder.DropIndex(
                name: "IX_ClassSections_SemesterId_ProgramCode_YearLevel_SectionName",
                table: "ClassSections");

            migrationBuilder.DropColumn(
                name: "SemesterId",
                table: "ClassSections");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_ProgramCode_YearLevel_SectionName",
                table: "ClassSections",
                columns: new[] { "ProgramCode", "YearLevel", "SectionName" },
                unique: true);
        }
    }
}
