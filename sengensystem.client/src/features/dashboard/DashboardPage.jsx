import { useAuth } from '../auth/useAuth';

const roleLabels = {
    Student: 'Student',
    FacultyMember: 'Faculty Member',
    AdmissionOfficer: 'Admission Officer',
    Registrar: 'Registrar',
    AcademicHead: 'Academic Head',
    SchoolAdmin: 'School Admin'
};

function DashboardPage() {
    const { user, logout } = useAuth();

    return (
        <div style={{ maxWidth: '720px', margin: '0 auto', padding: '2rem', textAlign: 'left' }}>
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <h1 style={{ fontSize: '1.5rem' }}>SEN-GEN</h1>
                <button onClick={logout}>Sign out</button>
            </header>
            <p>
                Welcome, <strong>{user.firstName} {user.lastName}</strong> — signed in as{' '}
                <strong>{roleLabels[user.role] ?? user.role}</strong>.
            </p>
            <p style={{ opacity: 0.75 }}>
                Enrollment, enlistment, and scheduling modules will appear here as each
                feature slice is delivered (see RequirementsSpecifications.md).
            </p>
        </div>
    );
}

export default DashboardPage;
