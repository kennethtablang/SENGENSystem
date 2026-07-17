import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { getDashboardMetrics } from './api';
import { getMyLink } from '../documents/api';
import { myEnlistment } from '../enlistment/api';
import { getMySchedule } from '../scheduling/api';
import { subscribeToReports } from '../reports/live';
import { LiveChip } from '../reports/ReportsPage';
import '../reports/reports.css'; // LiveChip styles
import './dashboard.css';

/* The semester-aware dashboard (FR-DASH). Staff see live metrics scoped to the active (or
   selected) semester — enrollment/enlistment statistics, section fill, room utilization,
   faculty load with imbalance flags (FR-DASH-01/02, FR-DOC-04, FR-FAC-04). Students see
   their live enrollment journey; faculty see their teaching week at a glance. */

const STAFF_ROLES = ['SchoolAdmin', 'AcademicHead', 'Registrar', 'AdmissionOfficer'];

const flagChip = {
    Overloaded: 'chip chip-yellow',
    AboveAverage: 'chip chip-yellow',
    BelowAverage: 'chip chip-muted',
    Unassigned: 'chip chip-muted',
    Balanced: 'chip chip-blue'
};

function Stat({ value, label }) {
    return (
        <div className="dash-stat card">
            <span className="dash-stat-num">{value}</span>
            <span className="dash-stat-label">{label}</span>
        </div>
    );
}

function Bar({ pct }) {
    return (
        <span className="dash-bar">
            <span className="dash-bar-fill" style={{ width: `${Math.min(100, pct)}%` }} />
        </span>
    );
}

// ---------- Staff dashboard ----------

function StaffDashboard() {
    const [data, setData] = useState(null);
    const [semesterId, setSemesterId] = useState(null);
    const [error, setError] = useState(null);
    const [liveState, setLiveState] = useState('offline');
    const [updatedAt, setUpdatedAt] = useState(null);
    const [refreshTick, setRefreshTick] = useState(0);
    const debounceRef = useRef(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const payload = await getDashboardMetrics(semesterId);
                if (active) { setData(payload); setUpdatedAt(new Date()); }
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, [semesterId, refreshTick]);

    // Live metrics: every audited mutation on the server pushes a SignalR signal;
    // the dashboard refetches (debounced) so the numbers move as the school works.
    useEffect(() => {
        const unsubscribe = subscribeToReports(
            () => {
                clearTimeout(debounceRef.current);
                debounceRef.current = setTimeout(() => setRefreshTick(t => t + 1), 500);
            },
            state => setLiveState(state));
        return () => {
            clearTimeout(debounceRef.current);
            unsubscribe();
        };
    }, []);

    if (error) return <div className="alert">{error}</div>;
    if (!data) return <p className="dash-loading">Loading live metrics…</p>;
    if (!data.semesterId) {
        return <div className="alert">No semester has been set up yet — create and activate one under Academic setup.</div>;
    }

    const { registration, documents, enlistment, roomUtilization, facultyLoad } = data;

    return (
        <>
            <div className="dash-semester">
                <label>
                    <span>Semester</span>
                    <select value={data.semesterId} onChange={e => setSemesterId(e.target.value)}>
                        {data.semesters.map(s => (
                            <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' · active' : ''}</option>
                        ))}
                    </select>
                </label>
                <LiveChip state={liveState} updatedAt={updatedAt} />
            </div>

            <div className="dash-stats">
                <Stat value={registration.total} label="SIS registrations" />
                <Stat value={registration.confirmed} label="Confirmed" />
                <Stat value={`${documents.completionRatePct}%`} label="Document checklists complete" />
                <Stat value={registration.preAuthorized} label="Pre-authorized" />
                <Stat value={enlistment.pending} label="Slot requests pending" />
                <Stat value={enlistment.approved} label="Seats approved" />
            </div>

            <div className="dash-panels">
                <section className="card dash-panel">
                    <h3>Enlistment by section</h3>
                    {enlistment.sections.length === 0 ? (
                        <p className="dash-panel-empty">No scheduled sections this semester yet.</p>
                    ) : (
                        <ul className="dash-list">
                            {enlistment.sections.map(s => (
                                <li key={s.sectionCode}>
                                    <span className="dash-list-label">
                                        <strong>{s.subjectCode}</strong> {s.sectionCode}
                                    </span>
                                    <Bar pct={s.fillPct} />
                                    <span className="dash-list-value">{s.enrolled}/{s.capacity}</span>
                                </li>
                            ))}
                        </ul>
                    )}
                </section>

                <section className="card dash-panel">
                    <h3>Room utilization <small>(Mon–Fri, 07:00–18:00)</small></h3>
                    <ul className="dash-list">
                        {roomUtilization.map(r => (
                            <li key={r.room}>
                                <span className="dash-list-label">
                                    <strong>{r.room}</strong>{r.isLaboratory ? ' · lab' : ''}
                                </span>
                                <Bar pct={r.utilizationPct} />
                                <span className="dash-list-value">{r.hoursPerWeek} h · {r.utilizationPct}%</span>
                            </li>
                        ))}
                    </ul>
                </section>

                <section className="card dash-panel">
                    <h3>Faculty load <small>(mean {facultyLoad.meanUnits} units)</small></h3>
                    <ul className="dash-list">
                        {facultyLoad.members.map(f => (
                            <li key={f.name}>
                                <span className="dash-list-label"><strong>{f.name}</strong></span>
                                <Bar pct={f.maxLoadUnits === 0 ? 0 : (100 * f.assignedUnits) / f.maxLoadUnits} />
                                <span className="dash-list-value">{f.assignedUnits}/{f.maxLoadUnits} u</span>
                                <span className={flagChip[f.flag] || 'chip chip-muted'}>{f.flag}</span>
                            </li>
                        ))}
                    </ul>
                </section>
            </div>
        </>
    );
}

