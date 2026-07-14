import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';
import LoginPage from './features/auth/LoginPage';
import RegisterPage from './features/auth/RegisterPage';
import DashboardPage from './features/dashboard/DashboardPage';
import './App.css';

function RequireAuth({ children }) {
    const { user, loading } = useAuth();
    if (loading) return <p style={{ padding: '2rem' }}>Loading…</p>;
    return user ? children : <Navigate to="/login" replace />;
}

function RedirectIfAuthed({ children }) {
    const { user, loading } = useAuth();
    if (loading) return <p style={{ padding: '2rem' }}>Loading…</p>;
    return user ? <Navigate to="/" replace /> : children;
}

function App() {
    return (
        <Routes>
            <Route path="/login" element={<RedirectIfAuthed><LoginPage /></RedirectIfAuthed>} />
            <Route path="/register" element={<RedirectIfAuthed><RegisterPage /></RedirectIfAuthed>} />
            <Route path="/" element={<RequireAuth><DashboardPage /></RequireAuth>} />
            <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
    );
}

export default App;
