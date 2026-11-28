using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The requirement checklist spreadsheet round trip (FR-DOC-02): an office sheet with no SEN-GEN
    /// number is matched by name, its Student ID is learnt, and the next import — or SEN-GEN's own
    /// export — matches by ID.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class ChecklistTransferTests(SengenAppFactory app)
    {
        private const string SchoolAdmin = "admin@stialaminos.local";
        private const string SchoolAdminPassword = "Admin@Sengen2026";

        private static byte[] OfficeSheet((string Last, string First, string Middle) name, string studentId, string form138, string psa, string hepaA)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("ITP ");
            string[] headers = ["STUDENT ID", "LAST NAME", "FIRST NAME", "MIDDLE NAME", "LEVEL", "HEPA A", "FORM 138", "PSA", "REMARKS"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).SetValue(studentId);
            sheet.Cell(2, 2).Value = name.Last;
            sheet.Cell(2, 3).Value = name.First;
            sheet.Cell(2, 4).Value = name.Middle;
            sheet.Cell(2, 5).Value = "1Y1";
            sheet.Cell(2, 6).Value = hepaA;
            sheet.Cell(2, 7).Value = form138;
            sheet.Cell(2, 8).Value = psa;
            sheet.Cell(2, 9).Value = "COMPLETE";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static async Task<JsonDocument> PostAsync(HttpClient client, string url, byte[] file, object options)
        {
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(content, "file", "Student Admission.xlsx");
            form.Add(new StringContent(JsonSerializer.Serialize(options)), "options");
            var response = await client.PostAsync(url, form);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} → {(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body);
        }

        private static JsonElement RowFor(JsonDocument preview, Guid registrationId) =>
            preview.RootElement.GetProperty("rows").EnumerateArray()
                .Single(r => r.GetProperty("student").ValueKind == JsonValueKind.Object
                    && r.GetProperty("student").GetProperty("registrationId").GetGuid() == registrationId);

        private static string Outcome(JsonElement row, string code) =>
            row.GetProperty("changes").EnumerateArray()
                .Single(c => c.GetProperty("requirementCode").GetString() == code)
                .GetProperty("outcome").GetString()!;

        [SqlFact]
        public async Task An_office_sheet_matched_by_name_updates_the_checklist_and_learns_the_student_id()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync()); // ITP, new student
            await using (var scope = app.Scope(out var db))
            {
                var reg = await db.StudentRegistrations.Include(r => r.Documents).SingleAsync(r => r.Id == student.Id);
                reg.MiddleName = "SEÑALISTA";
                foreach (var code in new[] { "Form138_SF9", "PsaBirthCertificate" })
                    if (reg.Documents.All(d => d.RequirementCode != code))
                        db.RegistrationDocuments.Add(new RegistrationDocument { StudentRegistrationId = reg.Id, RequirementCode = code });
                await db.SaveChangesAsync();
                student.MiddleName = reg.MiddleName;
            }
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var studentId = $"0200{Random.Shared.Next(1_000_000, 9_999_999)}";
            // The office writes the middle name without its accent; HEPA A is not asked of ITP.
            var office = (student.LastName, student.FirstName, "SENALISTA");
            var file = OfficeSheet(office, studentId, form138: "✅ 9/23/2026", psa: "xerox", hepaA: "✅ Submitted");

            using var preview = await PostAsync(admin, "/api/documents/import/preview", file, new { });
            var row = RowFor(preview, student.Id);
            Assert.Equal("Matched", row.GetProperty("status").GetString());
            Assert.Equal("Name", row.GetProperty("matchedBy").GetString());
            Assert.Equal(studentId, row.GetProperty("studentIdToRecord").GetString());
            Assert.True(row.GetProperty("includeByDefault").GetBoolean());
            Assert.Equal("Update", Outcome(row, "Form138_SF9"));
            Assert.Equal("Update", Outcome(row, "PsaBirthCertificate"));
            Assert.Equal("NotRequired", Outcome(row, "HepaA"));

            var key = row.GetProperty("key").GetString();
            using var result = await PostAsync(admin, "/api/documents/import", file, new { include = new[] { key } });
            Assert.Equal(2, result.RootElement.GetProperty("papersUpdated").GetInt32());
            Assert.Equal(1, result.RootElement.GetProperty("studentIdsRecorded").GetInt32());

            await using (var scope = app.Scope(out var db))
            {
                var saved = await db.StudentRegistrations.AsNoTracking().Include(r => r.Documents).SingleAsync(r => r.Id == student.Id);
                Assert.Equal(studentId, saved.OfficialStudentNumber);
                Assert.Equal(DocumentStatus.Submitted, saved.Documents.Single(d => d.RequirementCode == "Form138_SF9").Status);
                Assert.Equal(DocumentStatus.XeroxCopy, saved.Documents.Single(d => d.RequirementCode == "PsaBirthCertificate").Status);
                Assert.NotNull(saved.Documents.Single(d => d.RequirementCode == "Form138_SF9").VerifiedByUserId);
                // "✅ 9/23/2026" is the day it came in; "xerox" gave no date, so today was recorded.
                Assert.Equal(new DateOnly(2026, 9, 23), saved.Documents.Single(d => d.RequirementCode == "Form138_SF9").SubmittedOn);
                Assert.Equal(Features.Documents.DocumentChecklist.Today(),
                    saved.Documents.Single(d => d.RequirementCode == "PsaBirthCertificate").SubmittedOn);
            }

            // The same sheet again, now saying the report card is missing: matched by the learnt ID,
            // and the original already on file is kept rather than downgraded.
            var stale = OfficeSheet(office, studentId, form138: "no req", psa: "xerox", hepaA: "");
            using var second = await PostAsync(admin, "/api/documents/import/preview", stale, new { });
            var again = RowFor(second, student.Id);
            Assert.Equal("Student ID", again.GetProperty("matchedBy").GetString());
            Assert.Equal("KeptHigher", Outcome(again, "Form138_SF9"));
            Assert.Equal("Unchanged", Outcome(again, "PsaBirthCertificate"));
            Assert.Equal(0, again.GetProperty("updateCount").GetInt32());
        }

        [SqlFact]
        public async Task The_export_carries_both_identifiers_and_reads_back_by_registration_number()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync());
            await using (var scope = app.Scope(out var db))
            {
                var reg = await db.StudentRegistrations.Include(r => r.Documents).SingleAsync(r => r.Id == student.Id);
                reg.OfficialStudentNumber = $"0201{Random.Shared.Next(1_000_000, 9_999_999)}";
                if (reg.Documents.All(d => d.RequirementCode != "GoodMoral"))
                    db.RegistrationDocuments.Add(new RegistrationDocument { StudentRegistrationId = reg.Id, RequirementCode = "GoodMoral" });
                await db.SaveChangesAsync();
                student.OfficialStudentNumber = reg.OfficialStudentNumber;
            }
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);

            var response = await admin.GetAsync($"/api/documents/export?search={student.StudentNumber}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();

            // Read it as the office would, mark the good moral received, and import it straight back.
            byte[] edited;
            using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
            {
                // One sheet per course; this ITP student is on the ITP sheet, which carries no
                // health-paper columns because ITP is not asked for them.
                Assert.Contains(workbook.Worksheets, w => w.Name == "ITP");
                var sheet = workbook.Worksheet("ITP");
                var headers = sheet.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
                var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == student.StudentNumber);
                Assert.Equal(student.OfficialStudentNumber, row.Cell(headers["Student ID"]).GetString());
                Assert.Equal("Not Submitted", row.Cell(headers["Certificate of Good Moral Character"]).GetString());
                Assert.False(headers.ContainsKey("Hepatitis A Vaccination Record"));
                Assert.True(headers.ContainsKey("Certificate of Good Moral Character — Date received"));
                row.Cell(headers["Certificate of Good Moral Character"]).Value = "Submitted";
                row.Cell(headers["Certificate of Good Moral Character — Date received"]).Value = new DateTime(2026, 10, 2);
                using var output = new MemoryStream();
                workbook.SaveAs(output);
                edited = output.ToArray();
            }

            using var preview = await PostAsync(admin, "/api/documents/import/preview", edited, new { });
            var match = RowFor(preview, student.Id);
            Assert.Equal("Registration no.", match.GetProperty("matchedBy").GetString());
            Assert.Equal("Update", Outcome(match, "GoodMoral"));
            var change = match.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("requirementCode").GetString() == "GoodMoral");
            Assert.Equal("2026-10-02", change.GetProperty("proposedDate").GetString());

            using var board = JsonDocument.Parse(await admin.GetStringAsync($"/api/documents?search={student.OfficialStudentNumber}"));
            var listed = board.RootElement.GetProperty("checklists").EnumerateArray()
                .Single(c => c.GetProperty("registrationId").GetGuid() == student.Id);
            Assert.Equal(student.OfficialStudentNumber, listed.GetProperty("officialStudentNumber").GetString());
        }
        [SqlFact]
        public async Task The_board_records_the_date_received_and_clears_it_when_a_paper_goes_back()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync());
            Guid documentId;
            await using (var scope = app.Scope(out var db))
            {
                var doc = new RegistrationDocument { StudentRegistrationId = student.Id, RequirementCode = "GoodMoral" };
                if (await db.RegistrationDocuments.AnyAsync(d => d.StudentRegistrationId == student.Id && d.RequirementCode == "GoodMoral"))
                    doc = await db.RegistrationDocuments.SingleAsync(d => d.StudentRegistrationId == student.Id && d.RequirementCode == "GoodMoral");
                else db.RegistrationDocuments.Add(doc);
                await db.SaveChangesAsync();
                documentId = doc.Id;
            }
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);

            async Task<(HttpStatusCode Code, DateOnly? On)> PutAsync(object body)
            {
                var response = await admin.PutAsync($"/api/documents/{documentId}",
                    new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"));
                await using var scope = app.Scope(out var db);
                var on = (await db.RegistrationDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId)).SubmittedOn;
                return (response.StatusCode, on);
            }

            // Marked received with no date: today.
            var first = await PutAsync(new { status = "Submitted" });
            Assert.Equal(HttpStatusCode.OK, first.Code);
            Assert.Equal(Features.Documents.DocumentChecklist.Today(), first.On);

            // Only the date corrected — handed in earlier than it was encoded.
            var corrected = await PutAsync(new { submittedOn = "2026-08-14" });
            Assert.Equal(HttpStatusCode.OK, corrected.Code);
            Assert.Equal(new DateOnly(2026, 8, 14), corrected.On);

            // A date in the future is refused and changes nothing.
            var future = await PutAsync(new { submittedOn = Features.Documents.DocumentChecklist.Today().AddDays(3).ToString("yyyy-MM-dd") });
            Assert.Equal(HttpStatusCode.BadRequest, future.Code);
            Assert.Equal(new DateOnly(2026, 8, 14), future.On);

            // Back to not submitted: no date.
            var back = await PutAsync(new { status = "NotSubmitted" });
            Assert.Equal(HttpStatusCode.OK, back.Code);
            Assert.Null(back.On);
        }
    }
}
