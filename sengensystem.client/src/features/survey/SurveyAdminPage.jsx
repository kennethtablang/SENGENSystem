import { Fragment, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { listInvitations, getResults, getCollection, setCollection, exportResults, remindInvitations } from './api';
import './survey.css';

/* Super Admin dashboard for the ISO/IEC 25010 evaluation: the usability report built from every
   submitted response, plus the control that keeps collection running until the Super Admin is
   satisfied with the number gathered. Choosing who participates lives on /survey-recipients. */

function fmt(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('en-PH', {
        timeZone: 'Asia/Manila', month: 'short', day: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: true
    });
}

/** Fill proportional to a 1–5 Likert mean. */
function Bar({ value, max = 5 }) {
    const pct = Math.max(0, Math.min(100, (value / max) * 100));
    return <div className="survey-bar"><span style={{ width: `${pct}%` }} /></div>;
}

function Interpretation({ value }) {
    if (!value || value === 'No data') return <span className="setup-muted">—</span>;
    const tone = value === 'Strongly Agree' || value === 'Agree' ? 'chip chip-blue'
        : value === 'Neutral' ? 'chip chip-yellow'
            : 'chip chip-red';
    return <span className={tone}>{value}</span>;
}

function SurveyAdminPage() {
    const [tab, setTab] = useState('dashboard');
    const [invitations, setInvitations] = useState(null);
    const [results, setResults] = useState(null);
    const [collection, setCollectionState] = useState(null);
    const [target, setTarget] = useState('');
    const [busy, setBusy] = useState('');

    const [reload, setReload] = useState(0);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const [inv, res, coll] = await Promise.all([listInvitations(), getResults(), getCollection()]);
                if (!active) return;
                setInvitations(inv);
                setResults(res);
                setCollectionState(coll);
                setTarget(String(coll.targetResponses));
            } catch (err) {
                if (active) notifyError(err.message);
            }
        })();
        return () => { active = false; };
    }, [reload]);

    async function toggleCollection() {
        const opening = !collection.isOpen;
        const ok = await confirmAction({
            title: opening ? 'Reopen collection?' : 'Close collection?',
            message: opening
                ? 'Invited people will be able to submit their evaluation again.'
                : `You have ${collection.responseCount} response(s). Closing stops new submissions — ` +
                  'everything already collected is kept, and you can reopen at any time.',
            confirmLabel: opening ? 'Reopen' : 'Close collection',
            danger: !opening
        });
        if (!ok) return;

        setBusy('collection');
        try {
            const next = await setCollection({ isOpen: opening });
            setCollectionState(next);
            notifySuccess(opening ? 'Collection reopened.' : 'Collection closed.');
            setReload(v => v + 1);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy('');
        }
    }

    async function saveTarget() {
        const value = Number(target);
        if (!Number.isFinite(value) || value < 1) {
            notifyError('Enter a response goal of 1 or more.');
            return;
        }
        setBusy('target');
        try {
            const next = await setCollection({ targetResponses: value });
            setCollectionState(next);
            notifySuccess(`Response goal set to ${next.targetResponses}.`);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy('');
        }
    }

    async function remindPending() {
        const ok = await confirmAction({
            title: 'Remind everyone still pending?',
            message: 'Every invited person who has not answered yet gets another notification on their bell.',
            confirmLabel: 'Send reminders'
        });
        if (!ok) return;
        setBusy('remind');
        try {
            const res = await remindInvitations({});
            notifySuccess(res.reminded === 0 ? 'Nobody is pending.' : `Reminded ${res.reminded} respondent(s).`);
            setReload(v => v + 1);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy('');
        }
    }

    async function download() {
        try {
            await exportResults();
        } catch (err) {
            notifyError(err.message);
        }
    }

    const tabs = [
        ['dashboard', 'Usability report'],
        ['responses', `Responses${results ? ` (${results.responseCount})` : ''}`],
        ['invitations', `Invitations${invitations ? ` (${invitations.completed}/${invitations.total})` : ''}`]
    ];

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Rating survey</h2>
                    <p className="setup-sub">
                        The ISO/IEC 25010 software-quality evaluation, answered in English and Filipino. Responses
                        keep coming in until you close collection.
                    </p>
                </div>
                <Link className="btn btn-primary" to="/survey-recipients">Choose recipients</Link>
            </header>

            {/* Collection window — the Super Admin's control over how many responses are gathered */}
            {collection && (
                <section className={`card param-card survey-collection${collection.isOpen ? '' : ' is-closed'}`}>
                    <div className="survey-collection-head">
                        <div>
                            <h3>
                                Collection is {collection.isOpen ? <span className="chip chip-blue">Open</span> : <span className="chip chip-red">Closed</span>}
                            </h3>
                            <p className="setup-sub">
                                {collection.isOpen
                                    ? 'Invited users can submit their evaluation. Close it once you have enough responses.'
                                    : `Closed ${fmt(collection.closedAtUtc)}${collection.lastChangedBy ? ` by ${collection.lastChangedBy}` : ''}. New submissions are turned away.`}
                            </p>
                        </div>
                        <button
                            type="button"
                            className={`btn ${collection.isOpen ? 'btn-ghost' : 'btn-primary'}`}
                            disabled={busy !== ''}
                            onClick={toggleCollection}
                        >
                            {busy === 'collection' && <span className="spinner" aria-hidden="true" />}
                            {collection.isOpen ? 'Close collection' : 'Reopen collection'}
                        </button>
                    </div>

                    <div className="survey-progress">
                        <div className="survey-progress-label">
                            <span><strong>{collection.responseCount}</strong> of {collection.targetResponses} target responses</span>
                            <span className={collection.targetMet ? 'chip chip-blue' : 'setup-muted'}>
                                {collection.targetMet ? 'Target met' : `${collection.progress}%`}
                            </span>
                        </div>
                        <Bar value={collection.progress} max={100} />
                    </div>

                    <div className="survey-actions">
                        <label className="survey-field survey-target">
                            <span>Response goal</span>
                            <input
                                type="number"
                                min="1"
                                value={target}
                                onChange={e => setTarget(e.target.value)}
                            />
                        </label>
                        <button type="button" className="btn btn-ghost btn-sm" disabled={busy !== ''} onClick={saveTarget}>
                            {busy === 'target' && <span className="spinner" aria-hidden="true" />}
                            Save goal
                        </button>
                        <button type="button" className="btn btn-ghost btn-sm" disabled={busy !== '' || !collection.isOpen} onClick={remindPending}>
                            {busy === 'remind' && <span className="spinner" aria-hidden="true" />}
                            Remind pending
                        </button>
                        <button type="button" className="btn btn-ghost btn-sm" disabled={!results?.responseCount} onClick={download}>
                            Export CSV
                        </button>
                    </div>
                </section>
            )}

            <nav className="survey-tabs">
                {tabs.map(([id, label]) => (
                    <button key={id} type="button" className={`survey-tab${tab === id ? ' active' : ''}`} onClick={() => setTab(id)}>
                        {label}
                    </button>
                ))}
            </nav>

            {!results ? <p className="setup-empty">Loading…</p> : tab === 'dashboard' ? (
                <>
                    <section className="card param-card">
                        <div className="survey-stats">
                            <div><strong>{results.responseCount}</strong><span>Responses</span></div>
                            <div><strong>{results.responseRate}%</strong><span>Response rate ({results.invitedCount} invited)</span></div>
                            <div><strong>{results.overallAverage}</strong><span>Overall mean (of 5)</span></div>
                            <div><strong>{results.usabilityAverage}</strong><span>Usability mean</span></div>
                        </div>
                        <p className="setup-sub survey-verdict">
                            Overall verbal interpretation: <Interpretation value={results.overallInterpretation} />
                        </p>
                    </section>

                    {results.responseCount === 0 ? (
                        <section className="card param-card">
                            <p className="setup-empty">
                                No responses yet. <Link to="/survey-recipients">Choose recipients</Link> to send the survey out.
                            </p>
                        </section>
                    ) : (
                        <>
                            <section className="card param-card">
                                <h3>Scores by ISO 25010 characteristic</h3>
                                <p className="setup-sub">Mean of every 1–5 answer within each characteristic.</p>
                                {results.characteristics.map(c => (
                                    <div className="survey-charrow" key={c.code}>
                                        <div className="survey-charname">
                                            <strong>{c.nameEn}</strong>
                                            <span>{c.nameFil}</span>
                                        </div>
                                        <Bar value={c.average} />
                                        <span className="survey-charavg">{c.average}</span>
                                        <span className="survey-charinterp"><Interpretation value={c.interpretation} /></span>
                                    </div>
                                ))}
                            </section>

                            <section className="card param-card">
                                <h3>Answer distribution</h3>
                                <p className="setup-sub">How the {results.responseCount} respondents used the 1–5 scale across all items.</p>
                                {results.distribution.map(d => (
                                    <div className="survey-charrow" key={d.score}>
                                        <div className="survey-charname">
                                            <strong>{d.score}</strong>
                                            <span>{['Strongly Disagree', 'Disagree', 'Neutral', 'Agree', 'Strongly Agree'][d.score - 1]}</span>
                                        </div>
                                        <Bar value={d.percent} max={100} />
                                        <span className="survey-charavg">{d.percent}%</span>
                                        <span className="survey-charinterp setup-muted">{d.count}</span>
                                    </div>
                                ))}
                            </section>

                            <section className="card param-card">
                                <h3>By respondent role</h3>
                                <div className="setup-table-wrap">
                                    <table className="setup-table">
                                        <thead>
                                            <tr><th>Role</th><th>Respondents</th><th>Mean</th><th>Interpretation</th></tr>
                                        </thead>
                                        <tbody>
                                            {results.byRole.map(r => (
                                                <tr key={r.role}>
                                                    <td><strong>{r.role}</strong></td>
                                                    <td className="setup-muted">{r.respondents}</td>
                                                    <td>{r.average}</td>
                                                    <td><Interpretation value={r.interpretation} /></td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            </section>

                            <section className="card param-card">
                                <h3>Item-by-item results</h3>
                                <div className="setup-table-wrap">
                                    <table className="setup-table">
                                        <thead>
                                            <tr><th>Statement</th><th>n</th><th>Mean</th><th>Interpretation</th></tr>
                                        </thead>
                                        <tbody>
                                            {results.characteristics.map(c => (
                                                <Fragment key={c.code}>
                                                    <tr className="survey-group-row">
                                                        <td colSpan={4}><strong>{c.nameEn}</strong> · {c.nameFil}</td>
                                                    </tr>
                                                    {c.questions.map(q => (
                                                        <tr key={q.key}>
                                                            <td>
                                                                <span>{q.en}</span>
                                                                <span className="survey-q-fil survey-q-block">{q.fil}</span>
                                                            </td>
                                                            <td className="setup-muted">{q.count}</td>
                                                            <td>{q.average}</td>
                                                            <td><Interpretation value={q.interpretation} /></td>
                                                        </tr>
                                                    ))}
                                                </Fragment>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            </section>

                            {results.responses.some(r => r.suggestions || r.furtherComments) && (
                                <section className="card param-card">
                                    <h3>Suggestions &amp; further comments</h3>
                                    <ul className="survey-comments">
                                        {results.responses.filter(r => r.suggestions || r.furtherComments).map((r, idx) => (
                                            <li key={idx}>
                                                <span className="survey-comment-who">{r.respondentName} · {r.respondentRole}{r.department ? ` · ${r.department}` : ''}</span>
                                                {r.suggestions && <p><em>Suggestion:</em> {r.suggestions}</p>}
                                                {r.furtherComments && <p><em>Comment:</em> {r.furtherComments}</p>}
                                            </li>
                                        ))}
                                    </ul>
                                </section>
                            )}
                        </>
                    )}
                </>
            ) : tab === 'responses' ? (
                <section className="card param-card">
                    <h3>Respondents</h3>
                    {results.responses.length === 0 ? (
                        <p className="setup-empty">No responses yet.</p>
                    ) : (
                        <div className="setup-table-wrap">
                            <table className="setup-table">
                                <thead>
                                    <tr>
                                        <th>Name</th><th>Role</th><th>Email</th><th>Position</th><th>Dept</th>
                                        <th>Age</th><th>Sex</th><th>Years</th><th>Mean</th><th>Interpretation</th><th>Submitted</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {results.responses.map((r, idx) => (
                                        <tr key={idx}>
                                            <td><strong>{r.respondentName}</strong></td>
                                            <td className="setup-muted">{r.respondentRole}</td>
                                            <td className="setup-muted">{r.respondentEmail}</td>
                                            <td className="setup-muted">{r.position || '—'}</td>
                                            <td className="setup-muted">{r.department || '—'}</td>
                                            <td className="setup-muted">{r.age ?? '—'}</td>
                                            <td className="setup-muted">{r.sex || '—'}</td>
                                            <td className="setup-muted">{r.yearsUsing || '—'}</td>
                                            <td><span className="chip chip-blue">{r.average}</span></td>
                                            <td><Interpretation value={r.interpretation} /></td>
                                            <td className="setup-muted">{fmt(r.submittedAtUtc)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </section>
            ) : (
                <section className="card param-card">
                    <h3>Invitations</h3>
                    {!invitations ? <p className="setup-empty">Loading…</p> : invitations.invitations.length === 0 ? (
                        <p className="setup-empty">
                            No invitations sent yet. <Link to="/survey-recipients">Choose recipients</Link> to get started.
                        </p>
                    ) : (
                        <div className="setup-table-wrap">
                            <table className="setup-table">
                                <thead>
                                    <tr><th>Name</th><th>Role</th><th>Email</th><th>Sent</th><th>Notified</th><th>Nudges</th><th>Status</th></tr>
                                </thead>
                                <tbody>
                                    {invitations.invitations.map(i => (
                                        <tr key={i.id}>
                                            <td><strong>{i.name}</strong></td>
                                            <td className="setup-muted">{i.role}</td>
                                            <td className="setup-muted">{i.email}</td>
                                            <td className="setup-muted">{fmt(i.sentAtUtc)}</td>
                                            <td className="setup-muted">{fmt(i.notifiedAtUtc)}</td>
                                            <td className="setup-muted">{i.reminderCount || '—'}</td>
                                            <td>
                                                {i.completedAtUtc
                                                    ? <span className="chip chip-blue">Answered · {fmt(i.completedAtUtc)}</span>
                                                    : <span className="chip chip-yellow">Pending</span>}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </section>
            )}
        </div>
    );
}

export default SurveyAdminPage;
