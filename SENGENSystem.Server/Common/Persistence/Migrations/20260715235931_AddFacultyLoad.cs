using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultyLoad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FacultyLoadAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacultyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacultyLoadAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacultyLoadAssignments_FacultyProfiles_FacultyProfileId",
                        column: x => x.FacultyProfileId,
                        principalTable: "FacultyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FacultyLoadAssignments_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacultyLoadAssignments_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_FacultyProfileId_SubjectId_SemesterId",
                table: "FacultyLoadAssignments",
                columns: new[] { "FacultyProfileId", "SubjectId", "SemesterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_SemesterId",
                table: "FacultyLoadAssignments",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_FacultyLoadAssignments_SubjectId",
                table: "FacultyLoadAssignments",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacultyLoadAssignments");
        }
    }
}
