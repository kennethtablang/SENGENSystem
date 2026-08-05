namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// How a subject a student sat for ended (FR-ENL-01/06 academic history). Deliberately a
    /// <i>verdict</i> and not a grade: SEN-GEN does not grade, and inventing a grade scale here
    /// would put the system in the business of computing standing it has no authority over. The
    /// three values are the only distinctions anything downstream actually needs — prerequisites
    /// ask "was it passed?", repeats ask "is it still owed?", and year level asks "how many units
    /// were earned?".
    /// <para>
    /// Persisted as a string (see <c>AppDbContext</c>), so the names are the stable contract.
    /// </para>
    /// </summary>
    public enum SubjectVerdict
    {
        /// <summary>Credit earned. Satisfies a prerequisite and counts toward the year-level ladder.</summary>
        Passed = 1,

        /// <summary>Sat for and not passed. Still owed, so it comes back into the enlistment plan.</summary>
        Failed = 2,

        /// <summary>
        /// Withdrawn mid-term. Treated exactly as <see cref="Failed"/> everywhere it matters —
        /// no credit, still owed — but kept distinct because the Registrar's record should not
        /// call a withdrawal a failure.
        /// </summary>
        Dropped = 3
    }

    public static class SubjectVerdictExtensions
    {
        /// <summary>
        /// Whether this verdict earns credit. The single place the pass/fail line is drawn, so a
        /// later verdict (an incomplete, say) cannot be added without deciding which side it is on.
        /// </summary>
        public static bool EarnsCredit(this SubjectVerdict verdict) => verdict == SubjectVerdict.Passed;

        /// <summary>Whether the student still owes this subject and should be offered it again.</summary>
        public static bool StillOwed(this SubjectVerdict verdict) => !verdict.EarnsCredit();
    }
}
