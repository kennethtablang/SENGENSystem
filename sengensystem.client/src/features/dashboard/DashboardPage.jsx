import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { getDashboardMetrics } from './api';
import { getMyLink } from '../documents/api';
import { myEnlistment } from '../enlistment/api';
import { getMySchedule } from '../scheduling/api';
import { subscribeToReports } from '../reports/live';
import { LiveChip } from '../reports/ReportsPage';
import {
    Donut, TrendChart, LoadColumns, Sparkline, Meter,
    Funnel, Heatmap, RankedBars, StackedBars
} from './charts';
import Tip, { TipBody } from '../shell/Tooltip';
import '../reports/reports.css'; // LiveChip styles
import './dashboard.css';

/* The semester-aware dashboard (FR-DASH). Staff see live metrics scoped to the active (or
   selected) semester — enrollment/enlistment statistics, section fill, room utilization,
   faculty load with imbalance flags (FR-DASH-01/02, FR-DOC-04, FR-FAC-04). Students see
   their live enrollment journey; faculty see their teaching week at a glance. */

const STAFF_ROLES = ['SchoolAdmin', 'AcademicHead', 'Registrar', 'AdmissionOfficer'];

// Enum names the API returns, spelled the way the SIS form spells them.
const PROGRAM_LABEL = {
    ITP: 'Information Technology Program',
    HRS: 'Hospitality and Restaurant Services',
    HRA: 'Hotel and Restaurant Administration'
};

const DOCUMENT_LABEL = {
    Form138_SF9: 'Form 138 / SF9',
    Form137_SF10: 'Form 137 / SF10',
    GoodMoral: 'Good moral',
    PsaBirthCertificate: 'PSA birth cert.',
    OfficialTranscript: 'Transcript',
    HonorableDismissal: 'Honorable dismissal',
    HepaA: 'Hepatitis A',
    HepaB: 'Hepatitis B',
    Xray: 'Chest X-ray'
};

const flagChip = {
    Overloaded: 'chip chip-yellow',
    AboveAverage: 'chip chip-yellow',
    BelowAverage: 'chip chip-muted',
    Unassigned: 'chip chip-muted',
    Balanced: 'chip chip-blue'
};

/* Page header: greeting on the left, per-dashboard controls (semester picker,
   live chip) inline on the right. */
function DashHead({ controls }) {
    const { user } = useAuth();
    const isStaff = STAFF_ROLES.includes(user.role);
    return (
        <div className="dash-head">
            <div>
                <h2 className="dash-title">Welcome back, <span className="text-brand">{user.firstName}</span></h2>
                <p className="dash-sub">
                    {isStaff
                        ? 'Live, semester-scoped metrics across enrollment, enlistment, rooms, and faculty load.'
                        : user.role === 'FacultyMember'
                            ? 'Your teaching week at a glance.'
                            : 'Your enrollment journey — each step unlocks the next.'}
                </p>
            </div>
            {controls && <div className="dash-head-controls">{controls}</div>}
        </div>
    );
}

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

/* A KPI tile: headline figure, caption, a supporting line, and an explanatory
   tooltip so every number on the board says where it comes from. */
function Kpi({ value, label, sub, tone = '', tip, spark }) {
    return (
        <Tip as="div" className={`dash-kpi card ${tone}`.trim()} content={tip}>
            <span className="dash-kpi-label">{label}</span>
            <span className="dash-kpi-num">{value}</span>
            {sub && <span className="dash-kpi-sub">{sub}</span>}
            {spark && <span className="dash-kpi-spark">{spark}</span>}
        </Tip>
    );
}

/* One line of the operational-health panel: label, meter, figure. */
function HealthRow({ label, figure, pct, tone, tip }) {
    return (
        <Tip as="li" className="health-row" content={tip}>
            <span className="health-label">{label}</span>
            <Meter pct={pct} tone={tone} />
            <span className="health-figure">{figure}</span>
        </Tip>
    );
}

