import { useEffect, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { getFacultyPreferences, saveFacultyPreferences } from './api';
import { notifySuccess, notifyError } from '../shell/notify';

/* FR-SCHED-03: preferred teaching windows per faculty member. The CSP engine rewards
   placements inside these windows (soft constraint — hard constraints always win). */

const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// 07:00 … 18:00 on the half hour, matching the schedule board's teaching window.
const times = [];
for (let m = 7 * 60; m <= 18 * 60; m += 30) times.push(m);

const label = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

export default function FacultyPreferencesModal({ faculty, onClose }) {
    const [windows, setWindows] = useState([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getFacultyPreferences(faculty.facultyProfileId);
                if (active) setWindows(data.windows);
            } catch (ex) {
                if (active) setError(ex.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [faculty.facultyProfileId]);

    function update(index, patch) {
        setSaved(false);
        setWindows(prev => prev.map((w, i) => (i === index ? { ...w, ...patch } : w)));
    }

    function add() {
        setSaved(false);
        setWindows(prev => [...prev, { day: 'Monday', startMinutes: 8 * 60, endMinutes: 12 * 60 }]);
    }

    function remove(index) {
        setSaved(false);
        setWindows(prev => prev.filter((_, i) => i !== index));
    }

    async function save() {
        setSaving(true);
        setError('');
        try {
            const data = await saveFacultyPreferences(faculty.facultyProfileId, windows);
            setWindows(data.windows);
            setSaved(true);
            notifySuccess(`Saved ${data.windows.length} preferred window(s) for ${faculty.name}.`);
        } catch (ex) {
            setError(ex.message);
            notifyError(ex.message);
        } finally {
            setSaving(false);
        }
    }

    return (
        <SetupModal
            title={`Preferred teaching windows — ${faculty.name}`}
            onClose={onClose}
            footer={
                <>
                    <span style={{ marginRight: 'auto', fontSize: '0.8rem', color: 'var(--text-3)' }}>
                        {saved ? 'Saved — the next generation run will honor these.' :
                            'The engine rewards classes inside these windows; hard constraints always win.'}
                    </span>
                    <button className="btn" type="button" onClick={onClose}>Close</button>
                    <button className="btn btn-primary" type="button" onClick={save} disabled={saving || loading}>
                        {saving ? 'Saving…' : 'Save preferences'}
                    </button>
                </>
            }
        >
            {error && <div className="alert">{error}</div>}
            {loading ? (
                <p style={{ color: 'var(--text-3)', fontSize: '0.88rem' }}>Loading…</p>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
                    {windows.length === 0 && (
                        <p style={{ color: 'var(--text-3)', fontSize: '0.85rem' }}>
                            No preferences yet — with none set, the engine treats every slot equally
                            for this member.
                        </p>
                    )}
                    {windows.map((w, i) => (
                        <div key={i} style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
                            <select value={w.day} onChange={e => update(i, { day: e.target.value })}>
                                {days.map(d => <option key={d} value={d}>{d}</option>)}
                            </select>
                            <select
                                value={w.startMinutes}
                                onChange={e => update(i, { startMinutes: Number(e.target.value) })}
                            >
                                {times.map(t => <option key={t} value={t}>{label(t)}</option>)}
                            </select>
                            <span style={{ color: 'var(--text-3)' }}>to</span>
                            <select
                                value={w.endMinutes}
                                onChange={e => update(i, { endMinutes: Number(e.target.value) })}
                            >
                                {times.map(t => <option key={t} value={t}>{label(t)}</option>)}
                            </select>
                            <button className="btn" type="button" onClick={() => remove(i)}>Remove</button>
                        </div>
                    ))}
                    <div>
                        <button className="btn" type="button" onClick={add}>+ Add window</button>
                    </div>
                </div>
            )}
        </SetupModal>
    );
}
