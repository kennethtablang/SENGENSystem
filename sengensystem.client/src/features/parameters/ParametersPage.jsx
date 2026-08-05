import { useEffect, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { hhmm } from '../scheduling/calendarUtils';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
import {
    getParameters, setSectionCapacityCap, updateSettings,
    createTimeSlot, updateTimeSlot, deleteTimeSlot,
    setFacultyLoadLimit
} from './api';
import '../academic/academic.css';
import './parameters.css';

/* The engine schedules Mon–Sat; Sunday is offered for completeness but never seeded. */
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/* <input type="time"> speaks "HH:MM"; the domain stores minutes past midnight. */
const toTimeValue = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
const toMinutes = (v) => {
    const [h, m] = (v || '').split(':').map(Number);
    return Number.isFinite(h) && Number.isFinite(m) ? h * 60 + m : null;
};

// ---------------- Time slot editor ----------------

function TimeSlotModal({ record, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { day: '1', start: '08:00', end: '09:30' }
        : { day: String(record.day), start: toTimeValue(record.startMinutes), end: toTimeValue(record.endMinutes) });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    const payload = () => ({
        day: Number(form.day),
        startMinutes: toMinutes(form.start),
        endMinutes: toMinutes(form.end)
    });

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createTimeSlot(payload());
            else await updateTimeSlot(record.id, payload());
            notifySuccess(isCreate ? 'Time slot added.' : 'Time slot updated.');
            onChanged(); onClose();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        const label = `${DAYS[record.day]} ${hhmm(record.startMinutes)}–${hhmm(record.endMinutes)}`;
        if (!(await confirmDelete(`the ${label} time slot`))) return;
        setError(''); setBusy(true);
        try {
            await deleteTimeSlot(record.id);
            notifySuccess('Time slot removed.');
            onChanged(); onClose();
        } catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <button type="button" className="btn btn-danger setup-foot-spacer" disabled={busy} onClick={remove}>
                    Delete
                </button>
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="slot-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Add slot' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New time slot' : 'Edit time slot'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            {!isCreate && record.inUse && (
                <div className="alert">
                    This slot is used in the published schedule, so it can’t be changed or removed.
                    Add a new slot instead.
                </div>
            )}
            <form id="slot-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="slot-day">Day</label>
                    <select id="slot-day" value={form.day} onChange={set('day')}>
                        {DAYS.map((d, i) => <option key={d} value={i}>{d}</option>)}
                    </select>
                    {err('day') && <p className="field-error">{err('day')}</p>}
                </div>
                <div className="param-time-row">
                    <div className="field">
                        <label htmlFor="slot-start">Starts</label>
                        <input id="slot-start" type="time" value={form.start} onChange={set('start')} />
                        {err('startMinutes') && <p className="field-error">{err('startMinutes')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="slot-end">Ends</label>
                        <input id="slot-end" type="time" value={form.end} onChange={set('end')} />
                        {err('endMinutes') && <p className="field-error">{err('endMinutes')}</p>}
                    </div>
                </div>
            </form>
        </SetupModal>
    );
}

// ---------------- Section seat cap ----------------

// Keyed on the saved cap by the parent, so a reload re-mounts this with the new value as its
// initial state — no effect needed to re-sync the input after a save.
function SeatCapCard({ data, onChanged }) {
    const [value, setValue] = useState(String(data.cap));
    const [saving, setSaving] = useState(false);
    const [fieldError, setFieldError] = useState('');

    const dirty = value !== String(data.cap);

    async function save(e) {
        e.preventDefault();
        setFieldError(''); setSaving(true);
        try {
            const result = await setSectionCapacityCap(value === '' ? null : Number(value));
            notifySuccess(
                result.sectionsAboveCap > 0
                    ? `Seat cap set to ${result.cap}. ${result.sectionsAboveCap} existing section(s) stay above it.`
                    : `Seat cap set to ${result.cap}.`
            );
            onChanged();
        } catch (ex) {
            notifyError(ex.message);
            setFieldError(ex.fieldErrors?.cap?.[0] || ex.message);
        } finally { setSaving(false); }
    }

    return (
        <section className="card param-card">
            <header className="param-card-head">
                <div>
                    <h3>Section seat cap</h3>
                    <p className="setup-sub">
                        The most students any one section may hold. New sections are created at this
                        number, and no section can be set above it.
                    </p>
                </div>
            </header>

            <form className="param-cap-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="seat-cap">Seats per section</label>
                    <input id="seat-cap" type="number" min="1" max={data.maxCap}
                        value={value} onChange={e => setValue(e.target.value)} />
                    {fieldError && <p className="field-error">{fieldError}</p>}
                </div>
                <button className="btn btn-primary" type="submit" disabled={saving || !dirty}>
                    {saving && <span className="spinner" aria-hidden="true" />}
                    {saving ? 'Saving…' : 'Save cap'}
                </button>
                <p className="param-hint">Default is {data.defaultCap}.</p>
            </form>

            {data.sectionsAboveCap > 0 && (
                <p className="param-warn">
                    {data.sectionsAboveCap} existing section{data.sectionsAboveCap === 1 ? '' : 's'} sit
                    above this cap. They keep their seats — students already enlisted are never bumped.
                    Lower each section’s own capacity to bring them in line.
                </p>
            )}
        </section>
    );
}

