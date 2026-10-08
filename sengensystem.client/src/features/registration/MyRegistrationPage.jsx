import { useEffect, useState } from 'react';
import AddressPicker from './AddressPicker';
import { getMyRegistration, updateMyRegistration } from './api';
import { humanize } from './options';
import { notifySuccess, notifyError } from '../shell/notify';
import '../academic/academic.css';
import './registration.css';

/* F-04: the student's own Student Information Sheet. While it is still awaiting the Registrar's
   confirmation the student can fix a typo themselves instead of queueing at a counter; once
   confirmed it is read-only here and corrections go through the Registrar's office.

   Only the typo-prone personal and contact fields are editable — program, student type, and the
   email are not (see MyRegistrationEndpoints on the server for why). */

const TEXT_FIELDS = [
    ['lastName', 'Last name'],
    ['firstName', 'First name'],
    ['middleName', 'Middle name'],
    ['birthplace', 'Birthplace'],
    ['mobileNumber', 'Mobile number'],
    ['guardianName', 'Guardian name'],
    ['guardianMobile', 'Guardian mobile']
];
const ADDRESS_FIELDS = ['addressLine', 'barangay', 'cityMunicipality', 'province', 'zipCode'];
const EDITABLE = [...TEXT_FIELDS.map(([k]) => k), 'dateOfBirth', ...ADDRESS_FIELDS];

function pick(record) {
    return Object.fromEntries(EDITABLE.map(k => [k, record[k] ?? '']));
}

export default function MyRegistrationPage() {
    const [record, setRecord] = useState(null);
    const [form, setForm] = useState(null);
    const [error, setError] = useState('');
    const [fieldErrors, setFieldErrors] = useState({});
    const [saving, setSaving] = useState(false);
    // The PSGC picker is a cascade that starts empty, so the saved address is shown as text and
    // the picker only opens when the student chooses to change it.
    const [editingAddress, setEditingAddress] = useState(false);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getMyRegistration();
                if (active) { setRecord(data); setForm(pick(data)); }
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, []);

    if (error) return <div className="setup-page"><div className="alert" role="alert"><p>{error}</p></div></div>;
    if (!record || !form) return <div className="setup-page"><p className="setup-empty">Loading…</p></div>;

    const saved = pick(record);
    const changed = EDITABLE.filter(k => form[k] !== saved[k]);
    const err = (name) => fieldErrors[name]?.[0];

    // SIS text is stored in CAPS (FR-AUTH-03) — uppercase as typed, like the public form.
    const set = (field) => (e) => {
        const value = e.target.type === 'date' ? e.target.value : e.target.value.toUpperCase();
        setForm(prev => ({ ...prev, [field]: value }));
    };

    async function save(e) {
        e.preventDefault();
        setSaving(true); setFieldErrors({});
        try {
            const patch = Object.fromEntries(changed.map(k => [k, form[k]]));
            const data = await updateMyRegistration(patch);
            setRecord(data); setForm(pick(data)); setEditingAddress(false);
            notifySuccess('Your Student Information Sheet was updated.');
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
        } finally { setSaving(false); }
    }

    function cancel() {
        setForm(pick(record)); setFieldErrors({}); setEditingAddress(false);
    }

    const address = [record.addressLine, record.barangay, record.cityMunicipality, record.province, record.zipCode]
        .filter(Boolean).join(', ');

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>My Student Information Sheet</h2>
                    <p className="setup-sub">
                        <span className="reg-mono">{record.studentNumber}</span> · {record.program} ·{' '}
                        {humanize(record.studentType)} · {record.email}
                    </p>
                </div>
            </header>

            {record.canEdit ? (
                <div className="alert alert-success" role="status">
                    <p>
                        Your SIS is awaiting the Registrar’s confirmation, so you can still correct it here.
                        Once it is confirmed it locks, and changes go through the Registrar’s office.
                    </p>
                </div>
            ) : (
                <div className="alert" role="status"><p>{record.lockedReason}</p></div>
            )}

            <form className="card myreg-card" onSubmit={save} noValidate>
                <fieldset disabled={!record.canEdit || saving} className="myreg-fieldset">
                    <div className="myreg-grid">
                        {TEXT_FIELDS.map(([name, label]) => (
                            <div className="field" key={name}>
                                <label htmlFor={`my-${name}`}>{label}{name !== 'middleName' && ' *'}</label>
                                <input id={`my-${name}`} type="text" value={form[name]} onChange={set(name)} />
                                {err(name) && <p className="field-error">{err(name)}</p>}
                            </div>
                        ))}
                        <div className="field">
                            <label htmlFor="my-dateOfBirth">Date of birth *</label>
                            <input id="my-dateOfBirth" type="date" value={form.dateOfBirth} onChange={set('dateOfBirth')} />
                            {err('dateOfBirth') && <p className="field-error">{err('dateOfBirth')}</p>}
                        </div>
                    </div>

                    <h4 className="reg-detail-title">Permanent address</h4>
                    {editingAddress ? (
                        <AddressPicker form={form} setForm={setForm} err={err} />
                    ) : (
                        <p className="myreg-address">
                            {address || '—'}
                            {record.canEdit && (
                                <button type="button" className="btn btn-sm" onClick={() => setEditingAddress(true)}>
                                    Change address
                                </button>
                            )}
                        </p>
                    )}
                </fieldset>

                {record.canEdit && (
                    <div className="myreg-actions">
                        <button className="btn btn-primary" type="submit" disabled={saving || changed.length === 0}>
                            {saving && <span className="spinner" aria-hidden="true" />}
                            {saving ? 'Saving…' : 'Save corrections'}
                        </button>
                        <button className="btn" type="button" onClick={cancel} disabled={saving || (changed.length === 0 && !editingAddress)}>
                            Cancel
                        </button>
                    </div>
                )}
            </form>
        </div>
    );
}
