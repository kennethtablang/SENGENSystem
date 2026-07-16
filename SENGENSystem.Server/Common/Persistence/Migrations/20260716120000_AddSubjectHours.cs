using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // Adds weekly contact hours to Subjects (FR-SCHED-04): the basis for the Schedule Board's
    // Weekly Hours Tracker. Hand-authored (the [Migration] attribute lives here rather than in a
    // Designer file) so it is discovered and applied by MigrateAsync at startup without needing
    // a design-time build. The model snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260716120000_AddSubjectHours")]
    public partial class AddSubjectHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Hours",
                table: "Subjects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Seed weekly hours from existing units so pre-existing subjects have a sensible value.
            migrationBuilder.Sql("UPDATE [Subjects] SET [Hours] = [Units];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hours",
                table: "Subjects");
        }
    }
}
