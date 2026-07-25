namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A digital Student Information Sheet submission (FR-SIS-01). Captures a new student's or
    /// transferee's personal, academic, and family details as entered through the public,
    /// account-less SIS form. A registrant is a data record — not a <see cref="User"/> login —
    /// identified to the outside world by <see cref="StudentNumber"/>.
    /// </summary>
    public class StudentRegistration
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// SEN-GEN's own registration number, auto-issued on SIS submission, e.g. "2026-000001".
        /// This is <b>not</b> the official student number — that is issued by a separate student
        /// system and recorded later in <see cref="OfficialStudentNumber"/> by the Admission
        /// Officer. Used for term-activation lookup and as the enrollee's identifier throughout.
        /// </summary>
        public string StudentNumber { get; set; } = string.Empty;

        /// <summary>
        /// The official student number issued by the separate student-records system. SEN-GEN does
        /// not generate it: after a registrant is enrolled there, the Admission Officer records the
        /// number here. Null until that step is done.
        /// </summary>
        public string? OfficialStudentNumber { get; set; }

        /// <summary>When and by whom <see cref="OfficialStudentNumber"/> was recorded.</summary>
        public DateTime? OfficialStudentNumberSetAtUtc { get; set; }

        public Guid? OfficialStudentNumberSetByUserId { get; set; }

        public RegistrationStatus Status { get; set; } = RegistrationStatus.Submitted;

        public StudentType StudentType { get; set; }

        public ProgramTrack Program { get; set; }

        /// <summary>The term this SIS was submitted for (the active semester at submission).</summary>
        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        /// <summary>
        /// The student's login account once claimed (matched by email at account registration or
        /// via the claim flow). Null until linked; enlistment requires the link (FR-ENL-05).
        /// </summary>
        public Guid? UserId { get; set; }

        public User? User { get; set; }

        /// <summary>
        /// FR-PRE-02/04: cleared by the Admission Officer for online subject slot selection —
        /// granted only after the document checklist is complete and the SIS is confirmed.
        /// </summary>
        public bool IsPreAuthorized { get; set; }

        public DateTime? PreAuthorizedAtUtc { get; set; }

        public Guid? PreAuthorizedByUserId { get; set; }

        // ---- Identity (SIS items 3–8, 14) ----
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public DateOnly DateOfBirth { get; set; }
        public string Birthplace { get; set; } = string.Empty;
        public string Citizenship { get; set; } = string.Empty;
        public CivilStatus CivilStatus { get; set; }
        public Gender Gender { get; set; }

        /// <summary>Registrant email — not on the paper SIS but required for the confirmation notice (FR-SIS-05).</summary>
        public string Email { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;

        // ---- Permanent address (SIS items 9–13) ----
        public string AddressLine { get; set; } = string.Empty; // house / lot / unit no + street
        public string Barangay { get; set; } = string.Empty;    // building / subdivision / village / barangay
        public string CityMunicipality { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;

        // ---- Last school attended (SIS items 15–20) ----
        public LastSchoolLevel LastSchoolLevel { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string SchoolProgram { get; set; } = string.Empty; // program / track & strand / specialization
        public string SchoolYear { get; set; } = string.Empty;    // e.g. "2017-2018"
        public YearGradeLevel YearGradeLastAttended { get; set; }
        public AcademicTerm LastTerm { get; set; }

        // ---- Family & guardian (SIS items 21–28) ----
        public string FatherName { get; set; } = string.Empty;
        public string FatherMobile { get; set; } = string.Empty;
        public string MotherName { get; set; } = string.Empty;
        public string MotherMobile { get; set; } = string.Empty;
        public GuardianRelationship GuardianRelationship { get; set; }
        public string GuardianName { get; set; } = string.Empty;
        public string GuardianMobile { get; set; } = string.Empty;

        /// <summary>Name of a student who referred this registrant (optional, SIS item 28).</summary>
        public string? ReferredBy { get; set; }

        /// <summary>FR-SIS-02: terms-and-conditions acknowledgment, persisted with its timestamp.</summary>
        public DateTime? TermsAcceptedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The admission-requirements checklist for this enrollee (FR-DOC).</summary>
        public ICollection<RegistrationDocument> Documents { get; set; } = new List<RegistrationDocument>();

        public string FullName => $"{FirstName} {LastName}";
    }
}
