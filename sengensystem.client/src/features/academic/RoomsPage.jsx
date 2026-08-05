import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
import { listRooms, createRoom, updateRoom, deleteRoom, listBuildings } from './api';
import './academic.css';

// A room is one of three things, and which one is a hard scheduling constraint: laboratory
// hours can only be plotted in the laboratory their subject requires, and lecture hours only
// in a lecture room. STI Alaminos has one Computer Lab and one Kitchen Lab.
const roomKinds = [
    { value: 'LectureRoom', label: 'Lecture room', hint: 'Ordinary classroom — lecture hours only.' },
    { value: 'ComputerLaboratory', label: 'Computer laboratory', hint: 'Required by ITP laboratory subjects.' },
    { value: 'KitchenLaboratory', label: 'Kitchen laboratory', hint: 'Required by HRA/HRS laboratory subjects.' }
];

function RoomModal({ record, buildings, defaultBuildingId, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { name: '', capacity: '', kind: 'LectureRoom', buildingId: defaultBuildingId || '' }
        : { name: record.name, capacity: String(record.capacity), kind: record.kind, buildingId: record.buildingId || '' });
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
            kind: form.kind,
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
        if (!(await confirmDelete(`room “${record.name}”`))) return;
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
                <div className="field">
                    <label htmlFor="room-kind">Room type</label>
                    <select id="room-kind" value={form.kind} onChange={set('kind')}>
                        {roomKinds.map(k => <option key={k.value} value={k.value}>{k.label}</option>)}
                    </select>
                    <p className="field-hint">{roomKinds.find(k => k.value === form.kind)?.hint}</p>
                    {err('kind') && <p className="field-error">{err('kind')}</p>}
                </div>
            </form>
        </SetupModal>
    );
}

export default function RoomsPage() {
    const [rows, setRows] = useState([]);
    const [search, setSearch] = useState('');
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

    // Building is a server-side filter; the text box, sorting, and paging work over the result.
    const table = useTableControls(rows, {
        columns: {
            name: r => r.name,
            buildingName: r => r.buildingName,
            capacity: r => r.capacity,
            kindLabel: r => r.kindLabel
        },
        initialSort: { key: 'name', dir: 'asc' },
        search,
        searchFields: [r => r.name, r => r.buildingName, r => r.kindLabel]
    });

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Rooms</h2>
                    <p className="setup-sub">
                        Add lecture rooms and laboratories. Capacity and room type are hard constraints in the
                        scheduling engine — a subject’s laboratory hours can only be plotted in the laboratory it requires.
                    </p>
                </div>
                <div className="setup-controls">
                    <TableSearch value={search} onChange={setSearch} placeholder="Filter room or building…" />
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
            ) : table.total === 0 ? (
                <p className="setup-empty">
                    {search
                        ? 'No rooms match your filter.'
                        : `No rooms${buildingFilter !== 'All' ? ' in this building' : ' yet'}.`}
                </p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <SortHeader label="Name" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Building" sortKey="buildingName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Capacity" sortKey="capacity" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Type" sortKey="kindLabel" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(r => (
                                <tr key={r.id} className="setup-row" onClick={() => setModal(r)}>
                                    <td><strong>{r.name}</strong></td>
                                    <td className="setup-muted">{r.buildingName || '—'}</td>
                                    <td className="setup-num">{r.capacity}</td>
                                    <td>
                                        <span className={`chip ${r.isLaboratory ? 'chip-lab' : 'chip-muted'}`}>
                                            {r.kindLabel}
                                        </span>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
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
