namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Categories of security- and data-relevant actions recorded in the audit trail
    /// (FR-AUD-01). Values are persisted as strings, so their names are the stable
    /// contract — append new members, never renumber existing ones.
    /// </summary>
    public enum AuditAction
    {
        // Currently wired
        AccountRegistered = 1,
        ProfileUpdated = 2,
        PasswordChanged = 3,
        ScheduleGenerated = 4,

        // Reserved for upcoming slices (kept here so the contract is visible)
        ScheduleOverridden = 5,
        SchedulePublished = 6,
        DocumentChecklistUpdated = 7,
        SlotRequested = 8,
        SlotApproved = 9,
        SlotRejected = 10,
        UserAccountCreated = 11,
        UserAccountUpdated = 12,
        UserAccountDeactivated = 13,
        NotificationDispatched = 14,

        // Authentication events (appended — earlier values are a fixed contract)
        LoginSucceeded = 15,
        LoginFailed = 16,

        // Student registration & term activation (SIS, FR-SIS)
        StudentRegistered = 17,
        RegistrationConfirmed = 18,
        TermActivationRequested = 19,
        TermActivationValidated = 20,

        // Academic setup — school years, semesters, buildings, rooms
        SchoolYearSaved = 21,
        SemesterSaved = 22,
        BuildingSaved = 23,
        RoomSaved = 24,

        // Curriculum & subjects
        CurriculumSaved = 25,
        SubjectSaved = 26
    }
}
