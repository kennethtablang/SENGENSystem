using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.PreEnrollment.Import
{
    // Vertical slice: the Registrar imports prospective student lists from .xlsx (FR-PRE-01).
    // Valid rows load in one transaction with issued student numbers and seeded document
    // checklists; failures and duplicates are reported per row without aborting the rest
    // (FR-PRE-03). Imported students then flow through the same confirm → checklist →
    // pre-authorization gate as everyone else (FR-PRE-02).
    public static class ImportStudentsEndpoint
    {
        private const long MaxFileBytes = 5 * 1024 * 1024;

        public static IEndpointRouteBuilder MapPreEnrollmentImport(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/pre-enrollment/import", ImportAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)))
                .DisableAntiforgery();
            app.MapGet("/api/pre-enrollment/template", Template)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> ImportAsync(
            IFormFile? file,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "Attach an .xlsx file to import." });
            }
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "Only .xlsx workbooks are supported. Download the template to start." });
            }
            if (file.Length > MaxFileBytes)
            {
                return Results.BadRequest(new { message = "The file is too large (5 MB max)." });
            }

            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Import is closed: no active semester is set." });
            }

            ImportReport report;
            try
            {
                await using var stream = file.OpenReadStream();
                report = await XlsxStudentImporter.ImportAsync(stream, db, semester, cancellationToken);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new
                {
                    message = "The file could not be read as an Excel workbook. Save it as .xlsx and try again."
                });
            }

            if (report.Loaded > 0)
            {
                audit.Record(AuditAction.PreEnrollmentImported,
                    $"Imported {report.Loaded} prospective student(s) from \"{file.FileName}\" " +
                    $"({report.Skipped} duplicate(s) skipped, {report.Failed} row(s) failed validation).",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                report.TotalRows,
                report.Loaded,
                report.Skipped,
                report.Failed,
                rows = report.Rows
            });
        }

        private static IResult Template() =>
            Results.File(
                XlsxStudentImporter.BuildTemplate(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "sengen-preenrollment-template.xlsx");
    }
}
