using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSubmittedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SubmittedOn",
                table: "RegistrationDocuments",
                type: "date",
                nullable: true);

            // Papers already received get the best date on record: the day (Philippine time) their
            // row was last decided. Rows nobody has ruled on since provenance existed stay blank —
            // an honest "unknown" the office can fill in, rather than an invented date.
            migrationBuilder.Sql(
                """
                UPDATE RegistrationDocuments
                SET SubmittedOn = CAST(DATEADD(HOUR, 8, UpdatedAtUtc) AS date)
                WHERE Status <> 'NotSubmitted' AND UpdatedAtUtc IS NOT NULL AND SubmittedOn IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmittedOn",
                table: "RegistrationDocuments");
        }
    }
}
