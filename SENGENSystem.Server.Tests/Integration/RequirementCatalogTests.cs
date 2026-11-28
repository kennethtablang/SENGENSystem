using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// Editing an admission requirement (FR-DOC-01). Every PUT used to 500: the program links were
    /// cleared and re-created with client-generated keys, which EF took for existing rows and
    /// tried to UPDATE, matching nothing.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class RequirementCatalogTests(SengenAppFactory app)
    {
        private const string SchoolAdmin = "admin@stialaminos.local";
        private const string SchoolAdminPassword = "Admin@Sengen2026";

        private static async Task<Guid> CreateAsync(HttpClient admin, string name, string[] programs)
        {
            var response = await admin.PostAsJsonAsync("/api/requirements", new
            {
                name,
                programs,
                studentTypes = new[] { "NewStudent" },
                isRequiredForAuthorization = false
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("id").GetGuid();
        }

        [SqlFact]
        public async Task Toggling_the_authorization_gate_with_the_same_programs_saves()
        {
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var id = await CreateAsync(admin, $"Gate Toggle {Guid.NewGuid():N}", ["ITP", "HRS"]);

            var response = await admin.PutAsJsonAsync($"/api/requirements/{id}", new
            {
                name = "Gate Toggle Renamed",
                programs = new[] { "ITP", "HRS" },
                studentTypes = new[] { "NewStudent" },
                isRequiredForAuthorization = true
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = app.Scope(out var db);
            var saved = await db.AdmissionRequirements.AsNoTracking().Include(r => r.Programs).SingleAsync(r => r.Id == id);
            Assert.True(saved.IsRequiredForAuthorization);
            Assert.Equal(["HRS", "ITP"], saved.Programs.Select(p => p.Program.ToString()).OrderBy(p => p));
        }

        [SqlFact]
        public async Task Changing_the_program_set_adds_and_removes_links()
        {
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var id = await CreateAsync(admin, $"Program Swap {Guid.NewGuid():N}", ["ITP", "HRS"]);

            var response = await admin.PutAsJsonAsync($"/api/requirements/{id}", new
            {
                name = "Program Swap",
                programs = new[] { "HRS", "HRA" },
                studentTypes = new[] { "NewStudent" }
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = app.Scope(out var db);
            var programs = await db.AdmissionRequirementPrograms.AsNoTracking()
                .Where(p => p.AdmissionRequirementId == id)
                .Select(p => p.Program).ToListAsync();
            Assert.Equal(["HRA", "HRS"], programs.Select(p => p.ToString()).OrderBy(p => p));
        }

        private static async Task<List<string>> ChecklistLabelsAsync(HttpClient admin, Guid registrationId)
        {
            using var body = JsonDocument.Parse(await admin.GetStringAsync($"/api/registration/{registrationId}"));
            return body.RootElement.GetProperty("documents").EnumerateArray()
                .Select(d => d.GetProperty("label").GetString()!).ToList();
        }

        [SqlFact]
        public async Task A_requirement_added_later_reaches_students_who_already_registered()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync()); // ITP, new student
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var name = $"X-Ray {Guid.NewGuid():N}";

            await CreateAsync(admin, name, ["ITP"]);

            Assert.Contains(name, await ChecklistLabelsAsync(admin, student.Id));
        }

        /// <summary>
        /// The live case: the Chest X-ray applied to HRA/HRS only, yet ITP students already held
        /// rows for it (some marked submitted). Ticking ITP on the requirement must put it on every
        /// ITP student's Document Requirements checklist, keeping the statuses already recorded.
        /// </summary>
        [SqlFact]
        public async Task Adding_a_program_shows_the_paper_on_the_documents_board_and_keeps_recorded_statuses()
        {
            var scenario = new EnlistmentScenario(app);
            var term = await scenario.ActiveTermAsync();
            var (withRow, _) = await scenario.StudentAsync(term);   // ITP, already holds a submitted row
            var (withoutRow, _) = await scenario.StudentAsync(term); // ITP, no row yet
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var name = $"Chest X-ray {Guid.NewGuid():N}";
            var id = await CreateAsync(admin, name, ["HRA", "HRS"]);

            string code;
            await using (var scope = app.Scope(out var db))
            {
                code = (await db.AdmissionRequirements.SingleAsync(r => r.Id == id)).Code;
                db.RegistrationDocuments.Add(new Domain.RegistrationDocument
                {
                    StudentRegistrationId = withRow.Id, RequirementCode = code, Status = Domain.DocumentStatus.Submitted
                });
                await db.SaveChangesAsync();
            }

            var response = await admin.PutAsJsonAsync($"/api/requirements/{id}", new
            {
                name, programs = new[] { "HRA", "HRS", "ITP" }, studentTypes = new[] { "NewStudent" }
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            async Task<string?> BoardStatusAsync(StudentRegistrationRef student)
            {
                using var body = JsonDocument.Parse(
                    await admin.GetStringAsync($"/api/documents?search={student.StudentNumber}"));
                var row = body.RootElement.GetProperty("checklists").EnumerateArray()
                    .Single(c => c.GetProperty("registrationId").GetGuid() == student.Id);
                return row.GetProperty("documents").EnumerateArray()
                    .Where(d => d.GetProperty("label").GetString() == name)
                    .Select(d => d.GetProperty("status").GetString())
                    .SingleOrDefault();
            }

            Assert.Equal("Submitted", await BoardStatusAsync(new(withRow.Id, withRow.StudentNumber)));
            Assert.Equal("NotSubmitted", await BoardStatusAsync(new(withoutRow.Id, withoutRow.StudentNumber)));
        }

        private sealed record StudentRegistrationRef(Guid Id, string StudentNumber);

        [SqlFact]
        public async Task Widening_a_requirement_backfills_and_narrowing_it_hides_it_again()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync()); // ITP, new student
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);
            var name = $"Drug Test {Guid.NewGuid():N}";
            var id = await CreateAsync(admin, name, ["HRS"]);
            Assert.DoesNotContain(name, await ChecklistLabelsAsync(admin, student.Id));

            Task<HttpResponseMessage> SetProgramsAsync(string[] programs) =>
                admin.PutAsJsonAsync($"/api/requirements/{id}", new
                {
                    name, programs, studentTypes = new[] { "NewStudent" }
                });

            Assert.Equal(HttpStatusCode.OK, (await SetProgramsAsync(["HRS", "ITP"])).StatusCode);
            Assert.Contains(name, await ChecklistLabelsAsync(admin, student.Id));

            Assert.Equal(HttpStatusCode.OK, (await SetProgramsAsync(["HRS"])).StatusCode);
            Assert.DoesNotContain(name, await ChecklistLabelsAsync(admin, student.Id));

            // Widening again must not trip the unique (registration, code) index on the kept row.
            Assert.Equal(HttpStatusCode.OK, (await SetProgramsAsync(["HRS", "ITP"])).StatusCode);
            Assert.Contains(name, await ChecklistLabelsAsync(admin, student.Id));
        }
    }
}
