import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { listBuildings, createBuilding, updateBuilding, deleteBuilding } from './api';
import './academic.css';

function BuildingModal({ record, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate ? { name: '', code: '' } : { name: record.name, code: record.code || '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createBuilding(form);
            else await updateBuilding(record.id, form);
            notifySuccess(isCreate ? 'Building created.' : 'Building updated.');
            onChanged(); onClose();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        if (!(await confirmDelete(`building “${record.name}”`))) return;
        setError(''); setBusy(true);
        try { await deleteBuilding(record.id); notifySuccess(`Building “${record.name}” deleted.`); onChanged(); onClose(); }
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
            <button type="submit" form="bldg-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New building' : 'Edit building'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="bldg-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="bldg-name">Name</label>
                    <input id="bldg-name" value={form.name} onChange={set('name')} autoComplete="off" placeholder="Main Building" />
                    {err('name') && <p className="field-error">{err('name')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="bldg-code">Code <span className="setup-muted">(optional)</span></label>
                    <input id="bldg-code" value={form.code} onChange={set('code')} autoComplete="off" placeholder="MB" />
                    {err('code') && <p className="field-error">{err('code')}</p>}
                </div>
            </form>
        </SetupModal>
    );
}

export default function BuildingsPage() {
    const [rows, setRows] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try { const data = await listBuildings(); if (active) setRows(data.buildings); }
            catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [reload]);

    const refresh = () => setReload(r => r + 1);

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Buildings</h2>
                    <p className="setup-sub">Manage campus buildings. Each building groups the rooms it contains.</p>
                </div>
                <div className="setup-controls">
                    <button className="btn btn-primary btn-sm" type="button" onClick={() => setModal({})}>
                        New building
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="setup-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="setup-empty">No buildings yet.</p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>Code</th>
                                <th>Rooms</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(b => (
                                <tr key={b.id} className="setup-row" onClick={() => setModal(b)}>
                                    <td><strong>{b.name}</strong></td>
                                    <td className="setup-muted">{b.code || '—'}</td>
                                    <td className="setup-num">{b.roomCount}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && (
                <BuildingModal
                    record={modal.id ? modal : null}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
