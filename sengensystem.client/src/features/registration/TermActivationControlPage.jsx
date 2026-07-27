import { useEffect, useState } from 'react';
import { getTermActivationControl, setTermActivationControl } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { formatPHT } from './options';
import './registration.css';

/* The institution-wide switch for the public term-activation form (Registrar, Academic Head, and
   the two admin roles — the people who own the enrollment cycle). Closing it stops returning
   students filing for a term that is not open yet; it never touches a request already on file or
   anyone's individual record, so the Admission Office can keep working through the queue with the
   window shut.

   Deliberately its own page rather than a row in System parameters: those are School-Admin-only
   scheduling inputs, and this belongs to the people running enrollment. */

export default function TermActivationControlPage() {
    const [state, setState] = useState(null);
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);

    async function load() {
        try {
            setState(await getTermActivationControl());
            setError('');
        } catch (ex) {
            setError(ex.message);
        }
    }

    useEffect(() => {
        const initial = setTimeout(load, 0);
        return () => clearTimeout(initial);
    }, []);

    async function toggle() {
        const next = !state.open;
        const ok = await confirmAction(next
            ? {
                title: 'Open term activation?',
                message: `Returning students will be able to file activation requests for `
                    + `${state.semesterName ?? 'the active term'}. Each one still goes to the Admission `
                    + 'Office for validation.',
                confirmLabel: 'Open activation'
            }
            : {
                title: 'Close term activation?',
                message: 'The public activation form stops accepting new requests. Requests already '
                    + 'filed are untouched and the Admission Office can keep validating them.',
                confirmLabel: 'Close activation'
            });
        if (!ok) return;

        setBusy(true);
        try {
            const saved = await setTermActivationControl(next);
            setState(saved);
            notifySuccess(next ? 'Term activation is open.' : 'Term activation is closed.');
        } catch (ex) {
            notifyError(ex.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Term activation control</h2>
                    <p className="reg-sub">
                        Whether returning students can file activation requests for the active term
                        through the public form.
                    </p>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {!state ? (
                <p className="reg-empty">Loading…</p>
            ) : (
                <section className="card reg-control-card">
                    <header className="reg-control-head">
                        <div>
                            <h3>{state.open ? 'Term activation is open' : 'Term activation is closed'}</h3>
                            <p className="reg-sub">
                                {state.semesterName
                                    ? <>Requests are filed against <strong>{state.semesterName}</strong>.</>
                                    : 'No semester is active — activation is unavailable regardless of this switch.'}
                            </p>
                        </div>
                        <label className="reg-switch">
                            <input
                                type="checkbox" checked={state.open} disabled={busy}
                                onChange={toggle}
                            />
                            <span>{state.open ? 'Open' : 'Closed'}</span>
                        </label>
                    </header>

                    <dl className="reg-control-facts">
                        <div>
                            <dt>Awaiting validation</dt>
                            <dd>{state.pendingCount}</dd>
                        </div>
                        <div>
                            <dt>Filed this term</dt>
                            <dd>{state.requestCount}</dd>
                        </div>
                        <div>
                            <dt>Last changed</dt>
                            <dd>{state.updatedAtUtc ? formatPHT(state.updatedAtUtc) : '—'}</dd>
                        </div>
                    </dl>

                    <p className="reg-hint reg-control-note">
                        Closing affects the public form only. Requests already on file stay in the
                        Admission Office queue, and no student's record or pre-authorization changes.
                        Every change is recorded in the audit trail.
                    </p>
                </section>
            )}
        </div>
    );
}
