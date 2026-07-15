using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurriculum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subjects_Code",
                table: "Subjects");

            migrationBuilder.AddColumn<Guid>(
                name: "CurriculumId",
                table: "Subjects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Curricula",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProgramName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    EffectivityYear = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Curricula", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubjectPrerequisites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrerequisiteSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectPrerequisites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectPrerequisites_Subjects_PrerequisiteSubjectId",
                        column: x => x.PrerequisiteSubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubjectPrerequisites_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_CurriculumId_Code",
                table: "Subjects",
                columns: new[] { "CurriculumId", "Code" },
                unique: true,
                filter: "[CurriculumId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Curricula_ProgramCode_EffectivityYear",
                table: "Curricula",
                columns: new[] { "ProgramCode", "EffectivityYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectPrerequisites_PrerequisiteSubjectId",
                table: "SubjectPrerequisites",
                column: "PrerequisiteSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectPrerequisites_SubjectId_PrerequisiteSubjectId",
                table: "SubjectPrerequisites",
                columns: new[] { "SubjectId", "PrerequisiteSubjectId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Curricula_CurriculumId",
                table: "Subjects",
                column: "CurriculumId",
                principalTable: "Curricula",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Curricula_CurriculumId",
                table: "Subjects");

            migrationBuilder.DropTable(
                name: "Curricula");

            migrationBuilder.DropTable(
                name: "SubjectPrerequisites");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_CurriculumId_Code",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "CurriculumId",
                table: "Subjects");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Code",
                table: "Subjects",
                column: "Code",
                unique: true);
        }
    }
}
