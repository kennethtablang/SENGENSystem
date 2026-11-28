using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// Bulk pre-authorization (FR-PRE-02/04) from the "Start selecting students" mode: every
    /// selected student passes the same gate as a single grant, and a blocked one is skipped with
    /// its reason rather than failing the run.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class BulkPreAuthorizationTests(SengenAppFactory app)
    {
        private const string SchoolAdmin = "admin@stialaminos.local";
        private const string SchoolAdminPassword = "Admin@Sengen2026";

        private async Task<Guid> StudentAsync(RegistrationStatus status)
        {
            var scenario = new EnlistmentScenario(app);
            var (student, _) = await scenario.StudentAsync(await scenario.ActiveTermAsync());
            await using var scope = app.Scope(out var db);
            var row = await db.StudentRegistrations.SingleAsync(r => r.Id == student.Id);
            row.IsPreAuthorized = false;
            row.Status = status;
            await db.SaveChangesAsync();
            return student.Id;
        }

        [SqlFact]
        public async Task Eligible_students_are_cleared_and_blocked_ones_are_skipped_with_a_reason()
        {
            var ready1 = await StudentAsync(RegistrationStatus.Confirmed);
            var ready2 = await StudentAsync(RegistrationStatus.Confirmed);
            var unconfirmed = await StudentAsync(RegistrationStatus.Submitted);
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);

            var response = await admin.PostAsJsonAsync("/api/pre-authorization/bulk",
                new { registrationIds = new[] { ready1, ready2, unconfirmed } });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(2, body.RootElement.GetProperty("authorizedCount").GetInt32());
            var skipped = body.RootElement.GetProperty("outcomes").EnumerateArray()
                .Where(o => !o.GetProperty("authorized").GetBoolean()).ToList();
            var only = Assert.Single(skipped);
            Assert.Equal(unconfirmed, only.GetProperty("registrationId").GetGuid());
            Assert.Contains("confirm", only.GetProperty("reasons")[0].GetString());

            await using var scope = app.Scope(out var db);
            var states = await db.StudentRegistrations.AsNoTracking()
                .Where(r => r.Id == ready1 || r.Id == ready2 || r.Id == unconfirmed)
                .ToDictionaryAsync(r => r.Id, r => r.IsPreAuthorized);
            Assert.True(states[ready1]);
            Assert.True(states[ready2]);
            Assert.False(states[unconfirmed]);
        }

        [SqlFact]
        public async Task An_empty_selection_is_refused()
        {
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);

            var response = await admin.PostAsJsonAsync("/api/pre-authorization/bulk",
                new { registrationIds = Array.Empty<Guid>() });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
