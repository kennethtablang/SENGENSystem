using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    // Term activation gains a switch and a confirmation step:
    //  · SystemSettings.TermActivationOpen — the institution-wide open/close control the Registrar,
    //    Academic Head, and the two admin roles throw between terms. Defaults to true, so an
    //    existing database keeps the always-open behaviour it had until someone closes it.
    //  · TermActivations.DeclaredYearLevel — the year level the student confirmed when filing.
    //    Nullable: requests filed before the confirmation step existed never had one.
    /// <inheritdoc />
    public partial class AddTermActivationControlAndConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeclaredYearLevel",
                table: "TermActivations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TermActivationOpen",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredYearLevel",
                table: "TermActivations");

            migrationBuilder.DropColumn(
                name: "TermActivationOpen",
                table: "SystemSettings");
        }
    }
}
