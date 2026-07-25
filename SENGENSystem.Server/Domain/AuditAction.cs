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
        ScheduleArchived = 40,

        // Bulk data export — the one-workbook semester bundle (FR-RPT-02)
        SemesterExported = 41,

        // Bulk data export — the system parameters / setup master-data workbook
        SystemParametersExported = 42,

        // System parameters management (FR-SCHED-05) — the scheduling engine's inputs
        SectionCapacityCapChanged = 43,
        TimeSlotSaved = 44,
        FacultyLoadLimitChanged = 45,

        // Archiving — curricula retired instead of deleted, keeping their subjects and history
        CurriculumArchived = 46,
        CurriculumRestored = 47,

        // Enrollment cycle — the Registrar moving a term from one stage to the next
        EnrollmentStageChanged = 48,

        // Schedule finalization — the Academic Head signing off a draft as ready to publish,
        // and reopening it for further edits (FR-SCHED-06)
        ScheduleFinalized = 49,
        ScheduleReopened = 50,

        // Admission Officer records the official student number issued by the separate
        // student-records system against a SIS registration (FR-SIS)
        StudentNumberAssigned = 51,

        // Configurable admission-requirement catalog (FR-DOC-01) — add/edit/archive the papers
        // and choose which programs each applies to
        RequirementCreated = 52,
        RequirementUpdated = 53,
        RequirementArchived = 54,

        // A student login account provisioned from a SIS submission / term activation, with a
        // system-generated temporary password the student must change on first sign-in
        StudentAccountProvisioned = 55,

        // Academic Head tunes the scheduling engine's soft-constraint weights (FR-SCHED-03/-05)
        SoftConstraintWeightsChanged = 56,

        // Manual override of a section's seat cap to complete a section (FR-ENL-03) — Registrar,
        // Academic Head, or School Admin raises Section.Capacity above the institutional default
        SectionCapacityOverridden = 57,

        // School Admin tunes the institutional enrollment/enlistment and scheduling-engine
        // parameters (FR-SCHED-05, FR-ENL) — enlistment open/close, unit ceilings, engine budgets
        SystemParametersUpdated = 58,

        // Two-factor authentication (opt-in email one-time code, FR-AUTH)
        TwoFactorEnabled = 59,
        TwoFactorDisabled = 60,
        TwoFactorChallengeIssued = 61,
        TwoFactorFailed = 62,

        // ISO/IEC 25010 rating survey — Super Admin dispatch and respondent submissions
        SurveyInvitationsSent = 63,
        SurveyResponseSubmitted = 64
    }
}
