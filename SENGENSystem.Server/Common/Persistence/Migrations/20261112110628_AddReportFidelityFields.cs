using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportFidelityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "ClassNumbers",
                startValue: 1001L);

            migrationBuilder.AddColumn<string>(
                name: "ProgramHeadOf",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LabTeachingCreditFactor",
                table: "SystemSettings",
                type: "float",
                nullable: false,
                defaultValue: 1.0);

            // Existing offerings get a number each, in a stable order, before the column becomes
            // required — so the unique index below never meets a duplicate or a blank.
            migrationBuilder.AddColumn<int>(
                name: "ClassNumber",
                table: "Sections",
                type: "int",
                nullable: true);

            // NEXT VALUE FOR cannot be ordered inside an UPDATE, so number the existing rows with
            // ROW_NUMBER from the sequence's start, then move the sequence past the last one.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT [ClassNumber], ROW_NUMBER() OVER (ORDER BY [SectionCode], [Id]) AS rn
                    FROM [Sections])
                UPDATE numbered SET [ClassNumber] = 1000 + rn;

                DECLARE @next int = ISNULL((SELECT MAX([ClassNumber]) FROM [Sections]), 1000) + 1;
                DECLARE @restart nvarchar(100) = N'ALTER SEQUENCE [ClassNumbers] RESTART WITH ' + CAST(@next AS nvarchar(12));
                EXEC sp_executesql @restart;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ClassNumber",
                table: "Sections",
                type: "int",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR [ClassNumbers]",
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ProgramHeadOf",
                table: "Users",
                column: "ProgramHeadOf",
                unique: true,
                filter: "[ProgramHeadOf] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ClassNumber",
                table: "Sections",
                column: "ClassNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ProgramHeadOf",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ClassNumber",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "ProgramHeadOf",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LabTeachingCreditFactor",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ClassNumber",
                table: "Sections");

            migrationBuilder.DropSequence(
                name: "ClassNumbers");
        }
    }
}
