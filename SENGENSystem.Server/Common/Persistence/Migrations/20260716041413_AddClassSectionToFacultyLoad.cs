using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSectionToFacultyLoad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacultyLoadAssignments_FacultyProfileId_SubjectId_SemesterId",
                table: "FacultyLoadAssignments");

            migrationBuilder.DropIndex(
                name: "IX_FacultyLoadAssignments_SubjectId",
                table: "FacultyLoadAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "ClassSectionId",
                table: "FacultyLoadAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_ClassSectionId",
                table: "FacultyLoadAssignments",
                column: "ClassSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_FacultyProfileId",
                table: "FacultyLoadAssignments",
                column: "FacultyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_SubjectId_ClassSectionId",
                table: "FacultyLoadAssignments",
                columns: new[] { "SubjectId", "ClassSectionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FacultyLoadAssignments_ClassSections_ClassSectionId",
                table: "FacultyLoadAssignments",
                column: "ClassSectionId",
                principalTable: "ClassSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FacultyLoadAssignments_ClassSections_ClassSectionId",
                table: "FacultyLoadAssignments");

            migrationBuilder.DropIndex(
                name: "IX_FacultyLoadAssignments_ClassSectionId",
                table: "FacultyLoadAssignments");

            migrationBuilder.DropIndex(
                name: "IX_FacultyLoadAssignments_FacultyProfileId",
                table: "FacultyLoadAssignments");

            migrationBuilder.DropIndex(
                name: "IX_FacultyLoadAssignments_SubjectId_ClassSectionId",
                table: "FacultyLoadAssignments");

            migrationBuilder.DropColumn(
                name: "ClassSectionId",
                table: "FacultyLoadAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_FacultyProfileId_SubjectId_SemesterId",
                table: "FacultyLoadAssignments",
                columns: new[] { "FacultyProfileId", "SubjectId", "SemesterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_SubjectId",
                table: "FacultyLoadAssignments",
                column: "SubjectId");
        }
    }
}
