import { Navigate, Route, Routes } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import { useAuth } from './features/auth/useAuth';
import LoginPage from './features/auth/LoginPage';
import RegisterPage from './features/auth/RegisterPage';
import ForgotPasswordPage from './features/auth/ForgotPasswordPage';
import ResetPasswordPage from './features/auth/ResetPasswordPage';
import ConfirmEmailPage from './features/auth/ConfirmEmailPage';
import DashboardPage from './features/dashboard/DashboardPage';
import ProfilePage from './features/profile/ProfilePage';
import GenerateSchedulePage from './features/scheduling/GenerateSchedulePage';
import ReviewSchedulePage from './features/scheduling/ReviewSchedulePage';
import ScheduleBoardPage from './features/scheduling/ScheduleBoardPage';
import SchedulePage from './features/scheduling/SchedulePage';
import AuditTrailPage from './features/audit/AuditTrailPage';
import SisRegistrationPage from './features/registration/SisRegistrationPage';
import TermActivationPage from './features/registration/TermActivationPage';
import RegistrationsPage from './features/registration/RegistrationsPage';
import TermActivationsPage from './features/registration/TermActivationsPage';
import UserManagementPage from './features/users/UserManagementPage';
import ParametersPage from './features/parameters/ParametersPage';
import SchoolYearsPage from './features/academic/SchoolYearsPage';
import SemestersPage from './features/academic/SemestersPage';
import BuildingsPage from './features/academic/BuildingsPage';
import RoomsPage from './features/academic/RoomsPage';
import ClassSectionsPage from './features/academic/ClassSectionsPage';
import SubjectsCurriculumPage from './features/curriculum/SubjectsCurriculumPage';
import FacultyLoadPage from './features/faculty/FacultyLoadPage';
import PublishingPage from './features/publishing/PublishingPage';
import DocumentsPage from './features/documents/DocumentsPage';
import PreAuthorizationPage from './features/pre-enrollment/PreAuthorizationPage';
import PreEnrollmentPage from './features/pre-enrollment/PreEnrollmentPage';
import EnlistmentPage from './features/enlistment/EnlistmentPage';
import ApprovalsPage from './features/enlistment/ApprovalsPage';
import ReportsPage from './features/reports/ReportsPage';
import FacultyLoadReportsPage from './features/reports/FacultyLoadReportsPage';
import RoomUtilizationPage from './features/analytics/RoomUtilizationPage';
import NotificationsPage from './features/notifications/NotificationsPage';
import SettingsPage from './features/settings/SettingsPage';
import HelpPage from './features/help/HelpPage';
import AppLayout from './features/shell/AppLayout';
import ComingSoon from './features/shell/ComingSoon';
import { allNavItems } from './features/shell/nav';
import { getPrefs } from './features/settings/prefs';
import './App.css';

// Routes with a real page; everything else in the nav falls back to ComingSoon.
const builtRoutes = new Set([
    '/', '/profile', '/schedule', '/scheduling/generate', '/scheduling/review', '/scheduling/board', '/audit',
    '/registrations', '/term-activations', '/users', '/parameters',
    '/school-years', '/semesters', '/buildings', '/rooms', '/class-sections', '/subjects', '/faculty-load',
    '/publishing', '/documents', '/pre-authorization', '/enlistment', '/approvals', '/pre-enrollment', '/reports',
    '/settings', '/help', '/notifications', '/reports/faculty-load', '/analytics/room-utilization'
]);

function RequireAuth({ children }) {
    const { user, loading } = useAuth();
    if (loading) return <p style={{ padding: '2rem' }}>Loading…</p>;
    return user ? children : <Navigate to="/login" replace />;
}

function RedirectIfAuthed({ children }) {
    const { user, loading } = useAuth();
    if (loading) return <p style={{ padding: '2rem' }}>Loading…</p>;
    // Land on the page chosen in Settings → Behavior (defaults to the dashboard).
    return user ? <Navigate to={getPrefs().landing || '/'} replace /> : children;
}

function App() {
    return (
        <>
        <ToastContainer theme="light" newestOnTop pauseOnFocusLoss={false} />
        <Routes>
            <Route path="/login" element={<RedirectIfAuthed><LoginPage /></RedirectIfAuthed>} />
            <Route path="/register" element={<RedirectIfAuthed><RegisterPage /></RedirectIfAuthed>} />

            {/* Public, account-less student registration (FR-SIS) */}
            <Route path="/register-sis" element={<SisRegistrationPage />} />
            <Route path="/term-activation" element={<TermActivationPage />} />

            {/* Public account-recovery pages (emailed links land here) */}
            <Route path="/forgot-password" element={<RedirectIfAuthed><ForgotPasswordPage /></RedirectIfAuthed>} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            <Route path="/confirm-email" element={<ConfirmEmailPage />} />

            <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
                <Route path="/" element={<DashboardPage />} />
                <Route path="/profile" element={<ProfilePage />} />
                <Route path="/scheduling/generate" element={<GenerateSchedulePage />} />
                <Route path="/scheduling/review" element={<ReviewSchedulePage />} />
                <Route path="/scheduling/board" element={<ScheduleBoardPage />} />
                <Route path="/schedule" element={<SchedulePage />} />
                <Route path="/audit" element={<AuditTrailPage />} />
                <Route path="/registrations" element={<RegistrationsPage />} />
                <Route path="/term-activations" element={<TermActivationsPage />} />
                <Route path="/users" element={<UserManagementPage />} />
                <Route path="/parameters" element={<ParametersPage />} />
                <Route path="/school-years" element={<SchoolYearsPage />} />
                <Route path="/semesters" element={<SemestersPage />} />
                <Route path="/buildings" element={<BuildingsPage />} />
                <Route path="/rooms" element={<RoomsPage />} />
                <Route path="/class-sections" element={<ClassSectionsPage />} />
                <Route path="/subjects" element={<SubjectsCurriculumPage />} />
                <Route path="/faculty-load" element={<FacultyLoadPage />} />
                <Route path="/publishing" element={<PublishingPage />} />
                <Route path="/documents" element={<DocumentsPage />} />
                <Route path="/pre-authorization" element={<PreAuthorizationPage />} />
                <Route path="/pre-enrollment" element={<PreEnrollmentPage />} />
                <Route path="/enlistment" element={<EnlistmentPage />} />
                <Route path="/approvals" element={<ApprovalsPage />} />
                <Route path="/reports" element={<ReportsPage />} />
                <Route path="/reports/faculty-load" element={<FacultyLoadReportsPage />} />
                <Route path="/analytics/room-utilization" element={<RoomUtilizationPage />} />
                <Route path="/settings" element={<SettingsPage />} />
                <Route path="/help" element={<HelpPage />} />
                <Route path="/notifications" element={<NotificationsPage />} />
                {allNavItems()
                    .filter(item => !builtRoutes.has(item.to))
                    .map(item => (
                        <Route key={item.to} path={item.to} element={<ComingSoon />} />
                    ))}
            </Route>

            <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
        </>
    );
}

export default App;
