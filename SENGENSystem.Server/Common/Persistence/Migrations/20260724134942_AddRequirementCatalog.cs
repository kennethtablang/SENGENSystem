using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementCatalog : Migration
    {
        // Deterministic ids for the nine built-in requirements so the seed is stable across runs.
        // Codes match the former DocumentType enum names, so existing RegistrationDocument rows
        // (renamed DocumentType -> RequirementCode) keep resolving to the right requirement.
        private static readonly Guid Form138 = new("a1000000-0000-0000-0000-000000000001");
        private static readonly Guid Form137 = new("a1000000-0000-0000-0000-000000000002");
        private static readonly Guid GoodMoral = new("a1000000-0000-0000-0000-000000000003");
        private static readonly Guid Psa = new("a1000000-0000-0000-0000-000000000004");
        private static readonly Guid Transcript = new("a1000000-0000-0000-0000-000000000005");
        private static readonly Guid Honorable = new("a1000000-0000-0000-0000-000000000006");
        private static readonly Guid HepaA = new("a1000000-0000-0000-0000-000000000007");
        private static readonly Guid HepaB = new("a1000000-0000-0000-0000-000000000008");
        private static readonly Guid Xray = new("a1000000-0000-0000-0000-000000000009");

        private static readonly DateTime Seeded = new(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Preserve existing checklists: rename the column instead of drop/add ----
            migrationBuilder.DropIndex(
                name: "IX_RegistrationDocuments_StudentRegistrationId_DocumentType",
                table: "RegistrationDocuments");

            migrationBuilder.RenameColumn(
                name: "DocumentType",
                table: "RegistrationDocuments",
                newName: "RequirementCode");

            migrationBuilder.AlterColumn<string>(
                name: "RequirementCode",
                table: "RegistrationDocuments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            // ---- Requirement catalog ----
            migrationBuilder.CreateTable(
                name: "AdmissionRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdmissionRequirementPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmissionRequirementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Program = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionRequirementPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdmissionRequirementPrograms_AdmissionRequirements_AdmissionRequirementId",
                        column: x => x.AdmissionRequirementId,
                        principalTable: "AdmissionRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationDocuments_StudentRegistrationId_RequirementCode",
                table: "RegistrationDocuments",
                columns: new[] { "StudentRegistrationId", "RequirementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionRequirementPrograms_AdmissionRequirementId_Program",
                table: "AdmissionRequirementPrograms",
                columns: new[] { "AdmissionRequirementId", "Program" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionRequirements_Code",
                table: "AdmissionRequirements",
                column: "Code",
                unique: true);

            // ---- Seed the nine built-in requirements (mirrors the former DocumentType enum) ----
            migrationBuilder.InsertData(
                table: "AdmissionRequirements",
                columns: new[] { "Id", "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAtUtc" },
                values: new object[,]
                {
                    { Form138, "Form138_SF9", "Form 138 / SF9-SHS (Report Card)", null, true, 1, Seeded },
                    { Form137, "Form137_SF10", "Form 137 / SF10-SHS (Permanent Record)", null, true, 2, Seeded },
                    { GoodMoral, "GoodMoral", "Certificate of Good Moral Character", null, true, 3, Seeded },
                    { Psa, "PsaBirthCertificate", "PSA Birth Certificate", null, true, 4, Seeded },
                    { Transcript, "OfficialTranscript", "Official Transcript of Records", null, true, 5, Seeded },
                    { Honorable, "HonorableDismissal", "Certificate of Transfer (Honorable Dismissal)", null, true, 6, Seeded },
                    { HepaA, "HepaA", "Hepatitis A Vaccination Record", null, true, 7, Seeded },
                    { HepaB, "HepaB", "Hepatitis B Vaccination Record", null, true, 8, Seeded },
                    { Xray, "Xray", "Chest X-ray Result", null, true, 9, Seeded }
                });

            // Program applicability. Health papers (Hepa A/B, X-ray) apply to the hospitality
            // programs only; ITP enrollees are exempt. Everything else applies to all three programs.
            migrationBuilder.InsertData(
                table: "AdmissionRequirementPrograms",
                columns: new[] { "Id", "AdmissionRequirementId", "Program" },
                values: new object[,]
                {
                    { NewId(1), Form138, "ITP" }, { NewId(2), Form138, "HRS" }, { NewId(3), Form138, "HRA" },
                    { NewId(4), Form137, "ITP" }, { NewId(5), Form137, "HRS" }, { NewId(6), Form137, "HRA" },
                    { NewId(7), GoodMoral, "ITP" }, { NewId(8), GoodMoral, "HRS" }, { NewId(9), GoodMoral, "HRA" },
                    { NewId(10), Psa, "ITP" }, { NewId(11), Psa, "HRS" }, { NewId(12), Psa, "HRA" },
                    { NewId(13), Transcript, "ITP" }, { NewId(14), Transcript, "HRS" }, { NewId(15), Transcript, "HRA" },
                    { NewId(16), Honorable, "ITP" }, { NewId(17), Honorable, "HRS" }, { NewId(18), Honorable, "HRA" },
                    { NewId(19), HepaA, "HRS" }, { NewId(20), HepaA, "HRA" },
                    { NewId(21), HepaB, "HRS" }, { NewId(22), HepaB, "HRA" },
                    { NewId(23), Xray, "HRS" }, { NewId(24), Xray, "HRA" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdmissionRequirementPrograms");

            migrationBuilder.DropTable(
                name: "AdmissionRequirements");

            migrationBuilder.DropIndex(
                name: "IX_RegistrationDocuments_StudentRegistrationId_RequirementCode",
                table: "RegistrationDocuments");

            migrationBuilder.AlterColumn<string>(
                name: "RequirementCode",
                table: "RegistrationDocuments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.RenameColumn(
                name: "RequirementCode",
                table: "RegistrationDocuments",
                newName: "DocumentType");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationDocuments_StudentRegistrationId_DocumentType",
                table: "RegistrationDocuments",
                columns: new[] { "StudentRegistrationId", "DocumentType" },
                unique: true);
        }

        private static Guid NewId(int n) =>
            new($"a2000000-0000-0000-0000-{n:D12}");
    }
}
