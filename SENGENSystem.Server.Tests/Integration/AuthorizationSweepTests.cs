using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// From the pass-16 sweep of every route's authorization. The sweep found the per-object checks
    /// sound — the one student route that takes an id checks ownership — and the anonymous forms
    /// unlimited. These pin both: one student cannot touch another's seat, and the public lookup
    /// stops answering a script.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class AuthorizationSweepTests(SengenAppFactory app)
    {
        private readonly EnlistmentScenario scenario = new(app);

        [SqlFact]
        public async Task A_student_cannot_cancel_or_drop_another_students_seat()
        {
            var term = await scenario.ActiveTermAsync();
            var subject = (await scenario.SubjectsAsync(term, 1))[0];
            var section = await scenario.SectionAsync(subject, term);
            var (owner, _) = await scenario.StudentAsync(term);
            var (_, otherEmail) = await scenario.StudentAsync(term);
            var pending = await scenario.RequestAsync(owner, section);
            var approved = await scenario.RequestAsync(owner, await scenario.SectionAsync(
                (await scenario.SubjectsAsync(term, 1))[0], term), SlotRequestStatus.Approved);

            var other = await app.SignedInAsync(otherEmail, StudentPassword);

            // 404, not 403: the request's existence is not confirmed to someone it doesn't belong to.
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/enlistment/requests/{pending}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/enlistment/requests/{approved}")).StatusCode);

            await using var scope = app.Scope(out var db);
            Assert.Equal(SlotRequestStatus.Requested, (await db.SlotRequests.SingleAsync(r => r.Id == pending)).Status);
            Assert.Equal(SlotRequestStatus.Approved, (await db.SlotRequests.SingleAsync(r => r.Id == approved)).Status);
        }

        [SqlFact]
        public async Task The_public_term_activation_lookup_stops_answering_a_script()
        {
            // Student number + surname → "is this a student?" is enumerable without a limit.
            var client = app.CreateClient();
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 35; i++)
            {
                var response = await client.PostAsJsonAsync("/api/registration/term-activation/lookup",
                    new { studentNumber = $"0200{i:D7}", lastName = "NOBODY" });
                statuses.Add(response.StatusCode);
            }

            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
            Assert.True(statuses.TakeWhile(s => s != HttpStatusCode.TooManyRequests).Count() >= 30,
                "The limit should allow a lab's worth of genuine lookups before refusing.");
        }
    }
}
