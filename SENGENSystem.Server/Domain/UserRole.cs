namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The six institutional roles defined by SEN-GEN's role-based access control (FR-AUTH).
    /// </summary>
    public enum UserRole
    {
        Student = 1,
        FacultyMember = 2,
        AdmissionOfficer = 3,
        Registrar = 4,
        AcademicHead = 5,
        SchoolAdmin = 6
    }
}
