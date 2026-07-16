using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemesterTermAndCurriculumEffectivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Semesters_SchoolYearId",
                table: "Semesters");

            migrationBuilder.DropIndex(
                name: "IX_Curricula_ProgramCode_EffectivityYear",
                table: "Curricula");

            migrationBuilder.DropColumn(
                name: "EffectivityYear",
                table: "Curricula");

            migrationBuilder.AddColumn<string>(
                name: "Term",
                table: "Semesters",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FirstSemester");

            migrationBuilder.CreateTable(
                name: "CurriculumSchoolYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumSchoolYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumSchoolYears_Curricula_CurriculumId",
                        column: x => x.CurriculumId,
                        principalTable: "Curricula",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumSchoolYears_SchoolYears_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "SchoolYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_SchoolYearId_Term",
                table: "Semesters",
                columns: new[] { "SchoolYearId", "Term" },
                unique: true,
                filter: "[SchoolYearId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumSchoolYears_CurriculumId_SchoolYearId",
                table: "CurriculumSchoolYears",
                columns: new[] { "CurriculumId", "SchoolYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumSchoolYears_SchoolYearId",
                table: "CurriculumSchoolYears",
                column: "SchoolYearId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurriculumSchoolYears");

            migrationBuilder.DropIndex(
                name: "IX_Semesters_SchoolYearId_Term",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "Term",
                table: "Semesters");

            migrationBuilder.AddColumn<int>(
                name: "EffectivityYear",
                table: "Curricula",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_SchoolYearId",
                table: "Semesters",
                column: "SchoolYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Curricula_ProgramCode_EffectivityYear",
                table: "Curricula",
                columns: new[] { "ProgramCode", "EffectivityYear" },
                unique: true);
        }
    }
}
