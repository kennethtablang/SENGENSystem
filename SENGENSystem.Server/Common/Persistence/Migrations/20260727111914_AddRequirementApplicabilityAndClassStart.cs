using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <summary>
    /// Admission requirements gain a student-type applicability, an authorization gate, and a
    /// certificate-of-grades option; the scheduling parameters gain the time the teaching day opens.
    /// <para>
    /// The built-in nine are reclassified to the school's actual practice: a new enrollee is asked
    /// for the report card, permanent record, and good moral (the report card and good moral gate
    /// their authorization); a transferee for the transcript and honorable dismissal (both gating).
    /// The transcript is never accepted as a photocopy — a certificate of grades stands in for it
    /// instead, so existing "xerox copy" transcript rows are carried over to that status.
    /// </para>
    /// </summary>
    public partial class AddRequirementApplicabilityAndClassStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClassDayStartMinutes",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsCertificateOfGrades",
                table: "AdmissionRequirements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToNewStudents",
                table: "AdmissionRequirements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToTransferees",
                table: "AdmissionRequirements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredForAuthorization",
                table: "AdmissionRequirements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The teaching day opens at 07:00 unless the Academic Head moves it — the earliest
            // period the existing grids carry, so generation behaves exactly as it did before.
            migrationBuilder.Sql("UPDATE [SystemSettings] SET [ClassDayStartMinutes] = 420;");

            // Every requirement asks every student type until told otherwise, so a school that
            // added its own papers keeps them on both checklists.
            migrationBuilder.Sql(
                "UPDATE [AdmissionRequirements] SET [AppliesToNewStudents] = 1, [AppliesToTransferees] = 1;");

            // Papers only the high school a new enrollee is leaving can issue.
            migrationBuilder.Sql(
                """
                UPDATE [AdmissionRequirements]
                SET [AppliesToTransferees] = 0
                WHERE [Code] IN ('Form138_SF9', 'Form137_SF10', 'GoodMoral');
                """);

            // Papers only the college a transferee is leaving can issue.
            migrationBuilder.Sql(
                """
                UPDATE [AdmissionRequirements]
                SET [AppliesToNewStudents] = 0
                WHERE [Code] IN ('OfficialTranscript', 'HonorableDismissal');
                """);

            // The gate on pre-authorization: report card + good moral for a new enrollee,
            // transcript + honorable dismissal for a transferee. The rest may still follow.
            migrationBuilder.Sql(
                """
                UPDATE [AdmissionRequirements]
                SET [IsRequiredForAuthorization] = 1
                WHERE [Code] IN ('Form138_SF9', 'GoodMoral', 'OfficialTranscript', 'HonorableDismissal');
                """);

            // A certificate of grades stands in for the transcript, in place of a photocopy.
            migrationBuilder.Sql(
                "UPDATE [AdmissionRequirements] SET [AcceptsCertificateOfGrades] = 1 WHERE [Code] = 'OfficialTranscript';");

            migrationBuilder.Sql(
                """
                UPDATE d
                SET d.[Status] = 'CertificateOfGrades'
                FROM [RegistrationDocuments] d
                WHERE d.[RequirementCode] = 'OfficialTranscript' AND d.[Status] = 'XeroxCopy';
                """);

            // Drop the checklist rows a student's route into the school can never produce. Seeded
            // before applicability existed, they would otherwise sit permanently unsubmitted —
            // holding completion, and now authorization, hostage.
            migrationBuilder.Sql(
                """
                DELETE d
                FROM [RegistrationDocuments] d
                INNER JOIN [StudentRegistrations] r ON r.[Id] = d.[StudentRegistrationId]
                INNER JOIN [AdmissionRequirements] a ON a.[Code] = d.[RequirementCode]
                WHERE (r.[StudentType] = 'Transferee' AND a.[AppliesToTransferees] = 0)
                   OR (r.[StudentType] <> 'Transferee' AND a.[AppliesToNewStudents] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The dropped checklist rows are not restored — the papers they stood for were never
            // applicable to those students. Transcripts recorded against a certificate of grades
            // fall back to the nearest state the old schema had for them.
            migrationBuilder.Sql(
                "UPDATE [RegistrationDocuments] SET [Status] = 'XeroxCopy' WHERE [Status] = 'CertificateOfGrades';");

            migrationBuilder.DropColumn(
                name: "ClassDayStartMinutes",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "AcceptsCertificateOfGrades",
                table: "AdmissionRequirements");

            migrationBuilder.DropColumn(
                name: "AppliesToNewStudents",
                table: "AdmissionRequirements");

            migrationBuilder.DropColumn(
                name: "AppliesToTransferees",
                table: "AdmissionRequirements");

            migrationBuilder.DropColumn(
                name: "IsRequiredForAuthorization",
                table: "AdmissionRequirements");
        }
    }
}
