namespace SENGENSystem.Server.Domain
{
    // Categorizes bell notices so the client can pick an icon and users can scan by type.
    // Stored as strings; append new kinds rather than renumbering.
    public enum NotificationKind
    {
        General = 0,
        SchedulePublished = 1,
        EnlistmentApproved = 2,
        EnlistmentRejected = 3,
        SlotRequested = 4,
        FacultyLoadUpdated = 5,
        Documents = 6,
        Account = 7
    }
}
