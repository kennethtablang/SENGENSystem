using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The Registrar's approval through the real endpoint: the one place a seat is taken
    /// (FR-ENL-04). Each test pins one decision the approval makes — grant, full, overlap, the
    /// cross-term non-clash F-09 fixed, and the last-seat race the retry loop exists for.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class ApprovalTests(SengenAppFactory app)
    {
        private readonly EnlistmentScenario scenario = new(app);

        private static readonly (DayOfWeek, int, int) Monday8To10 = (DayOfWeek.Monday, 8 * 60, 10 * 60);
        private static readonly (DayOfWeek, int, int) Monday9To11 = (DayOfWeek.Monday, 9 * 60, 11 * 60);
        private static readonly (DayOfWeek, int, int) Tuesday8To10 = (DayOfWeek.Tuesday, 8 * 60, 10 * 60);

        private async Task<HttpResponseMessage> ApproveAsync(Guid requestId)
        {
            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            return await registrar.PostAsync($"/api/enlistment/approvals/{requestId}/approve", null);
        }

        private async Task<(SlotRequestStatus Status, int Enrolled)> StateAsync(Guid requestId, Guid sectionId)
        {
            await using var scope = app.Scope(out var db);
            var status = (await db.SlotRequests.SingleAsync(r => r.Id == requestId)).Status;
            var enrolled = (await db.Sections.SingleAsync(s => s.Id == sectionId)).EnrolledCount;
            return (status, enrolled);
        }

        [SqlFact]
        public async Task Approving_takes_exactly_one_seat()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, publishedAt: Monday8To10);
            var (student, _) = await scenario.StudentAsync(term);
            var requestId = await scenario.RequestAsync(student, section);

            var response = await ApproveAsync(requestId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal((SlotRequestStatus.Approved, 1), await StateAsync(requestId, section.Id));
        }

        [SqlFact]
        public async Task A_full_section_refuses_and_leaves_the_request_pending()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 1, publishedAt: Monday8To10);
            var (holder, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(holder, section, SlotRequestStatus.Approved);
            var (late, _) = await scenario.StudentAsync(term);
            var requestId = await scenario.RequestAsync(late, section);

            var response = await ApproveAsync(requestId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("is full", await MessageAsync(response));
            // RevertApproval must undo the in-memory decision, or a skipped request reads approved.
            Assert.Equal((SlotRequestStatus.Requested, 1), await StateAsync(requestId, section.Id));
        }

        [SqlFact]
        public async Task An_overlapping_class_this_term_is_refused()
        {
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2);
            var held = await scenario.SectionAsync(subjects[0], term, publishedAt: Monday8To10);
            var clashing = await scenario.SectionAsync(subjects[1], term, publishedAt: Monday9To11);
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(student, held, SlotRequestStatus.Approved);
            var requestId = await scenario.RequestAsync(student, clashing);

            var response = await ApproveAsync(requestId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("overlapping", await MessageAsync(response));
            Assert.Equal((SlotRequestStatus.Requested, 0), await StateAsync(requestId, clashing.Id));
        }

        [SqlFact]
        public async Task Back_to_back_classes_on_different_days_are_not_a_clash()
        {
            var term = await scenario.ActiveTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2);
            var held = await scenario.SectionAsync(subjects[0], term, publishedAt: Monday8To10);
            var other = await scenario.SectionAsync(subjects[1], term, publishedAt: Tuesday8To10);
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(student, held, SlotRequestStatus.Approved);
            var requestId = await scenario.RequestAsync(student, other);

            Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(requestId)).StatusCode);
        }

        [SqlFact]
        public async Task Last_terms_class_at_the_same_time_is_not_a_clash()
        {
            // F-09 as a regression test: the approval leg used to compare against approvals from
            // every term, so a returning student got a false "overlapping classes" refusal against
            // a class that ended last semester — and the only offered remedy was to reject.
            var term = await scenario.ActiveTermAsync();
            var past = await scenario.PastTermAsync();
            var subjects = await scenario.SubjectsAsync(term, 2);
            var lastTerms = await scenario.SectionAsync(subjects[0], past, publishedAt: Monday8To10);
            var thisTerms = await scenario.SectionAsync(subjects[1], term, publishedAt: Monday8To10);
            var (student, _) = await scenario.StudentAsync(term);
            await scenario.RequestAsync(student, lastTerms, SlotRequestStatus.Approved);
            var requestId = await scenario.RequestAsync(student, thisTerms);

            var response = await ApproveAsync(requestId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal((SlotRequestStatus.Approved, 1), await StateAsync(requestId, thisTerms.Id));
        }

        [SqlFact]
        public async Task Two_approvals_racing_for_the_last_seat_grant_exactly_one()
        {
            // The race the RowVersion retry loop exists for, through the real endpoint: two
            // Registrars, two students, one seat. Exactly one is granted, the other is told the
            // section is full, and the count ends at the capacity — never above it.
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 1, publishedAt: Monday8To10);
            var (first, _) = await scenario.StudentAsync(term);
            var (second, _) = await scenario.StudentAsync(term);
            var a = await scenario.RequestAsync(first, section);
            var b = await scenario.RequestAsync(second, section);

            var registrarA = await app.SignedInAsync(Registrar, StaffPassword);
            var registrarB = await app.SignedInAsync(Registrar, StaffPassword);
            var responses = await Task.WhenAll(
                registrarA.PostAsync($"/api/enlistment/approvals/{a}/approve", null),
                registrarB.PostAsync($"/api/enlistment/approvals/{b}/approve", null));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            await using var scope = app.Scope(out var db);
            Assert.Equal(1, (await db.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount);
            Assert.Equal(1, await db.SlotRequests.CountAsync(r =>
                r.SectionId == section.Id && r.Status == SlotRequestStatus.Approved));
        }

        // ---- Capacity override (FR-ENL-10): within the institutional cap and the room ----

        private async Task<HttpResponseMessage> OverrideAsync(Guid sectionId, int capacity)
        {
            var registrar = await app.SignedInAsync(Registrar, StaffPassword);
            return await registrar.PostAsJsonAsync(
                $"/api/enlistment/approvals/sections/{sectionId}/capacity", new { capacity, reason = "test" });
        }

        private async Task<int> CapacityAsync(Guid sectionId)
        {
            await using var scope = app.Scope(out var db);
            return (await db.Sections.SingleAsync(s => s.Id == sectionId)).Capacity;
        }

        [SqlFact]
        public async Task An_override_past_the_institutional_seat_cap_is_refused()
        {
            // The override used to accept anything up to a hard-coded 200, so "max 40 per section"
            // held only until someone typed a bigger number.
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 1, publishedAt: Monday8To10);
            int cap;
            await using (var scope = app.Scope(out var db))
            {
                cap = (await db.GetSettingsAsync()).SectionCapacityCap;
            }

            var response = await OverrideAsync(section.Id, cap + 1);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("institutional seat cap", await MessageAsync(response));
            Assert.Equal(1, await CapacityAsync(section.Id));
        }

        [SqlFact]
        public async Task An_override_past_the_room_the_section_meets_in_is_refused()
        {
            // A section can't seat more students than its smallest room holds — the computer lab's
            // PC count, say — however much room the institutional cap leaves.
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term, capacity: 3);
            var roomName = $"Small room {Guid.NewGuid().ToString("N")[..6]}";
            await using (var scope = app.Scope(out var db))
            {
                var room = new Room { Name = roomName, Capacity = 5, Kind = RoomKind.LectureRoom };
                var slot = new TimeSlot { Day = DayOfWeek.Friday, StartMinutes = 13 * 60, EndMinutes = 15 * 60, IsAllowable = false };
                db.Rooms.Add(room);
                db.TimeSlots.Add(slot);
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = term.Id,
                    SectionId = section.Id,
                    Room = room,
                    TimeSlot = slot,
                    FacultyProfileId = await db.FacultyProfiles.OrderBy(p => p.EmployeeId).Select(p => p.Id).FirstAsync(),
                    IsFinalized = true,
                    IsPublished = true
                });
                await db.SaveChangesAsync();
            }

            var over = await OverrideAsync(section.Id, 6);
            Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
            Assert.Contains(roomName, await MessageAsync(over));
            Assert.Equal(3, await CapacityAsync(section.Id));

            var fits = await OverrideAsync(section.Id, 5);
            Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
            Assert.Equal(5, await CapacityAsync(section.Id));
        }
    }
}
