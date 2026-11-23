using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The shared email frame: the footer names the configured institution (filled at send time),
    /// and the reminder no longer tells a student that every missing paper blocks enlistment.
    /// </summary>
    public class EmailLayoutTests
    {
        [Fact]
        public void The_footer_carries_a_slot_that_branding_fills_with_the_configured_institution()
        {
            var body = EmailLayout.Wrap("<p>Hello</p>");
            Assert.Contains(EmailLayout.InstitutionSlot, body);

            var sent = EmailLayout.Brand(body, "STI College Dagupan");
            Assert.DoesNotContain(EmailLayout.InstitutionSlot, sent);
            Assert.Contains("STI College Dagupan — SEN-GEN", sent);
        }

        [Fact]
        public void A_blank_name_falls_back_to_the_default_and_markup_in_the_name_is_escaped()
        {
            var body = EmailLayout.Wrap("");
            Assert.Contains(SystemSettings.DefaultInstitutionName, EmailLayout.Brand(body, "  "));
            Assert.Contains("A &amp; B &lt;b&gt;", EmailLayout.Brand(body, "A & B <b>"));
        }

        [Fact]
        public void The_reminder_marks_only_the_papers_that_hold_up_enlistment()
        {
            var student = new StudentRegistration { FirstName = "JUAN", StudentNumber = "2026-000001" };

            var (_, body) = DocumentEmails.SubmissionReminder(
                student, ["Form 138", "PSA birth certificate"], blocking: ["Form 138"]);

            Assert.Contains("<strong>Form 138</strong> — needed before you can enlist", body);
            Assert.Contains("<li>PSA birth certificate</li>", body);
            Assert.DoesNotContain("cannot be cleared for subject enlistment", body);
        }

        [Fact]
        public void With_nothing_blocking_the_reminder_says_enlistment_is_not_held_up()
        {
            var student = new StudentRegistration { FirstName = "JUAN", StudentNumber = "2026-000001" };

            var (_, body) = DocumentEmails.SubmissionReminder(student, ["PSA birth certificate"], blocking: []);

            Assert.Contains("None of these hold up your subject enlistment", body);
        }
    }
}