const relTime = (iso) => {
    const mins = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    if (mins < 1440) return `${Math.round(mins / 60)}h ago`;
    return `${Math.round(mins / 1440)}d ago`;
};

// "SubjectArchived" → "Subject archived"
const humanize = (pascal) => pascal
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, c => c.toUpperCase())
    .replace(/(?<= )[A-Z](?=[a-z])/g, c => c.toLowerCase());

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

    if (error) return <><DashHead /><div className="alert">{error}</div></>;
    if (!data) return <><DashHead /><p className="dash-loading">Loading live metrics…</p></>;
    if (!data.semesterId) {
        return (
            <>
                <DashHead />
                <div className="alert">No semester has been set up yet — create and activate one under Academic setup.</div>
            </>
        );
    }

    const {
        registration, documents, intakeTrend, enlistment, roomUtilization, facultyLoad,
        seats = {}, schedule = {}, inventory = {}, activity = [], semester = {},
        programMix = [], documentMix = [], scheduleHeat = []
    } = data;
    const requestsTotal = enlistment.pending + enlistment.approved + enlistment.rejected;
    const trendValues = (intakeTrend || []).map(p => p.cumulative);
    const last7 = (intakeTrend || []).slice(-7).reduce((sum, p) => sum + p.count, 0);
    const overloaded = facultyLoad.members.filter(f => f.flag === 'Overloaded').length;
    const unassigned = facultyLoad.members.filter(f => f.flag === 'Unassigned').length;
    const idleRooms = roomUtilization.filter(r => r.utilizationPct === 0).length;
    const meanRoomUse = roomUtilization.length === 0
        ? 0
        : Math.round(roomUtilization.reduce((sum, r) => sum + r.utilizationPct, 0) / roomUtilization.length);

    return (
        <>
            <DashHead controls={
                <>
                    <label className="dash-sem-picker">
                        <span>Semester</span>
                        <select value={data.semesterId} onChange={e => setSemesterId(e.target.value)}>
                            {data.semesters.map(s => (
                                <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' · active' : ''}</option>
                            ))}
                        </select>
                    </label>
                    <LiveChip state={liveState} updatedAt={updatedAt} />
                </>
            } />

            {/* Term context strip: which term these numbers describe, and how far into it we are. */}
            <section className="card dash-term">
                <div className="dash-term-id">
                    <h3>{data.semesterName}</h3>
                    <span className={semester.isActive ? 'chip chip-active' : 'chip chip-muted'}>
                        {semester.isActive ? 'Active term' : 'Inactive term'}
                    </span>
                    {schedule.isPublished
                        ? <span className="chip chip-blue">Timetable published</span>
                        : schedule.assignments > 0 && <span className="chip chip-yellow">Timetable in draft</span>}
                </div>
                {semester.daysTotal > 0 && (
                    <Tip
                        as="div"
                        className="dash-term-progress"
                        content={<TipBody
                            title="Term progress"
                            rows={[
                                ['Starts', semester.startDate],
                                ['Ends', semester.endDate],
                                ['Day', `${semester.daysElapsed} of ${semester.daysTotal}`],
                                ['Remaining', `${semester.daysRemaining} days`]
                            ]}
                        />}
                    >
                        <Meter pct={semester.progressPct} />
                        <span className="dash-term-figure">
                            {semester.progressPct}% elapsed · {semester.daysRemaining} days left
                        </span>
                    </Tip>
                )}
            </section>

            <div className="dash-kpis">
                <Kpi
                    label="SIS registrations"
                    value={registration.total}
                    sub={`+${last7} in the last 7 days`}
                    spark={<Sparkline values={trendValues} />}
                    tip={<TipBody
                        title="Student registrations this term"
                        rows={[
                            ['Submitted', registration.submitted],
                            ['Confirmed', registration.confirmed],
                            ['Rejected', registration.rejected],
                            ['Linked to accounts', registration.linkedAccounts],
                            ['New this week', last7]
                        ]}
                        note="Every SIS record filed against the selected semester (FR-SIS)."
                    />}
                />
                <Kpi
                    label="Confirmed by Registrar"
                    value={registration.confirmed}
                    sub={`${registration.total === 0 ? 0 : Math.round((100 * registration.confirmed) / registration.total)}% of intake`}
                    tone="tone-good"
                    tip={<TipBody
                        title="Confirmed registrations"
                        rows={[
                            ['Confirmed', registration.confirmed],
                            ['Still submitted', registration.submitted],
                            ['Rejected', registration.rejected]
                        ]}
                        note="A student must be confirmed before the Admission Office can clear them."
                    />}
                />
                <Kpi
                    label="Document checklists complete"
                    value={`${documents.completionRatePct}%`}
                    sub={`${documents.complete} complete · ${documents.incomplete} outstanding`}
                    tone={documents.completionRatePct < 50 ? 'tone-warn' : ''}
                    tip={<TipBody
                        title="Admission requirements (FR-DOC-04)"
                        rows={[
                            ['Complete', documents.complete],
                            ['Incomplete', documents.incomplete],
                            ['Completion rate', `${documents.completionRatePct}%`]
                        ]}
                        note="A checklist counts as complete only when every required paper is received."
                    />}
                />
                <Kpi
                    label="Cleared to enlist"
                    value={registration.preAuthorized}
                    sub={`${Math.max(0, registration.total - registration.preAuthorized)} not yet cleared`}
                    tip={<TipBody
                        title="Pre-authorized students (FR-PRE-04)"
                        rows={[
                            ['Cleared', registration.preAuthorized],
                            ['Awaiting clearance', Math.max(0, registration.total - registration.preAuthorized)],
                            ['Of confirmed', `${registration.confirmed === 0 ? 0 : Math.round((100 * registration.preAuthorized) / registration.confirmed)}%`]
                        ]}
                        note="Clearance is what unlocks online enlistment for a student."
                    />}
                />
                <Kpi
                    label="Slot requests pending"
                    value={enlistment.pending}
                    sub={`${enlistment.approved} approved · ${enlistment.rejected} rejected`}
                    tone={enlistment.pending > 0 ? 'tone-warn' : ''}
                    tip={<TipBody
                        title="Enlistment queue"
                        rows={[
                            ['Pending decision', enlistment.pending],
                            ['Approved', enlistment.approved],
                            ['Rejected', enlistment.rejected],
                            ['Approval rate', requestsTotal === 0 ? '—' : `${Math.round((100 * enlistment.approved) / requestsTotal)}%`]
                        ]}
                        note="Pending requests hold a seat that nobody else can take."
                    />}
                />
                <Kpi
                    label="Seat utilization"
                    value={`${seats.fillPct ?? 0}%`}
                    sub={`${seats.taken ?? 0} of ${seats.capacity ?? 0} seats taken`}
                    tone={(seats.fillPct ?? 0) > 90 ? 'tone-warn' : ''}
                    tip={<TipBody
                        title="Seats across scheduled sections"
                        rows={[
                            ['Capacity', seats.capacity ?? 0],
                            ['Taken', seats.taken ?? 0],
                            ['Free', seats.free ?? 0],
                            ['Sections at capacity', seats.sectionsFull ?? 0],
                            ['Sections with no enrollees', seats.sectionsEmpty ?? 0]
                        ]}
                        note="Counts only sections that already have a place on the schedule board."
                    />}
                />
                <Kpi
                    label="Students holding seats"
                    value={enlistment.studentsEnlisted ?? 0}
                    sub={`${registration.preAuthorized === 0 ? 0 : Math.round((100 * (enlistment.studentsEnlisted ?? 0)) / registration.preAuthorized)}% of cleared students`}
                    tip={<TipBody
                        title="Enlisted students"
                        rows={[
                            ['With a seat', enlistment.studentsEnlisted ?? 0],
                            ['Cleared but not enlisted',
                                Math.max(0, registration.preAuthorized - (enlistment.studentsEnlisted ?? 0))],
                            ['Approved requests', enlistment.approved]
                        ]}
                        note="Distinct students, not requests — one student may hold several seats."
                    />}
                />
                <Kpi
                    label="Classes on the board"
                    value={schedule.assignments ?? 0}
                    sub={`${schedule.published ?? 0} published · ${schedule.draft ?? 0} draft`}
                    tone={schedule.isPublished ? 'tone-good' : 'tone-warn'}
                    tip={<TipBody
                        title="Schedule assignments"
                        rows={[
                            ['Total', schedule.assignments ?? 0],
                            ['Published', schedule.published ?? 0],
                            ['Draft', schedule.draft ?? 0],
                            ['Manual overrides', schedule.manualOverrides ?? 0],
                            ['Rooms in use', `${schedule.roomsUsed ?? 0}/${inventory.rooms ?? 0}`]
                        ]}
                        note="Draft classes are invisible to students until the term is published."
                    />}
                />
            </div>

            {/* Operational health: the four things that stall a term, each as a meter. */}
            <section className="card dash-health">
                <h3>Operational health <small>(hover any row for the breakdown)</small></h3>
                <ul className="health-list">
                    <HealthRow
                        label="Schedule coverage"
                        pct={schedule.coveragePct ?? 0}
                        tone={(schedule.coveragePct ?? 0) < 100 ? 'meter-warn' : 'meter-ok'}
                        figure={`${schedule.sectionsScheduled ?? 0}/${schedule.sectionsTotal ?? 0} sections`}
                        tip={<TipBody
                            title="Sections placed on the board"
                            rows={[
                                ['Scheduled', schedule.sectionsScheduled ?? 0],
                                ['Unscheduled', schedule.sectionsUnscheduled ?? 0],
                                ['Total assignments', schedule.assignments ?? 0],
                                ['Manual overrides', schedule.manualOverrides ?? 0],
                                ['Rooms in use', `${schedule.roomsUsed ?? 0}/${inventory.rooms ?? 0}`]
                            ]}
                            note="Unscheduled sections cannot be enlisted in by students."
                        />}
                    />
                    <HealthRow
                        label="Timetable published"
                        pct={(schedule.assignments ?? 0) === 0 ? 0 : (100 * (schedule.published ?? 0)) / schedule.assignments}
                        tone={schedule.isPublished ? 'meter-ok' : 'meter-warn'}
                        figure={`${schedule.published ?? 0}/${schedule.assignments ?? 0} classes`}
                        tip={<TipBody
                            title="Publication state (FR-SCHED-06)"
                            rows={[
                                ['Published', schedule.published ?? 0],
                                ['Still draft', schedule.draft ?? 0]
                            ]}
                            note="Students and faculty only see published assignments."
                        />}
                    />
                    <HealthRow
                        label="Document clearance"
                        pct={documents.completionRatePct}
                        tone={documents.completionRatePct < 50 ? 'meter-warn' : 'meter-ok'}
                        figure={`${documents.complete}/${registration.total} students`}
                        tip={<TipBody
                            title="Admission paperwork"
                            rows={[['Complete', documents.complete], ['Outstanding', documents.incomplete]]}
                            note="Drives how many students can be cleared for enlistment."
                        />}
                    />
                    <HealthRow
                        label="Room utilization"
                        pct={meanRoomUse}
                        tone={meanRoomUse < 25 ? 'meter-warn' : 'meter-ok'}
                        figure={`${meanRoomUse}% mean · ${idleRooms} idle`}
                        tip={<TipBody
                            title="Room usage across Mon–Fri, 08:00–17:00 (45 h/week)"
                            rows={[
                                ['Rooms', inventory.rooms ?? roomUtilization.length],
                                ['Laboratories', inventory.laboratories ?? '—'],
                                ['Unused rooms', idleRooms],
                                ['Mean utilization', `${meanRoomUse}%`]
                            ]}
                            note="Idle rooms are spare capacity the scheduler can still draw on."
                        />}
                    />
                    <HealthRow
                        label="Faculty load balance"
                        pct={facultyLoad.members.length === 0
                            ? 0
                            : (100 * (facultyLoad.members.length - overloaded - unassigned)) / facultyLoad.members.length}
                        tone={overloaded > 0 ? 'meter-bad' : 'meter-ok'}
                        figure={`${overloaded} over · ${unassigned} idle`}
                        tip={<TipBody
                            title="Load distribution (FR-FAC-04)"
                            rows={[
                                ['Faculty', facultyLoad.members.length],
                                ['Over ceiling', overloaded],
                                ['Unassigned', unassigned],
                                ['Mean load', `${facultyLoad.meanUnits} u`]
                            ]}
                            note="Overloaded members exceed their own MaxLoadUnits ceiling."
                        />}
                    />
                </ul>
            </section>

            <div className="dash-charts">
                <section className="card chart-card chart-wide">
                    <h3>Registration intake <small>(last 30 days)</small></h3>
                    {!intakeTrend?.length || registration.total === 0 ? (
                        <p className="dash-panel-empty">No SIS registrations this semester yet.</p>
                    ) : (
                        <TrendChart points={intakeTrend} />
                    )}
                </section>

                <section className="card chart-card">
                    <h3>Registration pipeline</h3>
                    {registration.total === 0 ? (
                        <p className="dash-panel-empty">Nothing in the pipeline yet.</p>
                    ) : (
                        <Donut
                            centerValue={registration.total}
                            centerLabel="registrations"
                            segments={[
                                { label: 'Confirmed', value: registration.confirmed, tone: 'tone-up' },
                                { label: 'Submitted', value: registration.submitted, tone: 'tone-yellow' },
                                { label: 'Rejected', value: registration.rejected, tone: 'tone-down' }
                            ]}
                            tipNote="Registrar decisions on this term's SIS records."
                        />
                    )}
                </section>

                <section className="card chart-card">
                    <h3>Slot requests</h3>
                    {requestsTotal === 0 ? (
                        <p className="dash-panel-empty">No enlistment requests yet.</p>
                    ) : (
                        <Donut
                            centerValue={requestsTotal}
                            centerLabel="requests"
                            segments={[
                                { label: 'Approved', value: enlistment.approved, tone: 'tone-blue' },
                                { label: 'Pending', value: enlistment.pending, tone: 'tone-yellow' },
                                { label: 'Rejected', value: enlistment.rejected, tone: 'tone-down' }
                            ]}
                            tipNote="Seat requests students filed against scheduled sections."
                        />
                    )}
                </section>

                <section className="card chart-card chart-wide">
                    <h3>Faculty load distribution <small>(ticks mark each member’s ceiling)</small></h3>
                    {facultyLoad.members.length === 0 ? (
                        <p className="dash-panel-empty">No faculty profiles yet.</p>
                    ) : (
                        <LoadColumns members={facultyLoad.members} mean={facultyLoad.meanUnits} />
                    )}
                </section>
            </div>

            {/* Second analytics row: a funnel, a heatmap, and two composition charts —
                different shapes for different questions. */}
            <div className="dash-charts">
                <section className="card chart-card">
                    <h3>Enrollment funnel <small>(intake → seats held)</small></h3>
                    {registration.total === 0 ? (
                        <p className="dash-panel-empty">No students in the funnel yet.</p>
                    ) : (
                        <Funnel stages={[
                            { label: 'Registered', value: registration.total, note: 'SIS records filed for this term.' },
                            { label: 'Confirmed', value: registration.confirmed, note: 'Registrar accepted the record.' },
                            { label: 'Documents complete', value: documents.complete, note: 'Every required paper received.' },
                            { label: 'Cleared to enlist', value: registration.preAuthorized, note: 'Admission Office pre-authorized.' },
                            { label: 'Holding a seat', value: enlistment.studentsEnlisted ?? 0, note: 'At least one approved slot request.' }
                        ]} />
                    )}
                </section>

                <section className="card chart-card">
                    <h3>Weekly class density <small>(Mon–Sat, 07:00–18:00)</small></h3>
                    {scheduleHeat.length === 0 ? (
                        <p className="dash-panel-empty">Nothing scheduled this semester yet.</p>
                    ) : (
                        <Heatmap cells={scheduleHeat} />
                    )}
                </section>

                <section className="card chart-card">
                    <h3>Intake by program <small>(chosen track)</small></h3>
                    {programMix.length === 0 ? (
                        <p className="dash-panel-empty">No registrations to break down yet.</p>
                    ) : (
                        <RankedBars items={programMix.map(p => ({
                            label: p.program,
                            value: p.total,
                            tip: <TipBody
                                title={PROGRAM_LABEL[p.program] || p.program}
                                rows={[
                                    ['Registered', p.total],
                                    ['Confirmed', p.confirmed],
                                    ['Cleared to enlist', p.cleared],
                                    ['Share of intake', `${registration.total === 0 ? 0 : Math.round((100 * p.total) / registration.total)}%`]
                                ]}
                            />
                        }))} />
                    )}
                </section>

                <section className="card chart-card">
                    <h3>Requirements by document <small>(share received)</small></h3>
                    {documentMix.length === 0 ? (
                        <p className="dash-panel-empty">No checklists seeded yet.</p>
                    ) : (
                        <StackedBars rows={documentMix.map(d => ({
                            label: DOCUMENT_LABEL[d.document] || d.document,
                            segments: [
                                { label: 'Original', value: d.submitted, tone: 'seg-up' },
                                // A photocopy, or a certificate of grades standing in for the
                                // transcript — either way the original is still to come.
                                { label: 'Stand-in', value: d.xerox, tone: 'seg-yellow' },
                                { label: 'Missing', value: d.missing, tone: 'seg-muted' }
                            ],
                            tip: <TipBody
                                title={DOCUMENT_LABEL[d.document] || d.document}
                                rows={[
                                    ['Original received', d.submitted],
                                    ['Photocopy / cert. of grades', d.xerox],
                                    ['Not submitted', d.missing],
                                    ['Students tracked', d.total]
                                ]}
                                note={d.missing > 0
                                    ? `${d.missing} student(s) still owe this paper.`
                                    : 'Fully collected across the cohort.'}
                            />
                        }))} />
                    )}
                </section>
            </div>

            <div className="dash-panels">
                <section className="card dash-panel">
                    <h3>Enlistment by section</h3>
                    {enlistment.sections.length === 0 ? (
                        <p className="dash-panel-empty">No scheduled sections this semester yet.</p>
                    ) : (
                        <ul className="dash-list">
                            {enlistment.sections.map(s => (
                                <Tip
                                    as="li"
                                    key={s.sectionCode}
                                    content={<TipBody
                                        title={`${s.subjectCode} — ${s.subjectTitle || s.sectionCode}`}
                                        rows={[
                                            ['Section', s.sectionCode],
                                            ['Cohort', s.cohort],
                                            ['Enrolled', `${s.enrolled} of ${s.capacity}`],
                                            ['Free seats', s.free ?? Math.max(0, s.capacity - s.enrolled)],
                                            ['Fill rate', `${s.fillPct}%`],
                                            ...(s.units ? [['Units', s.units]] : [])
                                        ]}
                                        note={s.enrolled >= s.capacity
                                            ? 'At capacity — further requests will be refused.'
                                            : undefined}
                                    />}
                                >
                                    <span className="dash-list-label">
                                        <strong>{s.subjectCode}</strong> {s.sectionCode}
                                    </span>
                                    <Bar pct={s.fillPct} />
                                    <span className="dash-list-value">{s.enrolled}/{s.capacity}</span>
                                </Tip>
                            ))}
                        </ul>
                    )}
                </section>

                <section className="card dash-panel">
                    <h3>Room utilization <small>(Mon–Fri, 08:00–17:00)</small></h3>
                    <ul className="dash-list">
                        {roomUtilization.map(r => (
                            <Tip
                                as="li"
                                key={r.room}
                                content={<TipBody
                                    title={r.room}
                                    rows={[
                                        ['Building', r.building || '—'],
                                        ['Type', r.isLaboratory ? 'Laboratory' : 'Lecture room'],
                                        ['Seats', r.capacity],
                                        ['Classes/week', r.classes],
                                        ['In window', `${r.windowHoursPerWeek} of 45 h`],
                                        ['Booked total', `${r.hoursPerWeek} h`],
                                        ['Utilization', `${r.utilizationPct}%`]
                                    ]}
                                    note={r.utilizationPct === 0
                                        ? 'Unused this term — spare capacity for the scheduler.'
                                        : undefined}
                                />}
                            >
                                <span className="dash-list-label">
                                    <strong>{r.room}</strong>{r.isLaboratory ? ' · lab' : ''}
                                </span>
                                <Bar pct={r.utilizationPct} />
                                <span className="dash-list-value">{r.windowHoursPerWeek} h · {r.utilizationPct}%</span>
                            </Tip>
                        ))}
                    </ul>
                </section>

                <section className="card dash-panel">
                    <h3>Faculty load <small>(mean {facultyLoad.meanUnits} units)</small></h3>
                    <ul className="dash-list">
                        {facultyLoad.members.map(f => (
                            <Tip
                                as="li"
                                key={f.name}
                                content={<TipBody
                                    title={f.name}
                                    rows={[
                                        ['Assigned', `${f.assignedUnits} u`],
                                        ['Ceiling', `${f.maxLoadUnits} u`],
                                        [f.assignedUnits > f.maxLoadUnits ? 'Over by' : 'Headroom',
                                            `${Math.abs(f.maxLoadUnits - f.assignedUnits)} u`],
                                        ['Scheduled', `${f.scheduledHours ?? 0} h/wk`],
                                        ['Program', f.programCode || '—']
                                    ]}
                                    note={`${f.flag} against a department mean of ${facultyLoad.meanUnits} units.`}
                                />}
                            >
                                <span className="dash-list-label"><strong>{f.name}</strong></span>
                                <Bar pct={f.maxLoadUnits === 0 ? 0 : (100 * f.assignedUnits) / f.maxLoadUnits} />
                                <span className="dash-list-value">{f.assignedUnits}/{f.maxLoadUnits} u</span>
                                <span className={flagChip[f.flag] || 'chip chip-muted'}>{f.flag}</span>
                            </Tip>
                        ))}
                    </ul>
                </section>
            </div>

            {/* System census + audit tail: the "what is configured" and "what just happened"
                context that turns a metrics page into an administrative console. */}
            <div className="dash-footer-grid">
                <section className="card dash-panel">
                    <h3>System inventory <small>(master data behind the scheduler)</small></h3>
                    <ul className="dash-census">
                        <CensusCell label="Curricula" value={inventory.curricula}
                            tip={<TipBody title="Program curricula"
                                rows={[['Active', inventory.curricula ?? 0], ['Archived', inventory.curriculaArchived ?? 0]]}
                                note="Archived curricula keep their subjects and history but leave the catalog." />} />
                        <CensusCell label="Subjects" value={inventory.subjects}
                            tip={<TipBody title="Subjects"
                                rows={[['Active', inventory.subjects ?? 0], ['Archived', inventory.subjectsArchived ?? 0]]} />} />
                        <CensusCell label="Rooms" value={inventory.rooms}
                            tip={<TipBody title="Teaching spaces"
                                rows={[
                                    ['Rooms', inventory.rooms ?? 0],
                                    ['Laboratories', inventory.laboratories ?? 0],
                                    ['Buildings', inventory.buildings ?? 0],
                                    ['In use this term', schedule.roomsUsed ?? 0]
                                ]} />} />
                        <CensusCell label="Time slots" value={inventory.timeSlots}
                            tip={<TipBody title="Schedulable slots"
                                rows={[['Defined', inventory.timeSlots ?? 0]]}
                                note="The engine can only place classes into slots defined under System parameters." />} />
                        <CensusCell label="Faculty" value={inventory.faculty}
                            tip={<TipBody title="Faculty profiles"
                                rows={[
                                    ['Profiles', inventory.faculty ?? 0],
                                    ['Carrying load', facultyLoad.members.filter(f => f.assignedUnits > 0).length],
                                    ['Unassigned', unassigned]
                                ]} />} />
                        <CensusCell label="Class blocks" value={inventory.classSections}
                            tip={<TipBody title="Student blocks this term"
                                rows={[['Blocks', inventory.classSections ?? 0], ['Sections', schedule.sectionsTotal ?? 0]]} />} />
                        <CensusCell label="Active users" value={inventory.users}
                            tip={<TipBody title="Accounts"
                                rows={[['Active', inventory.users ?? 0], ['Deactivated', inventory.usersInactive ?? 0]]} />} />
                        <CensusCell label="Semesters" value={inventory.semesters}
                            tip={<TipBody title="Terms on record"
                                rows={[['Semesters', inventory.semesters ?? 0], ['Selected', data.semesterName]]} />} />
                    </ul>
                </section>

                <section className="card dash-panel">
                    <h3>Recent activity <small>(audit trail)</small></h3>
                    {activity.length === 0 ? (
                        <p className="dash-panel-empty">Nothing recorded yet.</p>
                    ) : (
                        <ul className="dash-feed">
                            {activity.map((e, i) => (
                                <Tip
                                    as="li"
                                    key={`${e.occurredAtUtc}-${i}`}
                                    content={<TipBody
                                        title={humanize(e.action)}
                                        rows={[
                                            ['Actor', e.actor || 'System'],
                                            ['Role', e.role || '—'],
                                            ['When', new Date(e.occurredAtUtc).toLocaleString()]
                                        ]}
                                        note={e.summary}
                                    />}
                                >
                                    <span className="feed-dot" aria-hidden="true" />
                                    <span className="feed-body">
                                        <span className="feed-summary">{e.summary}</span>
                                        <span className="feed-meta">{e.actor || 'System'} · {relTime(e.occurredAtUtc)}</span>
                                    </span>
                                </Tip>
                            ))}
                        </ul>
                    )}
                    <Link className="dash-panel-link" to="/audit">Open the full audit trail →</Link>
                </section>
            </div>
        </>
    );
}

