using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// Every report and export the app produces, downloaded through the real endpoint against the
    /// seeded database and then <i>opened</i> — a 200 with a corrupt body is the failure that
    /// matters for a document someone prints and signs. A workbook must load in ClosedXML with
    /// content on its first sheet, a PDF must be a whole PDF, and a bundle must be a zip of
    /// workbooks that each open.
    /// <para>
    /// Smoke tests, deliberately: they assert the file is real, not what every cell says. The one
    /// content check is the institution name, because it is a setting threaded through every
    /// document and the cheapest way to prove the threading is to change it and look.
    /// </para>
    /// </summary>
    [Collection(AppCollection.Name)]
    public class ReportSmokeTests(SengenAppFactory app)
    {
        private const string Admin = "admin@stialaminos.local";
        private const string AdminPassword = "Admin@Sengen2026";

        private sealed record Ids(Guid Semester, Guid Faculty, Guid Registration, Guid Curriculum);

        /// <summary>
        /// Arranges what every report needs rather than trusting the seed to have it: the seed
        /// creates no timetable (on a real install that comes from running the generator), and a
        /// report over nothing proves little. So: a published class with a faculty member, a
        /// student holding an approved seat in it, and a curriculum to print.
        /// </summary>
        private async Task<Ids> IdsAsync()
        {
            var scenario = new EnlistmentScenario(app);
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, publishedAt: (DayOfWeek.Wednesday, 13 * 60, 16 * 60));
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(student, section, SlotRequestStatus.Approved);

            await using var scope = app.Scope(out var db);
            var faculty = await db.ScheduleAssignments.Where(a => a.SectionId == section.Id)
                .Select(a => a.FacultyProfileId).SingleAsync();
            var curriculum = await db.Subjects.Where(s => s.Id == subject.Id).Select(s => s.CurriculumId!.Value).SingleAsync();
            return new Ids(term.Id, faculty, student.Id, curriculum);
        }

        private static void AssertWorkbook(byte[] bytes, string what)
        {
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            Assert.True(workbook.Worksheets.Count > 0, $"{what}: no worksheets");
            Assert.False(workbook.Worksheet(1).RangeUsed() is null, $"{what}: first sheet is empty");
        }

        private static void AssertPdf(byte[] bytes, string what)
        {
            Assert.True(bytes.Length > 1000, $"{what}: suspiciously small ({bytes.Length} bytes)");
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            // A truncated PDF still starts right; a whole one ends with its trailer marker.
            Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length)));
        }

        private async Task<byte[]> DownloadAsync(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadAsByteArrayAsync();
        }

        [SqlFact]
        public async Task Every_workbook_opens_with_content()
        {
            var ids = await IdsAsync();
            var admin = await app.SignedInAsync(Admin, AdminPassword);
            var sem = $"semesterId={ids.Semester}";

            string[] workbooks =
            [
                $"/api/reports/registration?{sem}&format=xlsx",
                $"/api/reports/enlistment?{sem}&format=xlsx",
                $"/api/reports/faculty-load?{sem}&format=xlsx",
                $"/api/reports/room-utilization?{sem}&format=xlsx",
                $"/api/reports/document-completion?{sem}&format=xlsx",
                $"/api/reports/faculty-loading/consolidated?{sem}",
                $"/api/reports/faculty-loading/{ids.Faculty}?{sem}",
                $"/api/reports/faculty-loading/{ids.Faculty}/schedule-grid?{sem}",
                $"/api/reports/grid-schedules?{sem}",
                $"/api/reports/room-grid-schedule?{sem}",
                $"/api/reports/semester-export?{sem}",
                "/api/reports/system-export",
                $"/api/analytics/room-utilization/export?{sem}"
            ];
            foreach (var url in workbooks)
            {
                AssertWorkbook(await DownloadAsync(admin, url), url);
            }
        }

        [SqlFact]
        public async Task Every_pdf_is_a_whole_pdf()
        {
            var ids = await IdsAsync();
            var admin = await app.SignedInAsync(Admin, AdminPassword);
            var sem = $"semesterId={ids.Semester}";

            string[] pdfs =
            [
                $"/api/reports/faculty-loading/consolidated.pdf?{sem}",
                $"/api/reports/faculty-loading/{ids.Faculty}/pdf?{sem}",
                $"/api/prospectus/curriculum.pdf?curriculumId={ids.Curriculum}&yearLevel=1",
                $"/api/prospectus/students/{ids.Registration}/registration-form.pdf"
            ];
            foreach (var url in pdfs)
            {
                AssertPdf(await DownloadAsync(admin, url), url);
            }
        }

        [SqlFact]
        public async Task The_bulk_bundle_is_a_zip_of_workbooks_that_each_open()
        {
            var ids = await IdsAsync();
            var admin = await app.SignedInAsync(Admin, AdminPassword);

            var bytes = await DownloadAsync(admin, $"/api/reports/faculty-loading/bulk?semesterId={ids.Semester}");
            using var zip = new ZipArchive(new MemoryStream(bytes));

            Assert.NotEmpty(zip.Entries);
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
            {
                using var buffer = new MemoryStream();
                await using (var stream = entry.Open()) await stream.CopyToAsync(buffer);
                AssertWorkbook(buffer.ToArray(), entry.FullName);
            }
        }

        [SqlFact]
        public async Task A_renamed_institution_appears_on_the_printed_memo()
        {
            var ids = await IdsAsync();
            var admin = await app.SignedInAsync(Admin, AdminPassword);
            const string Renamed = "STI College Integration Test";

            var original = (await admin.GetFromJsonAsync<ParametersBody>("/api/parameters"))!.Institution.Name;
            try
            {
                (await admin.PutAsJsonAsync("/api/parameters/settings", new { institutionName = Renamed })).EnsureSuccessStatusCode();

                var bytes = await DownloadAsync(admin, $"/api/reports/faculty-loading/consolidated?semesterId={ids.Semester}");
                using var workbook = new XLWorkbook(new MemoryStream(bytes));
                var text = string.Join("\n", workbook.Worksheets.SelectMany(ws => ws.CellsUsed()).Select(c => c.GetString()));
                Assert.Contains(Renamed.ToUpperInvariant(), text);
                Assert.DoesNotContain("STI COLLEGE ALAMINOS", text);
            }
            finally
            {
                // Shared database: put it back for the other tests.
                await admin.PutAsJsonAsync("/api/parameters/settings", new { institutionName = original });
            }
        }

        private sealed record ParametersBody(InstitutionBody Institution);

        private sealed record InstitutionBody(string Name);
    }
}