// ---------- Student dashboard ----------

function StudentDashboard() {
    const [link, setLink] = useState(null);
    const [mine, setMine] = useState(null);
    const [error, setError] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const [linkData, mineData] = await Promise.all([getMyLink(), myEnlistment()]);
                if (!active) return;
                setLink(linkData);
                setMine(mineData);
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, []);

    if (error) return <div className="alert">{error}</div>;
    if (!link || !mine) return <p className="dash-loading">Loading your enrollment status…</p>;

    const r = link.registration;
    const steps = [
        {
            title: 'Link your student record',
            detail: r ? `Linked as ${r.studentNumber}` : 'Claim your SIS record under Document requirements.',
            done: link.linked,
            to: '/documents'
        },
        {
            title: 'Complete document requirements',
            detail: r ? `${r.submittedCount}/${r.totalCount} papers received` : 'Submit your papers to the Admission Office.',
            done: r?.documentsComplete ?? false,
            to: '/documents'
        },
        {
            title: 'Get confirmed & cleared',
            detail: r?.isPreAuthorized
                ? 'You are cleared for online enlistment'
                : 'The Registrar confirms your SIS; the Admission Office clears you.',
            done: (r?.registrationStatus === 'Confirmed' && r?.isPreAuthorized) ?? false,
            to: '/documents'
        },
        {
            title: 'Enlist in subjects',
            detail: mine.approvedUnits > 0
                ? `${mine.approvedUnits} units approved`
                : 'Browse published sections and reserve your seats.',
            done: mine.approvedUnits > 0,
            to: '/enlistment'
        }
    ];
    const currentIndex = steps.findIndex(s => !s.done);

    return (
        <>
            <section className="card dash-progress" aria-label="Enrollment progress">
                {steps.map((step, i) => (
                    <div className={`step${i === currentIndex ? ' is-current' : ''}${step.done ? ' is-done' : ''}`} key={step.title}>
                        <span className="step-num">{step.done ? '✓' : `0${i + 1}`}</span>
                        <div>
                            <h4>{step.title}</h4>
                            <small>{step.detail}</small>
                        </div>
                    </div>
                ))}
            </section>

            <div className="dash-grid">
                <Link className="card dash-tile" to="/enlistment">
                    <div className="tile-head"><span className="tile-mark">EN</span></div>
                    <h3>Subject enlistment</h3>
                    <p>Browse published sections with live slot counts and reserve your seats.</p>
                </Link>
                <Link className="card dash-tile" to="/schedule">
                    <div className="tile-head"><span className="tile-mark">MS</span></div>
                    <h3>My schedule</h3>
                    <p>Your weekly timetable, built from your approved enlistments.</p>
                </Link>
                <Link className="card dash-tile" to="/documents">
                    <div className="tile-head"><span className="tile-mark">DC</span></div>
                    <h3>Document requirements</h3>
                    <p>Your admission checklist and its submission status.</p>
                </Link>
            </div>
        </>
    );
}

// ---------- Faculty dashboard ----------

function FacultyDashboard() {
    const [sched, setSched] = useState(null);
    const [error, setError] = useState(null);

    useEffect(() => {
        let active = true;
        getMySchedule()
            .then(data => { if (active) setSched(data); })
            .catch(err => { if (active) setError(err.message); });
        return () => { active = false; };
    }, []);

    if (error) return <div className="alert">{error}</div>;
    if (!sched) return <p className="dash-loading">Loading your teaching week…</p>;

    return (
        <>
            <div className="dash-stats">
                <Stat value={sched.count} label="Classes this week" />
                <Stat value={`${sched.totalHours} h`} label="Teaching hours" />
                <Stat value={sched.isPublished ? 'Published' : 'Draft'} label="Timetable status" />
            </div>
            <div className="dash-grid">
                <Link className="card dash-tile" to="/schedule">
                    <div className="tile-head"><span className="tile-mark">MS</span></div>
                    <h3>My schedule</h3>
                    <p>Your weekly timetable with per-section seat counts for {sched.semesterName ?? 'the active semester'}.</p>
                </Link>
            </div>
        </>
    );
}

function DashboardPage() {
    const { user } = useAuth();
    const isStaff = STAFF_ROLES.includes(user.role);

    return (
        <>
            <h2 className="dash-title">Welcome back, <span className="text-brand">{user.firstName}</span></h2>
            <p className="dash-sub">
                {isStaff
                    ? 'Live, semester-scoped metrics across enrollment, enlistment, rooms, and faculty load.'
                    : user.role === 'FacultyMember'
                        ? 'Your teaching week at a glance.'
                        : 'Your enrollment journey — each step unlocks the next.'}
            </p>
            {isStaff ? <StaffDashboard /> : user.role === 'FacultyMember' ? <FacultyDashboard /> : <StudentDashboard />}
        </>
    );
}

export default DashboardPage;
