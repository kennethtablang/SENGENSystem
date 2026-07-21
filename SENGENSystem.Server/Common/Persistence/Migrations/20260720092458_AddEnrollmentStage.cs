using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrollmentStage",
                table: "Semesters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Preparation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrollmentStage",
                table: "Semesters");
        }
    }
}
