using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The gates that matter most, exercised through the real HTTP pipeline against SQL Server.
    /// <para>
    /// The F-10 prerequisite gate was the one part of that fix live verification could not reach
    /// (the dev database had no published section for a subject with a prerequisite), and the
    /// unit tests cover the helper but not the two endpoints that call it. These close that: the
    /// request leg refuses and audits, the request leg falls open for a student with no history,
    /// and the approval leg refuses a request that reached the queue anyway. F-15's publish guard
    /// is covered the same way, preview and publish agreeing on the refusal.
    /// </para>
    /// </summary>
    [Collection(AppCollection.Name)]
    public class EndpointGateTests(SengenAppFactory app)
    {
        private readonly EnlistmentScenario scenario = new(app);

        [SqlFact]
        public async Task Request_leg_refuses_a_subject_whose_prerequisite_is_unmet_and_audits_it()
        {
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2, secondRequiresFirst: true);
            var section = await scenario.SectionAsync(subjects[1], term);
            var (student, email) = await scenario.StudentAsync(term);
            await scenario.RecordAsync(student, subjects[0], term, SubjectVerdict.Failed); // history on file, prerequisite not passed

            var client = await app.SignedInAsync(email, StudentPassword);
            var response = await client.PostAsJsonAsync("/api/enlistment/requests", new { sectionId = section.Id });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(subjects[0].Code, await MessageAsync(response));

            await using var scope = app.Scope(out var db);
            Assert.False(await db.SlotRequests.AnyAsync(r => r.StudentRegistrationId == student.Id));
            Assert.True(await db.AuditEntries.AnyAsync(a =>
                a.Action == AuditAction.PrerequisiteBlocked && a.EntityId == student.Id.ToString()));
        }

        [SqlFact]
        public async Task Request_leg_falls_open_for_a_student_with_no_history_on_file()
        {
            // An empty record is not "every prerequisite unmet" (AcademicHistory.IsEnforceable).
            // The request must get past the prerequisite gate — here it then stops at the next
            // check, because the test section has no published meetings.
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2, secondRequiresFirst: true);
            var section = await scenario.SectionAsync(subjects[1], term);
            var (_, email) = await scenario.StudentAsync(term);

            var client = await app.SignedInAsync(email, StudentPassword);
            var response = await client.PostAsJsonAsync("/api/enlistment/requests", new { sectionId = section.Id });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("not been published", await MessageAsync(response));
        }

        [SqlFact]
        public async Task Approval_leg_refuses_a_request_whose_prerequisite_is_unmet()
        {
            // A request that reached the queue anyway — filed before the record changed, or by a
            // path that skipped the request leg. A seat must not be granted on a check that is no
            // longer true.
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2, secondRequiresFirst: true);
            var section = await scenario.SectionAsync(subjects[1], term);
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RecordAsync(student, subjects[0], term, SubjectVerdict.Failed);
            var requestId = await scenario.RequestAsync(student, section);

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var response = await registrar.PostAsync($"/api/enlistment/approvals/{requestId}/approve", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(subjects[0].Code, await MessageAsync(response));

            await using var check = app.Scope(out var after);
            Assert.Equal(SlotRequestStatus.Requested, (await after.SlotRequests.SingleAsync(r => r.Id == requestId)).Status);
            Assert.Equal(0, (await after.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount);
        }

        [SqlFact]
        public async Task Publish_refuses_an_unfinalized_draft_and_the_preview_says_so_first()
        {
            Guid semesterId, assignmentId;
            await using (var scope = app.Scope(out var db))
            {
                var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                var term = new Semester
                {
                    Name = $"Publish gate {tag}",
                    StartDate = new DateOnly(2027, 1, 10),
                    EndDate = new DateOnly(2027, 5, 30)
                };
                var subject = new Subject { Code = $"PG{tag}", Title = "Publish gate", Units = 3, Hours = 3, LectureHours = 3, ProgramCode = "ITP", YearLevel = 1 };
                var section = new Section { Subject = subject, Semester = term, SectionCode = $"ITP-1Z-PG{tag}", ProgramCode = "ITP", YearLevel = 1, Block = "Z" };
                var assignment = new ScheduleAssignment
                {
                    SemesterId = term.Id,
                    Section = section,
                    RoomId = await db.Rooms.Select(r => r.Id).FirstAsync(),
                    TimeSlotId = await db.TimeSlots.Select(t => t.Id).FirstAsync(),
                    FacultyProfileId = await db.FacultyProfiles.Select(p => p.Id).FirstAsync(),
                    IsFinalized = false
                };
                db.AddRange(term, subject, section, assignment);
                await db.SaveChangesAsync();
                (semesterId, assignmentId) = (term.Id, assignment.Id);
            }

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);

            var preview = await registrar.GetAsync($"/api/publishing/{semesterId}/preview");
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            using (var doc = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, doc.RootElement.GetProperty("notFinalized").GetInt32());
                Assert.Contains("finalize", doc.RootElement.GetProperty("blockedReason").GetString());
            }

            var publish = await registrar.PostAsync($"/api/publishing/{semesterId}/publish", null);
            Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);

            await using var check = app.Scope(out var after);
            Assert.False((await after.ScheduleAssignments.SingleAsync(a => a.Id == assignmentId)).IsPublished);
        }
    }
}
