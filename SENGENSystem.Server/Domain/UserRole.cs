namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The institutional roles defined by SEN-GEN's role-based access control (FR-AUTH).
    /// <see cref="SuperAdmin"/> sits above <see cref="SchoolAdmin"/>: it reaches every function
    /// (like School Admin) and additionally owns the super-admin-only surfaces — user management
    /// and the ISO 25010 rating survey. A School Admin is elevated to every role EXCEPT SuperAdmin.
    /// </summary>
    public enum UserRole
    {
        Student = 1,
        FacultyMember = 2,
        AdmissionOfficer = 3,
        Registrar = 4,
        AcademicHead = 5,
        SchoolAdmin = 6,
        SuperAdmin = 7
    }
}
