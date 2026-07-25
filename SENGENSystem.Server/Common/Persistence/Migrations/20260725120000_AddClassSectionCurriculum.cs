using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // FR-SCHED-04: a class section (cohort) now carries the curriculum version it follows, so
    // cohorts of the same program can sit on different catalogs at once (a 2nd-year block on the
    // retired curriculum while a 1st-year block starts the new one). The column is nullable —
    // existing rows are backfilled to their program's active curriculum in DbInitializer.
    // Hand-authored (the [Migration] attribute lives here rather than in a Designer file) so it is
    // discovered and applied by MigrateAsync at startup without a design-time build. The model
    // snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260725120000_AddClassSectionCurriculum")]
    public partial class AddClassSectionCurriculum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurriculumId",
                table: "ClassSections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_CurriculumId",
                table: "ClassSections",
                column: "CurriculumId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSections_Curricula_CurriculumId",
                table: "ClassSections",
                column: "CurriculumId",
                principalTable: "Curricula",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClassSections_Curricula_CurriculumId",
                table: "ClassSections");

            migrationBuilder.DropIndex(
                name: "IX_ClassSections_CurriculumId",
                table: "ClassSections");

            migrationBuilder.DropColumn(
                name: "CurriculumId",
                table: "ClassSections");
        }
    }
}
