using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StudentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Program = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Birthplace = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Citizenship = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CivilStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Barangay = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CityMunicipality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ZipCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastSchoolLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SchoolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchoolProgram = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchoolYear = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    YearGradeLastAttended = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastTerm = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FatherName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FatherMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MotherName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    MotherMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GuardianRelationship = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GuardianName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    GuardianMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferredBy = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentRegistrations_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationDocuments_StudentRegistrations_StudentRegistrationId",
                        column: x => x.StudentRegistrationId,
                        principalTable: "StudentRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TermActivations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermActivations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TermActivations_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TermActivations_StudentRegistrations_StudentRegistrationId",
                        column: x => x.StudentRegistrationId,
                        principalTable: "StudentRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationDocuments_StudentRegistrationId_DocumentType",
                table: "RegistrationDocuments",
                columns: new[] { "StudentRegistrationId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentRegistrations_SemesterId",
                table: "StudentRegistrations",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentRegistrations_StudentNumber",
                table: "StudentRegistrations",
                column: "StudentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TermActivations_SemesterId",
                table: "TermActivations",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_TermActivations_Status",
                table: "TermActivations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TermActivations_StudentRegistrationId",
                table: "TermActivations",
                column: "StudentRegistrationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationDocuments");

            migrationBuilder.DropTable(
                name: "TermActivations");

            migrationBuilder.DropTable(
                name: "StudentRegistrations");
        }
    }
}
