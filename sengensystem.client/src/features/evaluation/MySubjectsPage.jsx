import { useEffect, useState } from 'react';
import { downloadMySubjects, downloadMyRegistrationForm } from './api';
import { getMyLink } from '../documents/api';
import { notifySuccess, notifyError } from '../shell/notify';
import { humanize } from '../registration/options';
import '../registration/registration.css';
import './evaluation.css';

/* FR-RPT-05, student side: the two sheets a student actually asks the Registrar's window for —
   "what does my year take?" and "what am I enrolled in?" — as their own downloads. An evaluated
   transferee's curriculum copy comes back with their credited subjects marked, so it reads as
   what they still have to take rather than a generic year plan. */

const yearLabel = (n) => (['1st year', '2nd year', '3rd year', '4th year'][n - 1] ?? `Year ${n}`);

export default function MySubjectsPage() {
    const [state, setState] = useState(null);
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getMyLink();
                if (active) setState(data);
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, []);

    async function download(kind) {
        setBusy(kind);
        try {
            if (kind === 'curriculum') {
                await downloadMySubjects();
                notifySuccess('Downloaded your subject list.');
            } else {
                await downloadMyRegistrationForm();
                notifySuccess('Downloaded your certificate of registration.');
            }
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy(null);
        }
    }

    const r = state?.registration;

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>My subjects</h2>
                    <p className="reg-sub">
                        The subjects your year level takes, and a printable copy of the ones you hold a seat in
                        this term.
                    </p>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {!state ? (
                <p className="reg-empty">Loading…</p>
            ) : !state.linked ? (
                <p className="reg-empty">
                    Your account is not linked to a student record yet. Claim it under Document requirements
                    first — these sheets are built from your registration.
                </p>
            ) : (
                <>
                    <div className="card pros-me-head">
                        <div>
                            <h3>{r.fullName}</h3>
                            <span className="reg-mono">{r.studentNumber}</span> · {r.program} · {humanize(r.studentType)}
                            {r.yearLevel ? <> · {yearLabel(r.yearLevel)}</> : null}
                        </div>
                    </div>

                    <div className="pros-grid">
                        <section className="card pros-card">
                            <header className="pros-card-head">
                                <div>
                                    <h3>My curriculum</h3>
                                    <p>
                                        Every subject your year level takes, with units, hours, and prerequisites.
                                        {r.studentType === 'Transferee'
                                            ? ' Once the Registrar completes your credit evaluation, the subjects credited from your previous school are marked.'
                                            : ''}
                                    </p>
                                </div>
                            </header>
                            <footer className="pros-card-foot">
                                <button
                                    type="button" className="btn btn-primary btn-sm"
                                    disabled={busy !== null}
                                    onClick={() => download('curriculum')}
                                >
                                    {busy === 'curriculum' ? 'Preparing…' : 'Download PDF'}
                                </button>
                            </footer>
                        </section>

                        <section className="card pros-card">
                            <header className="pros-card-head">
                                <div>
                                    <h3>Certificate of registration</h3>
                                    <p>
                                        The subjects you are enrolled in this term with their schedule — day, time,
                                        room, and faculty. Lists the classes the Registrar has approved.
                                    </p>
                                </div>
                            </header>
                            <footer className="pros-card-foot">
                                <button
                                    type="button" className="btn btn-sm"
                                    disabled={busy !== null}
                                    onClick={() => download('registration')}
                                >
                                    {busy === 'registration' ? 'Preparing…' : 'Download PDF'}
                                </button>
                            </footer>
                        </section>
                    </div>
                </>
            )}
        </div>
    );
}
