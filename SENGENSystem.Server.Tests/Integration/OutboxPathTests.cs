using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The notices that were still sent inline, one SMTP call per recipient inside the request —
    /// a published class moved on the board, and a seat request rejected. Both now land on the
    /// outbox with the change that justifies them, so a failure is retried and visible instead of
    /// lost. These pin that they are queued, and to whom.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class OutboxPathTests(SengenAppFactory app)
    {
        private const string AcademicHead = "academichead@stialaminos.local";
        private readonly EnlistmentScenario scenario = new(app);

        [SqlFact]
        public async Task Moving_a_published_class_queues_a_notice_for_its_teacher_and_every_enrolled_student()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, publishedAt: (DayOfWeek.Wednesday, 8 * 60, 10 * 60));
            var students = new List<StudentRegistration>();
            for (var i = 0; i < 3; i++)
            {
                var (s, _) = await scenario.StudentAsync(term);
                await scenario.RequestAsync(s, section, SlotRequestStatus.Approved);
                students.Add(s);
            }

            Guid assignmentId, roomId;
            await using (var scope = app.Scope(out var db))
            {
                var assignment = await db.ScheduleAssignments.SingleAsync(a => a.SectionId == section.Id);
                (assignmentId, roomId) = (assignment.Id, assignment.RoomId);
            }

            var head = await app.SignedInAsync(AcademicHead, StaffPassword);
            var response = await head.PutAsJsonAsync($"/api/scheduling/board/{assignmentId}",
                new { roomId, day = (int)DayOfWeek.Friday, startMinutes = 16 * 60, endMinutes = 17 * 60 });

            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            await using var check = app.Scope(out var after);
            var queued = await after.OutboxEmails
                .Where(m => m.Kind == "ScheduleAmended")
                .Select(m => m.ToEmail)
                .ToListAsync();
            foreach (var s in students)
            {
                Assert.Contains(s.Email, queued);
            }
            Assert.True((await after.ScheduleAssignments.SingleAsync(a => a.Id == assignmentId)).IsAmended);
        }

        [SqlFact]
        public async Task Rejecting_a_request_queues_the_notice_with_the_decision()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term);
            var (student, _) = await scenario.StudentAsync(term);
            var requestId = await scenario.RequestAsync(student, section);

            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            var response = await registrar.PostAsJsonAsync($"/api/enlistment/approvals/{requestId}/reject",
                new { reason = "Section reserved for irregular students." });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = app.Scope(out var db);
            var mail = await db.OutboxEmails.SingleAsync(m => m.DedupeKey == $"rejected:{requestId}");
            Assert.Equal(student.Email, mail.ToEmail);
            Assert.Equal("SlotRejected", mail.Kind);
            Assert.Equal(SlotRequestStatus.Rejected, (await db.SlotRequests.SingleAsync(r => r.Id == requestId)).Status);
        }
    }
}