// ---------------- Enrollment rules ----------------

// Keyed on its values by the parent, so a reload re-mounts with fresh initial state.
function EnrollmentRulesCard({ data, onChanged }) {
    const [open, setOpen] = useState(data.enlistmentOpen);
    const [maxUnits, setMaxUnits] = useState(String(data.maxEnlistmentUnitsPerStudent));
    const [minEnroll, setMinEnroll] = useState(String(data.minSectionEnrollment));
    const [saving, setSaving] = useState(false);
    const [fieldErrors, setFieldErrors] = useState({});

    const dirty = open !== data.enlistmentOpen
        || maxUnits !== String(data.maxEnlistmentUnitsPerStudent)
        || minEnroll !== String(data.minSectionEnrollment);

    async function toggleOpen() {
        const next = !open;
        setOpen(next);
        setSaving(true);
        try {
            await updateSettings({ enlistmentOpen: next });
            notifySuccess(next ? 'Online enlistment opened.' : 'Online enlistment closed.');
            onChanged();
        } catch (ex) {
            setOpen(!next);
            notifyError(ex.message);
        } finally { setSaving(false); }
    }

    async function saveNumbers(e) {
        e.preventDefault();
        setFieldErrors({}); setSaving(true);
        try {
            await updateSettings({
                maxEnlistmentUnitsPerStudent: maxUnits === '' ? 0 : Number(maxUnits),
                minSectionEnrollment: minEnroll === '' ? 0 : Number(minEnroll)
            });
            notifySuccess('Enrollment rules saved.');
            onChanged();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
        } finally { setSaving(false); }
    }

    return (
        <section className="card param-card">
            <header className="param-card-head">
                <div>
                    <h3>Enrollment rules</h3>
                    <p className="setup-sub">
                        Institution-wide gates for online subject enlistment (FR-ENL) — independent of any
                        one student’s eligibility.
                    </p>
                </div>
                <label className="param-switch">
                    <input type="checkbox" checked={open} disabled={saving} onChange={toggleOpen} />
                    <span>{open ? 'Enlistment open' : 'Enlistment closed'}</span>
                </label>
            </header>

            <form className="param-grid-form" onSubmit={saveNumbers} noValidate>
                <div className="field">
                    <label htmlFor="max-units">Max units per student</label>
                    <input id="max-units" type="number" min="0" max={data.maxEnlistmentUnitsCeiling}
                        value={maxUnits} onChange={e => setMaxUnits(e.target.value)} />
                    <p className="field-hint">0 means no institutional ceiling — only per-section capacity applies.</p>
                    {fieldErrors.maxEnlistmentUnitsPerStudent && (
                        <p className="field-error">{fieldErrors.maxEnlistmentUnitsPerStudent[0]}</p>
                    )}
                </div>
                <div className="field">
                    <label htmlFor="min-enroll">Minimum section size</label>
                    <input id="min-enroll" type="number" min="0" max="200"
                        value={minEnroll} onChange={e => setMinEnroll(e.target.value)} />
                    <p className="field-hint">Advisory: sections below this are flagged, never blocked.</p>
                    {fieldErrors.minSectionEnrollment && (
                        <p className="field-error">{fieldErrors.minSectionEnrollment[0]}</p>
                    )}
                </div>
                <button className="btn btn-primary" type="submit" disabled={saving || !dirty}>
                    {saving && <span className="spinner" aria-hidden="true" />}
                    {saving ? 'Saving…' : 'Save rules'}
                </button>
            </form>

            {data.underFilledSections > 0 && (
                <p className="param-warn">
                    {data.underFilledSections} active-term section{data.underFilledSections === 1 ? '' : 's'} sit
                    below the {data.minSectionEnrollment}-seat minimum. Consider merging, promoting, or moving students.
                </p>
            )}
        </section>
    );
}

// ---------------- Scheduling engine budgets ----------------

