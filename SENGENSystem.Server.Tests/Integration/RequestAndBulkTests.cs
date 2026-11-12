using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The rest of the seat lifecycle through the real endpoints: a student's request and the
    /// checks it runs (FR-ENL-04/07), the bulk approval's "approved 47 of 50, here is why three
    /// didn't" contract (FR-ENL-11), and a student dropping an approved seat, which is the only
    /// way besides a staff release that a seat comes back (F-08).
    /// </summary>
    [Collection(AppCollection.Name)]
    public class RequestAndBulkTests(SengenAppFactory app)
    {
        private readonly EnlistmentScenario scenario = new(app);

        private static readonly (DayOfWeek, int, int) Thursday8To10 = (DayOfWeek.Thursday, 8 * 60, 10 * 60);
        private static readonly (DayOfWeek, int, int) Thursday9To11 = (DayOfWeek.Thursday, 9 * 60, 11 * 60);
        private static readonly (DayOfWeek, int, int) Friday8To10 = (DayOfWeek.Friday, 8 * 60, 10 * 60);

        private static Task<HttpResponseMessage> RequestSeatAsync(HttpClient student, Section section) =>
            student.PostAsJsonAsync("/api/enlistment/requests", new { sectionId = section.Id });

        [SqlFact]
        public async Task An_eligible_student_can_request_a_published_seat()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, publishedAt: Thursday8To10);
            var (student, email) = await scenario.StudentAsync(term);

            var response = await RequestSeatAsync(await app.SignedInAsync(email, StudentPassword), section);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await using var scope = app.Scope(out var db);
            var row = await db.SlotRequests.SingleAsync(r => r.StudentRegistrationId == student.Id);
            Assert.Equal(SlotRequestStatus.Requested, row.Status);
            // A request is not a seat: nothing is taken until the Registrar approves.
            Assert.Equal(0, (await db.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount);
        }

        [SqlFact]
        public async Task A_second_section_of_the_same_subject_is_refused()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var first = await scenario.SectionAsync(subject, term, publishedAt: Thursday8To10);
            var second = await scenario.SectionAsync(subject, term, publishedAt: Friday8To10);
            var (_, email) = await scenario.StudentAsync(term);
            var client = await app.SignedInAsync(email, StudentPassword);

            Assert.Equal(HttpStatusCode.Created, (await RequestSeatAsync(client, first)).StatusCode);
            var response = await RequestSeatAsync(client, second);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("already have", await MessageAsync(response));
        }

        [SqlFact]
        public async Task A_request_that_clashes_with_one_already_held_is_refused_at_request_time()
        {
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2);
            var held = await scenario.SectionAsync(subjects[0], term, publishedAt: Thursday8To10);
            var clashing = await scenario.SectionAsync(subjects[1], term, publishedAt: Thursday9To11);
            var (_, email) = await scenario.StudentAsync(term);
            var client = await app.SignedInAsync(email, StudentPassword);

            Assert.Equal(HttpStatusCode.Created, (await RequestSeatAsync(client, held)).StatusCode);
            var response = await RequestSeatAsync(client, clashing);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("overlaps", await MessageAsync(response));
        }

        [SqlFact]
        public async Task The_per_student_unit_ceiling_is_enforced()
        {
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2); // 3 units each
            var a = await scenario.SectionAsync(subjects[0], term, publishedAt: Thursday8To10);
            var b = await scenario.SectionAsync(subjects[1], term, publishedAt: Friday8To10);
            var (_, email) = await scenario.StudentAsync(term);
            var client = await app.SignedInAsync(email, StudentPassword);

            int original;
            await using (var scope = app.Scope(out var db))
            {
                var settings = await db.GetSettingsForUpdateAsync();
                original = settings.MaxEnlistmentUnitsPerStudent;
                settings.MaxEnlistmentUnitsPerStudent = 3;
                await db.SaveChangesAsync();
            }
            try
            {
                Assert.Equal(HttpStatusCode.Created, (await RequestSeatAsync(client, a)).StatusCode);
                var response = await RequestSeatAsync(client, b);

                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Contains("3-unit enlistment ceiling", await MessageAsync(response));
            }
            finally
            {
                // Shared database: the next test must not inherit a 3-unit ceiling.
                await using var scope = app.Scope(out var db);
                (await db.GetSettingsForUpdateAsync()).MaxEnlistmentUnitsPerStudent = original;
                await db.SaveChangesAsync();
            }
        }

        [SqlFact]
        public async Task Bulk_approval_grants_what_fits_and_explains_what_it_skipped()
        {
            // One full section must not poison the run: three students want two seats in one
            // section, and a fourth wants a seat elsewhere. Three are granted, one is skipped with
            // its reason, and the counts end at capacity.
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2);
            var tight = await scenario.SectionAsync(subjects[0], term, capacity: 2, publishedAt: Thursday8To10);
            var roomy = await scenario.SectionAsync(subjects[1], term, publishedAt: Friday8To10);

            var ids = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                var (s, _) = await scenario.StudentAsync(term);
                ids.Add(await scenario.RequestAsync(s, tight));
            }
            var (other, _) = await scenario.StudentAsync(term);
            ids.Add(await scenario.RequestAsync(other, roomy));

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var response = await registrar.PostAsJsonAsync("/api/enlistment/approvals/bulk-approve", new { requestIds = ids });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, body.GetProperty("approvedCount").GetInt32());
            Assert.Equal(1, body.GetProperty("skippedCount").GetInt32());
            var skipped = body.GetProperty("outcomes").EnumerateArray().Single(o => !o.GetProperty("approved").GetBoolean());
            Assert.Contains("is full", skipped.GetProperty("reason").GetString());

            await using var scope = app.Scope(out var db);
            Assert.Equal(2, (await db.Sections.SingleAsync(s => s.Id == tight.Id)).EnrolledCount);
            Assert.Equal(1, (await db.Sections.SingleAsync(s => s.Id == roomy.Id)).EnrolledCount);
        }

        [SqlFact]
        public async Task A_student_dropping_an_approved_seat_gives_it_back()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, publishedAt: Thursday8To10);
            var (student, email) = await scenario.StudentAsync(term);
            var requestId = await scenario.RequestAsync(student, section, SlotRequestStatus.Approved);

            var client = await app.SignedInAsync(email, StudentPassword);
            var response = await client.DeleteAsync($"/api/enlistment/requests/{requestId}");

            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            await using var scope = app.Scope(out var db);
            Assert.Equal(SlotRequestStatus.Dropped, (await db.SlotRequests.SingleAsync(r => r.Id == requestId)).Status);
            Assert.Equal(0, (await db.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == AuditAction.SlotDropped));
        }
    }
}
