using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <summary>
    /// Students gain a year level (FR-SIS-09), published classes gain an amendment trail
    /// (FR-PUB-04), and transferees gain the Registrar's per-subject credit evaluation (FR-EVAL).
    /// <para>
    /// Existing registrations are backfilled to year 1 — the entry level, and the honest answer for
    /// a record that predates the field. A transferee's real level is settled by their evaluation
    /// and a returning student's by their next term activation, so nothing is guessed upward here:
    /// under-placing is correctable, over-placing puts a student in classes they haven't earned.
    /// </para>
    /// </summary>
    public partial class AddTransfereeEvaluationAndYearLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YearLevel",
                table: "StudentRegistrations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "YearLevelSetAtUtc",
                table: "StudentRegistrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "YearLevelSetByUserId",
                table: "StudentRegistrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AmendedAtUtc",
                table: "ScheduleAssignments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AmendedByUserId",
                table: "ScheduleAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAmended",
                table: "ScheduleAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TransfereeEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecommendedYearLevel = table.Column<int>(type: "int", nullable: false),
                    AssignedYearLevel = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EvaluatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransfereeEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransfereeEvaluations_Curricula_CurriculumId",
                        column: x => x.CurriculumId,
                        principalTable: "Curricula",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransfereeEvaluations_StudentRegistrations_StudentRegistrationId",
                        column: x => x.StudentRegistrationId,
                        principalTable: "StudentRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransfereeEvaluationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransfereeEvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceSubject = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SourceGrade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransfereeEvaluationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransfereeEvaluationItems_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransfereeEvaluationItems_TransfereeEvaluations_TransfereeEvaluationId",
                        column: x => x.TransfereeEvaluationId,
                        principalTable: "TransfereeEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransfereeEvaluationItems_SubjectId",
                table: "TransfereeEvaluationItems",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TransfereeEvaluationItems_TransfereeEvaluationId_SubjectId",
                table: "TransfereeEvaluationItems",
                columns: new[] { "TransfereeEvaluationId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransfereeEvaluations_CurriculumId",
                table: "TransfereeEvaluations",
                column: "CurriculumId");

            migrationBuilder.CreateIndex(
                name: "IX_TransfereeEvaluations_StudentRegistrationId",
                table: "TransfereeEvaluations",
                column: "StudentRegistrationId",
                unique: true);

            // Every record that predates the field enters at year 1 (see the class remarks).
            migrationBuilder.Sql(
                "UPDATE [StudentRegistrations] SET [YearLevel] = 1 WHERE [YearLevel] < 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransfereeEvaluationItems");

            migrationBuilder.DropTable(
                name: "TransfereeEvaluations");

            migrationBuilder.DropColumn(
                name: "YearLevel",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "YearLevelSetAtUtc",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "YearLevelSetByUserId",
                table: "StudentRegistrations");

            migrationBuilder.DropColumn(
                name: "AmendedAtUtc",
                table: "ScheduleAssignments");

            migrationBuilder.DropColumn(
                name: "AmendedByUserId",
                table: "ScheduleAssignments");

            migrationBuilder.DropColumn(
                name: "IsAmended",
                table: "ScheduleAssignments");
        }
    }
}
