using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.EnrollmentCycle
{
    /// <summary>What a stage gate decided, and the sentence to show when it refused.</summary>
    public sealed record StageGate(bool Open, string? Reason)
    {
        public static readonly StageGate Allowed = new(true, null);

        public static StageGate Closed(string reason) => new(false, reason);
    }

    /// <summary>
    /// The enrollment cycle as a <b>rule</b> rather than a caption (FR-CYC-01).
    ///
    /// <para>
    /// The stage used to be purely decorative: it was set, audited, and shown in the top-bar ticker,
    /// but no slice read it. Enlistment was actually governed by the unrelated
    /// <see cref="SystemSettings.EnlistmentOpen"/> switch, so a term could sit in
    /// <see cref="EnrollmentStage.Closed"/> while the API cheerfully accepted seat requests — the
    /// banner told every student one thing and the server did another. This class is what the
    /// mutating slices ask, so the banner and the behaviour cannot disagree.
    /// </para>
    ///
    /// <para><b>Precedence.</b> The stage defines the <i>period</i>; the parameter switches are a
    /// manual pause <i>within</i> it. Enlistment is open only when the term is in the Enlistment
    /// stage <b>and</b> <see cref="SystemSettings.EnlistmentOpen"/> is on — so closing either one
    /// closes enlistment, and the refusal names which one did it.</para>
    /// </summary>
    public static class EnrollmentCyclePolicy
    {
        /// <summary>
        /// The cycle in order. Advancing means "the next one along this list". Registration (the
        /// SIS) comes first: a student registers, then submits admission documents (which may keep
        /// arriving even after enlistment opens), then enlists in subjects.
        ///
        /// <para><b>This array — not the enum's numeric values — is the order.</b>
        /// <see cref="EnrollmentStage"/> was declared with DocumentSubmission = 2 before
        /// Registration = 3, and the FR-CYC decision later put Registration first. The enum values
        /// are a persistence contract that must not be renumbered, so they now disagree with the
        /// workflow, and comparing two stages with &lt; or &gt; gives the wrong answer. Always go
        /// through <see cref="IndexOf"/>.</para>
        /// </summary>
        public static readonly EnrollmentStage[] Order =
        [
            EnrollmentStage.Preparation,
            EnrollmentStage.Registration,
            EnrollmentStage.DocumentSubmission,
            EnrollmentStage.Enlistment,
            EnrollmentStage.Closed
        ];

        /// <summary>Position in the workflow, or -1. Never compare the enum values directly.</summary>
        public static int IndexOf(EnrollmentStage stage) => Array.IndexOf(Order, stage);

        /// <summary>Display names for the stages — the API owns the wording so every screen agrees.</summary>
        public static string Label(EnrollmentStage stage) => stage switch
        {
            EnrollmentStage.Preparation => "Preparation",
            EnrollmentStage.DocumentSubmission => "Document submission",
            EnrollmentStage.Registration => "Registration",
            EnrollmentStage.Enlistment => "Subject enlistment",
            EnrollmentStage.Closed => "Enrollment closed",
            _ => stage.ToString()
        };

        /// <summary>
        /// May a student reserve or release a seat right now (FR-ENL-08)? Checks the stage first,
        /// then the institutional switch, so the message names whichever gate is actually shut —
        /// a student told "enlistment is closed" while the banner reads "Subject enlistment" has
        /// been told nothing useful.
        /// </summary>
        public static async Task<StageGate> CheckEnlistmentAsync(
            AppDbContext db, CancellationToken cancellationToken)
        {
            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return StageGate.Closed("No semester is active yet, so there is nothing to enlist in.");
            }

            if (semester.EnrollmentStage != EnrollmentStage.Enlistment)
            {
                return StageGate.Closed(
                    $"{semester.Name} is in {Label(semester.EnrollmentStage)}, not subject enlistment. " +
                    (semester.EnrollmentStage == EnrollmentStage.Closed
                        ? "Enrollment for this term has ended — see the Registrar."
                        : "Slot selection opens when the Academic Head moves the term to subject enlistment."));
            }

            var settings = await db.GetSettingsAsync(cancellationToken);
            if (!settings.EnlistmentOpen)
            {
                return StageGate.Closed(
                    "Online enlistment is paused. Please check back once the Registrar reopens it.");
            }

            return StageGate.Allowed;
        }

        /// <summary>
        /// May a Student Information Sheet be filed right now (FR-SIS, FR-CYC)?
        ///
        /// <para>Deliberately wider than enlistment: a term accepts SIS submissions from the
        /// Registration stage onward, because late enrollees are ordinary and their papers and
        /// enlistment follow behind. Only the two ends are shut — <see cref="EnrollmentStage.Preparation"/>,
        /// where the term is not open to students at all, and <see cref="EnrollmentStage.Closed"/>,
        /// where the roster is final.</para>
        ///
        /// <para>Falls <b>open</b> when no semester is active: the SIS is an anonymous public form,
        /// and a setup gap must not present a prospective student with a locked door. The
        /// submission itself still fails loudly further in if it has no term to attach to.</para>
        /// </summary>
        public static async Task<StageGate> CheckRegistrationAsync(
            AppDbContext db, CancellationToken cancellationToken)
        {
            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null) return StageGate.Allowed;

            return semester.EnrollmentStage switch
            {
                EnrollmentStage.Preparation => StageGate.Closed(
                    $"{semester.Name} is still in preparation — registration has not opened yet. " +
                    "Please check back, or contact the Admission Office."),
                EnrollmentStage.Closed => StageGate.Closed(
                    $"Enrollment for {semester.Name} has closed. Please contact the Admission Office."),
                _ => StageGate.Allowed
            };
        }
    }
}
