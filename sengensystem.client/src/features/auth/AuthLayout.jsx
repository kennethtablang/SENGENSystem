import { Link } from 'react-router-dom';
import logo from '../../assets/SENGENlogo.png';
import './auth.css';

export function Wordmark() {
    return (
        <span className="wordmark">
            <img src={logo} alt="" />
            <span>SEN<span className="wordmark-dot">·</span>GEN</span>
            <span className="wordmark-campus">STI Alaminos</span>
        </span>
    );
}

function AuthLayout({ children }) {
    return (
        <div className="auth-shell">
            <aside className="auth-brand">
                <img className="auth-brand-watermark" src={logo} alt="" aria-hidden="true" />
                <Wordmark />
                <div className="auth-brand-body">
                    <h2>
                        From enrollment to a{' '}
                        <span className="text-brand">conflict&#8209;free class schedule</span>.
                    </h2>
                    <p>
                        SEN&#8209;GEN is a Student Enrollment and Constraint&#8209;Satisfaction
                        scheduling engine. Enroll, pick your subjects, and the engine slots
                        each one into a valid weekly timetable — checking time overlaps,
                        prerequisites, and your unit cap as you go.
                    </p>
                </div>
                <ol className="auth-pipeline">
                    <li><i>01</i> Documents</li>
                    <li><i>02</i> Registration</li>
                    <li><i>03</i> Enlistment</li>
                </ol>
            </aside>
            <div className="auth-main">
                <header className="auth-main-header">
                    <Link to="/login" aria-label="SEN-GEN home">
                        <Wordmark />
                    </Link>
                </header>
                <div className="auth-form-wrap">{children}</div>
                <footer className="auth-footer">
                    <span>© 2026 STI College Alaminos</span>
                    <span>Personal data handled under RA 10173</span>
                </footer>
            </div>
        </div>
    );
}

export default AuthLayout;
