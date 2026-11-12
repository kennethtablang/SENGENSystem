using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The pass-11 endpoints through the real pipeline: a student correcting their own SIS (F-04),
    /// the Registrar's queue carrying the enrolled marker and the duplicate flag (F-14, F-03), and
    /// the seat-count reconcile (F-08 follow-up). Unit tests cover their policy; these cover the
    /// wiring, the authorization, and the SQL.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class SelfServiceAndQueueTests(SengenAppFactory app)
    {
        private readonly EnlistmentScenario scenario = new(app);

        private async Task SetStatusAsync(StudentRegistration student, RegistrationStatus status)
        {
            await using var scope = app.Scope(out var db);
            (await db.StudentRegistrations.SingleAsync(r => r.Id == student.Id)).Status = status;
            await db.SaveChangesAsync();
        }

        // ---- F-04: My SIS ----

        [SqlFact]
        public async Task A_student_corrects_their_own_pending_sis_in_capitals_and_the_account_name_follows()
        {
            var term = await scenario.ActiveTermAsync();
            var (student, email) = await scenario.StudentAsync(term);
            await SetStatusAsync(student, RegistrationStatus.Submitted);
            var client = await app.SignedInAsync(email, StudentPassword);

            var response = await client.PutAsJsonAsync("/api/registration/mine",
                new { firstName = "  juan   carlos ", birthplace = "alaminos city" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = app.Scope(out var db);
            var saved = await db.StudentRegistrations.SingleAsync(r => r.Id == student.Id);
            Assert.Equal("JUAN CARLOS", saved.FirstName);
            Assert.Equal("ALAMINOS CITY", saved.Birthplace);
            // The provisioned account greets the student by the corrected name.
            Assert.Equal("Juan Carlos", (await db.Users.SingleAsync(u => u.Id == saved.UserId)).FirstName);
            Assert.True(await db.AuditEntries.AnyAsync(a =>
                a.Action == AuditAction.RegistrationSelfCorrected && a.EntityId == student.Id.ToString()));
        }

        [SqlFact]
        public async Task A_confirmed_sis_is_locked_to_the_student()
        {
            var term = await scenario.ActiveTermAsync();
            var (student, email) = await scenario.StudentAsync(term); // Confirmed by default
            var client = await app.SignedInAsync(email, StudentPassword);

            var mine = await client.GetFromJsonAsync<JsonElement>("/api/registration/mine");
            Assert.False(mine.GetProperty("canEdit").GetBoolean());

            var response = await client.PutAsJsonAsync("/api/registration/mine", new { firstName = "CHANGED" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            await using var scope = app.Scope(out var db);
            Assert.Equal("INTEGRATION", (await db.StudentRegistrations.SingleAsync(r => r.Id == student.Id)).FirstName);
        }

        [SqlFact]
        public async Task Program_and_email_are_not_student_editable_and_required_fields_cannot_be_blanked()
        {
            var term = await scenario.ActiveTermAsync();
            var (student, email) = await scenario.StudentAsync(term);
            await SetStatusAsync(student, RegistrationStatus.Submitted);
            var client = await app.SignedInAsync(email, StudentPassword);

            // Unknown properties are ignored by the binder: the request type has no program or email.
            (await client.PutAsJsonAsync("/api/registration/mine", new { program = "HRA", email = "x@example.test" }))
                .EnsureSuccessStatusCode();
            var blanked = await client.PutAsJsonAsync("/api/registration/mine", new { lastName = "   " });
            Assert.Equal(HttpStatusCode.BadRequest, blanked.StatusCode);

            await using var scope = app.Scope(out var db);
            var saved = await db.StudentRegistrations.SingleAsync(r => r.Id == student.Id);
            Assert.Equal(ProgramTrack.ITP, saved.Program);
            Assert.Equal(email.ToUpperInvariant(), saved.Email);
            Assert.StartsWith("TEST", saved.LastName);
        }

        [SqlFact]
        public async Task Staff_cannot_use_the_student_self_service_route()
        {
            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            Assert.Equal(HttpStatusCode.Forbidden, (await registrar.GetAsync("/api/registration/mine")).StatusCode);
        }

        // ---- F-14 / F-03 on the Registrar's queue ----

        [SqlFact]
        public async Task The_queue_marks_an_enrolled_student_and_flags_a_likely_duplicate()
        {
            var term = await scenario.ActiveTermAsync();
            var (student, _) = await scenario.StudentAsync(term);
            var (twin, _) = await scenario.StudentAsync(term);
            await using (var scope = app.Scope(out var db))
            {
                // Same person, second mailbox: same name and birth date as the first.
                var row = await db.StudentRegistrations.SingleAsync(r => r.Id == twin.Id);
                row.LastName = student.LastName;
                await db.SaveChangesAsync();
            }

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var page = await registrar.GetFromJsonAsync<JsonElement>(
                $"/api/registration?search={Uri.EscapeDataString(student.StudentNumber)}&pageSize=200");
            var row1 = page.GetProperty("registrations").EnumerateArray()
                .Single(r => r.GetProperty("id").GetGuid() == student.Id);

            Assert.Equal(1, row1.GetProperty("likelyDuplicateCount").GetInt32());
            // The plan resolves (an ITP curriculum exists); with nothing approved it reads as not started.
            Assert.Equal(JsonValueKind.Object, row1.GetProperty("enrollment").ValueKind);

            var detail = await registrar.GetFromJsonAsync<JsonElement>($"/api/registration/{student.Id}");
            Assert.Contains(detail.GetProperty("likelyDuplicates").EnumerateArray(),
                d => d.GetProperty("id").GetGuid() == twin.Id);
        }

        // ---- F-08 follow-up: seat-count reconcile ----

        [SqlFact]
        public async Task Drift_is_listed_and_corrected_on_purpose_and_audited()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 5);
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(student, section, SlotRequestStatus.Approved); // count 1, truthful
            await using (var scope = app.Scope(out var db))
            {
                (await db.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount = 3; // drift
                await db.SaveChangesAsync();
            }

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var list = await registrar.GetFromJsonAsync<JsonElement>("/api/enlistment/seat-counts/mismatches");
            var mine = list.GetProperty("sections").EnumerateArray()
                .Single(m => m.GetProperty("sectionId").GetGuid() == section.Id);
            Assert.Equal(3, mine.GetProperty("enrolledCount").GetInt32());
            Assert.Equal(1, mine.GetProperty("approvedRequests").GetInt32());

            var fix = await registrar.PostAsync($"/api/enlistment/seat-counts/{section.Id}/reconcile", null);
            Assert.Equal(HttpStatusCode.OK, fix.StatusCode);

            await using var check = app.Scope(out var after);
            Assert.Equal(1, (await after.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount);
            Assert.Equal(1, await after.AuditEntries.CountAsync(a =>
                a.Action == AuditAction.SeatCountReconciled && a.EntityId == section.Id.ToString()));
        }

        [SqlFact]
        public async Task Reconcile_refuses_when_more_students_are_approved_than_there_are_seats()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 2);
            foreach (var _ in Enumerable.Range(0, 2))
            {
                var (s, _) = await scenario.StudentAsync(term);
                await scenario.RequestAsync(s, section, SlotRequestStatus.Approved);
            }
            await using (var scope = app.Scope(out var db))
            {
                // Capacity lowered underneath two approvals: three live approvals in a two-seat
                // section is a decision for a person, and the CHECK forbids writing 3 anyway.
                var (third, _) = await scenario.StudentAsync(term);
                db.SlotRequests.Add(new SlotRequest
                {
                    StudentRegistrationId = third.Id, SectionId = section.Id, Status = SlotRequestStatus.Approved
                });
                await db.SaveChangesAsync();
            }

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var response = await registrar.PostAsync($"/api/enlistment/seat-counts/{section.Id}/reconcile", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("Raise its capacity", await MessageAsync(response));
        }
    }
}
