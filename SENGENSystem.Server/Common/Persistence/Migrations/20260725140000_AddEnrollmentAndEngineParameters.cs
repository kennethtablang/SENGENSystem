using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // FR-SCHED-05 / FR-ENL: SystemSettings gains five tunable institutional parameters —
    // an enlistment open/close switch, a per-student unit ceiling, a minimum viable section size,
    // and the CSP engine's time and step budgets. All default to the values previously hard-coded
    // (or "off"), so an existing database behaves exactly as before until an admin changes them.
    // Hand-authored so MigrateAsync applies it at startup without a design-time build; the model
    // snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260725140000_AddEnrollmentAndEngineParameters")]
    public partial class AddEnrollmentAndEngineParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnlistmentOpen",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxEnlistmentUnitsPerStudent",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinSectionEnrollment",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleTimeBudgetSeconds",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleMaxStepsThousands",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 2000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EnlistmentOpen", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "MaxEnlistmentUnitsPerStudent", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "MinSectionEnrollment", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "ScheduleTimeBudgetSeconds", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "ScheduleMaxStepsThousands", table: "SystemSettings");
        }
    }
}
