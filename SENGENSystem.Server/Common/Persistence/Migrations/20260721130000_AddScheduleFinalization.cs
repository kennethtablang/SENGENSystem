using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // Adds the Academic Head's finalization sign-off to ScheduleAssignments (FR-SCHED-06):
    // a draft can be locked as "final, ready to publish" before the Registrar publishes it.
    // Hand-authored (the [Migration] attribute lives here rather than in a Designer file) so it
    // is discovered and applied by MigrateAsync at startup without a design-time build. The model
    // snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721130000_AddScheduleFinalization")]
    public partial class AddScheduleFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFinalized",
                table: "ScheduleAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "FinalizedAtUtc",
                table: "ScheduleAssignments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsFinalized", table: "ScheduleAssignments");
            migrationBuilder.DropColumn(name: "FinalizedAtUtc", table: "ScheduleAssignments");
        }
    }
}