function EngineBudgetsCard({ data, onChanged }) {
    const [budget, setBudget] = useState(String(data.timeBudgetSeconds));
    const [steps, setSteps] = useState(String(data.maxStepsThousands));
    const [saving, setSaving] = useState(false);
    const [fieldErrors, setFieldErrors] = useState({});

    const dirty = budget !== String(data.timeBudgetSeconds) || steps !== String(data.maxStepsThousands);

    async function save(e) {
        e.preventDefault();
        setFieldErrors({}); setSaving(true);
        try {
            await updateSettings({
                scheduleTimeBudgetSeconds: budget === '' ? null : Number(budget),
                scheduleMaxStepsThousands: steps === '' ? null : Number(steps)
            });
            notifySuccess('Engine budgets saved.');
            onChanged();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
        } finally { setSaving(false); }
    }

    return (
        <section className="card param-card">
            <header className="param-card-head">
                <div>
                    <h3>Scheduling engine budgets</h3>
                    <p className="setup-sub">
                        The safety limits one generation run may use before it stops (FR-SCHED-07). Raise them
                        for large, tightly-constrained terms; lower them for a snappier run.
                    </p>
                </div>
            </header>

            <form className="param-grid-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="time-budget">Time budget (seconds)</label>
                    <input id="time-budget" type="number" min={data.minTimeBudgetSeconds} max={data.maxTimeBudgetSeconds}
                        value={budget} onChange={e => setBudget(e.target.value)} />
                    <p className="field-hint">{data.minTimeBudgetSeconds}–{data.maxTimeBudgetSeconds} s. Default is 20.</p>
                    {fieldErrors.scheduleTimeBudgetSeconds && (
                        <p className="field-error">{fieldErrors.scheduleTimeBudgetSeconds[0]}</p>
                    )}
                </div>
                <div className="field">
                    <label htmlFor="max-steps">Step budget (thousands)</label>
                    <input id="max-steps" type="number" min={data.minMaxStepsThousands} max={data.maxMaxStepsThousands}
                        value={steps} onChange={e => setSteps(e.target.value)} />
                    <p className="field-hint">{data.minMaxStepsThousands}–{data.maxMaxStepsThousands}k steps. Default is 2000.</p>
                    {fieldErrors.scheduleMaxStepsThousands && (
                        <p className="field-error">{fieldErrors.scheduleMaxStepsThousands[0]}</p>
                    )}
                </div>
                <button className="btn btn-primary" type="submit" disabled={saving || !dirty}>
                    {saving && <span className="spinner" aria-hidden="true" />}
                    {saving ? 'Saving…' : 'Save budgets'}
                </button>
            </form>
        </section>
    );
}

// ---------------- Faculty ceilings ----------------

// Keyed on the saved ceiling by the parent (see SeatCapCard) so a reload re-mounts the row.
function LoadLimitRow({ faculty, onChanged }) {
    const [value, setValue] = useState(String(faculty.maxLoadUnits));
    const [saving, setSaving] = useState(false);

    const dirty = value !== String(faculty.maxLoadUnits);
    const over = faculty.assignedUnits > faculty.maxLoadUnits;

    async function save() {
        if (!dirty) return;
        setSaving(true);
        try {
            await setFacultyLoadLimit(faculty.id, value === '' ? null : Number(value));
            notifySuccess(`${faculty.name}’s ceiling set to ${value} units.`);
            onChanged();
        } catch (ex) {
            notifyError(ex.message);
            setValue(String(faculty.maxLoadUnits));
        } finally { setSaving(false); }
    }

    return (
        <tr>
            <td>
                <strong>{faculty.name}</strong>
                {!faculty.isActive && <span className="chip chip-muted param-chip">Deactivated</span>}
            </td>
            <td className="setup-muted">{faculty.employeeId || '—'}</td>
            <td className="setup-muted">{faculty.programCode}</td>
            <td className="setup-num">
                <span className={over ? 'param-over' : undefined}>{faculty.assignedUnits}</span>
            </td>
            <td>
                <div className="param-limit-edit">
                    <input type="number" min="1" max="60" value={value} aria-label={`${faculty.name} unit ceiling`}
                        onChange={e => setValue(e.target.value)} />
                    <button type="button" className="btn btn-sm btn-primary"
                        disabled={!dirty || saving} onClick={save}>
                        {saving ? '…' : 'Save'}
                    </button>
                </div>
            </td>
        </tr>
    );
}

// ---------------- Page ----------------

/* The faculty ceilings table. Its own component so the table controls are a plain unconditional
   hook, and so the ceilings list — the longest thing on this page — filters, sorts, and pages like
   every other table in the system. */
