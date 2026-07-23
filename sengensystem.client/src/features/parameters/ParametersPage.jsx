import { useEffect, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { hhmm } from '../scheduling/calendarUtils';
import {
    getParameters, setSectionCapacityCap,
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

                {data.faculty.length === 0 ? (
                    <p className="setup-empty">No faculty profiles yet.</p>
                ) : (
                    <div className="setup-table-wrap">
                        <table className="setup-table">
                            <thead>
                                <tr>
                                    <th>Faculty</th>
                                    <th>Employee ID</th>
                                    <th>Program</th>
                                    <th>Assigned</th>
                                    <th>Ceiling (units)</th>
                                </tr>
                            </thead>
                            <tbody>
                                {data.faculty.map(f => (
                                    <LoadLimitRow key={`${f.id}-${f.maxLoadUnits}`} faculty={f} onChanged={refresh} />
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
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
