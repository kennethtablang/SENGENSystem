import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import { useAuth } from './features/auth/useAuth';
import LoginPage from './features/auth/LoginPage';
import FirstLoginPasswordChange from './features/auth/FirstLoginPasswordChange';
import AppLayout from './features/shell/AppLayout';
import ComingSoon from './features/shell/ComingSoon';
import { allNavItems } from './features/shell/nav';
import { getPrefs } from './features/settings/prefs';
import './App.css';

/* Route-level code splitting: every page below loads on first visit rather than in the main
   bundle, which had grown past 500 kB (FullCalendar, the report pages, the PSGC address data).
   Login, the forced first-login password change, and the shell stay eager so signing in never
   waits on a second request. The shell shows its own fallback while a page loads (AppLayout). */
const RegisterPage = lazy(() => import('./features/auth/RegisterPage'));
const ForgotPasswordPage = lazy(() => import('./features/auth/ForgotPasswordPage'));
const ResetPasswordPage = lazy(() => import('./features/auth/ResetPasswordPage'));
const ConfirmEmailPage = lazy(() => import('./features/auth/ConfirmEmailPage'));
const DashboardPage = lazy(() => import('./features/dashboard/DashboardPage'));
const ProfilePage = lazy(() => import('./features/profile/ProfilePage'));
const GenerateSchedulePage = lazy(() => import('./features/scheduling/GenerateSchedulePage'));
const ReviewSchedulePage = lazy(() => import('./features/scheduling/ReviewSchedulePage'));
const ScheduleBoardPage = lazy(() => import('./features/scheduling/ScheduleBoardPage'));
const SchedulePage = lazy(() => import('./features/scheduling/SchedulePage'));
const AuditTrailPage = lazy(() => import('./features/audit/AuditTrailPage'));
const SisRegistrationPage = lazy(() => import('./features/registration/SisRegistrationPage'));
const TermActivationPage = lazy(() => import('./features/registration/TermActivationPage'));
const RegistrationsPage = lazy(() => import('./features/registration/RegistrationsPage'));
const MyRegistrationPage = lazy(() => import('./features/registration/MyRegistrationPage'));
const TermActivationsPage = lazy(() => import('./features/registration/TermActivationsPage'));
const TermActivationControlPage = lazy(() => import('./features/registration/TermActivationControlPage'));
const AssignStudentNumberPage = lazy(() => import('./features/registration/AssignStudentNumberPage'));
const TransfereeEvaluationPage = lazy(() => import('./features/evaluation/TransfereeEvaluationPage'));
const AcademicRecordsPage = lazy(() => import('./features/academic-records/AcademicRecordsPage'));
const OutboxPage = lazy(() => import('./features/outbox/OutboxPage'));
const ProspectusPage = lazy(() => import('./features/evaluation/ProspectusPage'));
const MySubjectsPage = lazy(() => import('./features/evaluation/MySubjectsPage'));
const UserManagementPage = lazy(() => import('./features/users/UserManagementPage'));
const ParametersPage = lazy(() => import('./features/parameters/ParametersPage'));
const SchoolYearsPage = lazy(() => import('./features/academic/SchoolYearsPage'));
const SemestersPage = lazy(() => import('./features/academic/SemestersPage'));
const BuildingsPage = lazy(() => import('./features/academic/BuildingsPage'));
const RoomsPage = lazy(() => import('./features/academic/RoomsPage'));
const ClassSectionsPage = lazy(() => import('./features/academic/ClassSectionsPage'));
const SubjectsCurriculumPage = lazy(() => import('./features/curriculum/SubjectsCurriculumPage'));
const FacultyLoadPage = lazy(() => import('./features/faculty/FacultyLoadPage'));
const PublishingPage = lazy(() => import('./features/publishing/PublishingPage'));
const DocumentsPage = lazy(() => import('./features/documents/DocumentsPage'));
const PreAuthorizationPage = lazy(() => import('./features/pre-enrollment/PreAuthorizationPage'));
const PreEnrollmentPage = lazy(() => import('./features/pre-enrollment/PreEnrollmentPage'));
const EnlistmentPage = lazy(() => import('./features/enlistment/EnlistmentPage'));
const ApprovalsPage = lazy(() => import('./features/enlistment/ApprovalsPage'));
const ReportsPage = lazy(() => import('./features/reports/ReportsPage'));
const FacultyLoadReportsPage = lazy(() => import('./features/reports/FacultyLoadReportsPage'));
const RoomUtilizationPage = lazy(() => import('./features/analytics/RoomUtilizationPage'));
const NotificationsPage = lazy(() => import('./features/notifications/NotificationsPage'));
const SurveyPage = lazy(() => import('./features/survey/SurveyPage'));
const SurveyAdminPage = lazy(() => import('./features/survey/SurveyAdminPage'));
const SurveyRecipientsPage = lazy(() => import('./features/survey/SurveyRecipientsPage'));
const SettingsPage = lazy(() => import('./features/settings/SettingsPage'));
const HelpPage = lazy(() => import('./features/help/HelpPage'));