function LoadLimitsTable({ faculty, onChanged }) {
    const [search, setSearch] = useState('');
    const table = useTableControls(faculty, {
        columns: {
            name: f => f.name,
            employeeId: f => f.employeeId,
            programCode: f => f.programCode,
            assignedUnits: f => f.assignedUnits,
            maxLoadUnits: f => f.maxLoadUnits
        },
        initialSort: { key: 'name', dir: 'asc' },
        search,
        searchFields: [f => f.name, f => f.employeeId, f => f.programCode]
    });

    if (faculty.length === 0) {
        return <p className="setup-empty">No faculty profiles yet.</p>;
    }

    return (
        <>
            <div className="table-toolbar">
                <TableSearch value={search} onChange={setSearch} placeholder="Filter faculty…" />
            </div>
            {table.total === 0 ? (
                <p className="setup-empty">No faculty match your filter.</p>
            ) : (
                <div className="setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <SortHeader label="Faculty" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Employee ID" sortKey="employeeId" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="programCode" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Assigned" sortKey="assignedUnits" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Ceiling (units)" sortKey="maxLoadUnits" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(f => (
                                <LoadLimitRow key={`${f.id}-${f.maxLoadUnits}`} faculty={f} onChanged={onChanged} />
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}
        </>
    );
}

export default function ParametersPage() {
    const [data, setData] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const result = await getParameters();
                if (active) setData(result);
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [reload]);

    const refresh = () => setReload(r => r + 1);

    if (loading) return <div className="setup-page"><p className="setup-empty">Loading…</p></div>;
    if (error) return <div className="setup-page"><div className="alert">{error}</div></div>;
    if (!data) return null;

    const slotsByDay = DAYS
        .map((label, day) => ({ label, day, slots: data.timeSlots.filter(s => s.day === day) }))
        .filter(group => group.slots.length > 0);

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>System parameters</h2>
                    <p className="setup-sub">
                        The institutional inputs the scheduling engine runs on. Changes here shape every
                        schedule generated from now on — they never rewrite schedules already published.
                    </p>
                </div>
            </header>

            <SeatCapCard key={data.sectionCapacity.cap} data={data.sectionCapacity} onChanged={refresh} />

            <EnrollmentRulesCard
                key={`enroll-${data.enrollment.enlistmentOpen}-${data.enrollment.maxEnlistmentUnitsPerStudent}-${data.enrollment.minSectionEnrollment}`}
                data={data.enrollment} onChanged={refresh} />

            <EngineBudgetsCard
                key={`engine-${data.engine.timeBudgetSeconds}-${data.engine.maxStepsThousands}`}
                data={data.engine} onChanged={refresh} />

            <section className="card param-card">
                <header className="param-card-head">
                    <div>
                        <h3>Allowable time slots</h3>
                        <p className="setup-sub">
                            The only windows the engine may place a class into. A slot already used in the
                            schedule is locked — add a replacement rather than moving classes underneath.
                        </p>
                    </div>
                    <button className="btn btn-primary btn-sm" type="button" onClick={() => setModal({})}>
                        New slot
                    </button>
                </header>

                {data.timeSlots.length === 0 ? (
                    <p className="setup-empty">
                        No time slots yet. The engine cannot place any class until at least one exists.
                    </p>
                ) : (
                    <div className="param-days">
                        {slotsByDay.map(group => (
                            <div key={group.day} className="param-day">
                                <h4>{group.label}</h4>
                                <ul className="param-slots">
                                    {group.slots.map(slot => (
                                        <li key={slot.id}>
                                            <button type="button" className={`param-slot${slot.inUse ? ' param-slot-locked' : ''}`}
                                                onClick={() => setModal(slot)}
                                                title={slot.inUse ? 'Used in the schedule' : 'Edit this slot'}>
                                                {hhmm(slot.startMinutes)}–{hhmm(slot.endMinutes)}
                                                {slot.inUse && <span className="param-lock" aria-label="in use">•</span>}
                                            </button>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        ))}
                    </div>
                )}
            </section>

            <section className="card param-card">
                <header className="param-card-head">
                    <div>
                        <h3>Faculty unit-load ceilings</h3>
                        <p className="setup-sub">
                            The most units each member may be assigned in a semester. “Assigned” is their
                            load in the active semester; a ceiling can’t go below it.
                        </p>
                    </div>
                </header>

                <LoadLimitsTable faculty={data.faculty} onChanged={refresh} />
            </section>

            {modal && (
                <TimeSlotModal
                    record={modal.id ? modal : null}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
