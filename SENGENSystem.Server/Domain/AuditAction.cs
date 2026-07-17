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
        SubjectSaved = 26,

        // Faculty load management
        FacultyLoadSaved = 27,

        // Academic setup — class sections (student blocks)
        ClassSectionSaved = 28,

        // Student identity & pre-authorization (FR-ENL-05, FR-PRE-02/04)
        StudentAccountLinked = 29,
        StudentPreAuthorized = 30,

        // Pre-enrollment ETL import (FR-PRE-01)
        PreEnrollmentImported = 31,

        // Faculty time-slot preferences (FR-SCHED-03)
        FacultyPreferencesSaved = 32,

        // Self-service account recovery & email change (FR-AUTH)
        PasswordResetRequested = 33,
        PasswordResetCompleted = 34,
        EmailChangeRequested = 35,
        EmailChanged = 36,

        // Scheduling engine diagnostics (FR-SCHED-07)
        ScheduleGenerationFailed = 37,

        // Archiving — subjects retired on curriculum changes, semester schedules at term end
        SubjectArchived = 38,
        SubjectRestored = 39,
        ScheduleArchived = 40
    }
}