// Routes with a real page; everything else in the nav falls back to ComingSoon.
const builtRoutes = new Set([
    '/', '/profile', '/schedule', '/scheduling/generate', '/scheduling/review', '/scheduling/board', '/audit',
    '/registrations', '/my-registration', '/term-activations', '/term-activation-control',
    '/assign-student-number', '/users', '/parameters',
    '/evaluate-transferee', '/academic-records', '/prospectus', '/my-subjects',
    '/school-years', '/semesters', '/buildings', '/rooms', '/class-sections', '/subjects', '/faculty-load',
    '/publishing', '/documents', '/pre-authorization', '/enlistment', '/approvals', '/pre-enrollment', '/reports',
    '/settings', '/help', '/notifications', '/reports/faculty-load', '/analytics/room-utilization',
    '/survey-admin', '/survey-recipients', '/survey', '/outbox'
]);

function RequireAuth({ children }) {
    const { user, loading } = useAuth();
    if (loading) return <p style={{ padding: '2rem' }}>Loading…</p>;
    if (!user) return <Navigate to="/login" replace />;
    // A student on a system-generated temporary password must set their own before reaching
    // anything else (mirrors the server's MustChangePassword flag).
    if (user.mustChangePassword) return <FirstLoginPasswordChange />;
    return children;
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
        <Suspense fallback={<p className="route-loading" role="status">Loading…</p>}>
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

            {/* Public token-gated ISO 25010 rating survey (emailed link lands here) */}
            <Route path="/survey/:token" element={<SurveyPage />} />

            <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
                <Route path="/" element={<DashboardPage />} />
                <Route path="/profile" element={<ProfilePage />} />
                <Route path="/scheduling/generate" element={<GenerateSchedulePage />} />
                <Route path="/scheduling/review" element={<ReviewSchedulePage />} />
                <Route path="/scheduling/board" element={<ScheduleBoardPage />} />
                <Route path="/schedule" element={<SchedulePage />} />
                <Route path="/audit" element={<AuditTrailPage />} />
                <Route path="/registrations" element={<RegistrationsPage />} />
                {/* F-04: the student corrects their own SIS while it awaits confirmation */}
                <Route path="/my-registration" element={<MyRegistrationPage />} />
                <Route path="/term-activations" element={<TermActivationsPage />} />
                <Route path="/term-activation-control" element={<TermActivationControlPage />} />
                <Route path="/assign-student-number" element={<AssignStudentNumberPage />} />
                {/* FR-EVAL: the Registrar rules on a transferee's credits — the gate before enlistment */}
                <Route path="/evaluate-transferee" element={<TransfereeEvaluationPage />} />
                {/* FR-ENL-01/06: what a student has already taken — what prerequisites, repeats,
                    and the year-level ladder are answered from */}
                <Route path="/academic-records" element={<AcademicRecordsPage />} />
                {/* FR-RPT-05: printable subject listings, for staff and for the student themselves */}
                <Route path="/prospectus" element={<ProspectusPage />} />
                <Route path="/my-subjects" element={<MySubjectsPage />} />
                <Route path="/users" element={<UserManagementPage />} />
                {/* Operational visibility over transactional email — what went out,
                    what failed, and a way to requeue it */}
                <Route path="/outbox" element={<OutboxPage />} />
                <Route path="/survey-admin" element={<SurveyAdminPage />} />
                <Route path="/survey-recipients" element={<SurveyRecipientsPage />} />
                {/* Signed-in participation: the bell notice the Super Admin pushed lands here */}
                <Route path="/survey" element={<SurveyPage />} />
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
        </Suspense>
        </>
    );
}

export default App;
