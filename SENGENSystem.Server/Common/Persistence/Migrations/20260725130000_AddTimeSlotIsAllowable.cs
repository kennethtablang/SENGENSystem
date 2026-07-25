using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // FR-SCHED-05: TimeSlots gains IsAllowable, separating the admin-configured allowable grid from
    // the synthetic assignment periods the scheduler/board create to persist a placement. Only
    // allowable slots feed the CSP engine and the System Parameters page, so generated blocks no
    // longer dilute the configured times. Backfill: any slot already referenced by a schedule
    // assignment is a placement period, so it is marked non-allowable; everything else stays
    // allowable (the default). Hand-authored so MigrateAsync applies it at startup without a
    // design-time build; the model snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260725130000_AddTimeSlotIsAllowable")]
    public partial class AddTimeSlotIsAllowable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAllowable",
                table: "TimeSlots",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // Slots already used by a placement are synthetic assignment periods, not grid entries.
            migrationBuilder.Sql(
                "UPDATE [TimeSlots] SET [IsAllowable] = 0 " +
                "WHERE [Id] IN (SELECT DISTINCT [TimeSlotId] FROM [ScheduleAssignments]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAllowable",
                table: "TimeSlots");
        }
    }
}