/* One cell of the system-inventory census. */
function CensusCell({ label, value, tip }) {
    return (
        <Tip as="li" className="census-cell" content={tip}>
            <span className="census-num">{value ?? '—'}</span>
            <span className="census-label">{label}</span>
        </Tip>
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

    if (error) return <><DashHead /><div className="alert">{error}</div></>;
    if (!link || !mine) return <><DashHead /><p className="dash-loading">Loading your enrollment status…</p></>;

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
        // FR-EVAL: a transferee has one more gate than a new enrollee — the Registrar has to rule
        // on which of their previous subjects count here before there is anything to enlist in. The
        // step only exists for them, so a new student's journey is unchanged.
        ...(r?.studentType === 'Transferee' ? [{
            title: 'Get your credits evaluated',
            detail: r.evaluationStatus === 'Completed'
                ? `${r.creditedUnits} units credited · ${r.toTakeUnits} units to take · ${r.yearLevelLabel}`
                : r.evaluationStatus === 'InProgress'
                    ? 'The Registrar is working through your subjects.'
                    : 'The Registrar reviews your transcript and rules on each subject.',
            done: r.evaluationStatus === 'Completed',
            to: '/my-subjects'
        }] : []),
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
            <DashHead />
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

    if (error) return <><DashHead /><div className="alert">{error}</div></>;
    if (!sched) return <><DashHead /><p className="dash-loading">Loading your teaching week…</p></>;

    return (
        <>
            <DashHead />
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
    if (STAFF_ROLES.includes(user.role)) return <StaffDashboard />;
    return user.role === 'FacultyMember' ? <FacultyDashboard /> : <StudentDashboard />;
}

export default DashboardPage;
