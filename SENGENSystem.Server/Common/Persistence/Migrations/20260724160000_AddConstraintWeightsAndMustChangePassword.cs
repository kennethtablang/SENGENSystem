using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // Two additive changes (FR-SCHED-05, FR-AUTH):
    //  * SystemSettings gains the tunable soft-constraint weights the CSP engine optimises against,
    //    defaulted to the engine's built-in SoftWeights so existing databases behave unchanged.
    //  * Users gains MustChangePassword, set on accounts SEN-GEN provisions from a SIS with a
    //    temporary password so the first sign-in is forced through a password change.
    // Hand-authored (the [Migration] attribute lives here rather than in a Designer file) so it is
    // discovered and applied by MigrateAsync at startup without a design-time build. The model
    // snapshot is updated to match.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260724160000_AddConstraintWeightsAndMustChangePassword")]
    public partial class AddConstraintWeightsAndMustChangePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "WeightPreference",
                table: "SystemSettings",
                type: "float",
                nullable: false,
                defaultValue: 0.40000000000000002);

            migrationBuilder.AddColumn<double>(
                name: "WeightIdleGap",
                table: "SystemSettings",
                type: "float",
                nullable: false,
                defaultValue: 0.34999999999999998);

            migrationBuilder.AddColumn<double>(
                name: "WeightRoomFit",
                table: "SystemSettings",
                type: "float",
                nullable: false,
                defaultValue: 0.25);

            migrationBuilder.AddColumn<double>(
                name: "GapSaturationHours",
                table: "SystemSettings",
                type: "float",
                nullable: false,
                defaultValue: 8.0);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WeightPreference", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "WeightIdleGap", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "WeightRoomFit", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "GapSaturationHours", table: "SystemSettings");
            migrationBuilder.DropColumn(name: "MustChangePassword", table: "Users");
        }
    }
}
