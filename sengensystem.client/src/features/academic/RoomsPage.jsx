import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { listRooms, createRoom, updateRoom, deleteRoom, listBuildings } from './api';
import './academic.css';

function RoomModal({ record, buildings, defaultBuildingId, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { name: '', capacity: '', isLaboratory: false, buildingId: defaultBuildingId || '' }
        : { name: record.name, capacity: String(record.capacity), isLaboratory: record.isLaboratory, buildingId: record.buildingId || '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    function payload() {
        return {
            name: form.name,
            capacity: form.capacity === '' ? null : Number(form.capacity),
            isLaboratory: form.isLaboratory,
            buildingId: form.buildingId || null
        };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createRoom(payload());
            else await updateRoom(record.id, payload());
            notifySuccess(isCreate ? 'Room created.' : 'Room updated.');
            onChanged(); onClose();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        if (!window.confirm(`Delete room “${record.name}”? This can't be undone.`)) return;
        setError(''); setBusy(true);
        try { await deleteRoom(record.id); notifySuccess(`Room “${record.name}” deleted.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <button type="button" className="btn btn-danger setup-foot-spacer" disabled={busy} onClick={remove}>
                    Delete
                </button>
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="room-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New room' : 'Edit room'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="room-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="room-name">Name</label>
                    <input id="room-name" value={form.name} onChange={set('name')} autoComplete="off" placeholder="Room 301" />
                    {err('name') && <p className="field-error">{err('name')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="room-building">Building</label>
                    <select id="room-building" value={form.buildingId} onChange={set('buildingId')}>
                        <option value="">Select a building…</option>
                        {buildings.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
                    </select>
                    {err('buildingId') && <p className="field-error">{err('buildingId')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="room-cap">Capacity</label>
                    <input id="room-cap" type="number" min="1" value={form.capacity} onChange={set('capacity')} placeholder="40" />
                    {err('capacity') && <p className="field-error">{err('capacity')}</p>}
                </div>
                <label className="setup-checkbox">
                    <input type="checkbox" checked={form.isLaboratory}
                        onChange={e => setForm(prev => ({ ...prev, isLaboratory: e.target.checked }))} />
                    This room is a laboratory
                </label>
            </form>
        </SetupModal>
    );
}

export default function RoomsPage() {
    const [rows, setRows] = useState([]);
    const [buildings, setBuildings] = useState([]);
    const [buildingFilter, setBuildingFilter] = useState('All');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try { const data = await listBuildings(); if (active) setBuildings(data.buildings); }
            catch { /* surfaced by the list load below */ }
        })();
        return () => { active = false; };
    }, [reload]);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const data = await listRooms(buildingFilter === 'All' ? undefined : buildingFilter);
                if (active) setRows(data.rooms);
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [buildingFilter, reload]);

    const refresh = () => setReload(r => r + 1);

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Rooms</h2>
                    <p className="setup-sub">
                        Manage teaching rooms. Capacity and the laboratory flag are inputs to the scheduling engine.
                    </p>
                </div>
                <div className="setup-controls">
                    <label className="setup-filter">
                        <span>Building</span>
                        <select value={buildingFilter} onChange={e => setBuildingFilter(e.target.value)}>
                            <option value="All">All</option>
                            {buildings.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
                        </select>
                    </label>
                    <button className="btn btn-primary btn-sm" type="button"
                        disabled={buildings.length === 0}
                        title={buildings.length === 0 ? 'Create a building first' : undefined}
                        onClick={() => setModal({})}>
                        New room
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="setup-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="setup-empty">No rooms{buildingFilter !== 'All' ? ' in this building' : ' yet'}.</p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>Building</th>
                                <th>Capacity</th>
                                <th>Type</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(r => (
                                <tr key={r.id} className="setup-row" onClick={() => setModal(r)}>
                                    <td><strong>{r.name}</strong></td>
                                    <td className="setup-muted">{r.buildingName || '—'}</td>
                                    <td className="setup-num">{r.capacity}</td>
                                    <td>
                                        {r.isLaboratory
                                            ? <span className="chip chip-lab">Laboratory</span>
                                            : <span className="chip chip-muted">Lecture</span>}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && (
                <RoomModal
                    record={modal.id ? modal : null}
                    buildings={buildings}
                    defaultBuildingId={buildingFilter !== 'All' ? buildingFilter : ''}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
