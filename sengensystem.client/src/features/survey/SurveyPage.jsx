import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Wordmark } from '../auth/AuthLayout';
import { getSurvey, submitSurvey, getMySurvey, submitMySurvey } from './api';
import './survey.css';

const SEX_OPTIONS = ['', 'Female', 'Male', 'Prefer not to say'];
const YEARS_OPTIONS = ['', 'Less than 1 year', '1–2 years', '3–4 years', '5 years or more'];

/* The same instrument reached two ways: /survey/:token from the emailed link (signed out), and
   /survey from the bell notice the Super Admin pushed (signed in). Only the transport differs. */
function SurveyPage() {
    const { token } = useParams();
    // Signed-in takers already sit inside the app shell, so the standalone wash and wordmark
    // (which the emailed-link version needs) would only duplicate the branding around them.
    const embedded = !token;
    const shellClass = `survey-shell${embedded ? ' survey-embedded' : ''}`;
    const [state, setState] = useState({ loading: true, error: '', data: null });
    const [profile, setProfile] = useState({
        respondentName: '', position: '', age: '', sex: '', department: '', yearsUsing: ''
    });
    const [answers, setAnswers] = useState({});
    const [suggestions, setSuggestions] = useState('');
    const [furtherComments, setFurtherComments] = useState('');
    const [saving, setSaving] = useState(false);
    const [submitted, setSubmitted] = useState(false);
    const [formError, setFormError] = useState('');

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = token ? await getSurvey(token) : await getMySurvey();
                if (!active) return;
                setState({ loading: false, error: '', data });
                setProfile(p => ({ ...p, respondentName: data.respondent?.name ?? '' }));
            } catch (err) {
                if (active) setState({ loading: false, error: err.message, data: null });
            }
        })();
        return () => { active = false; };
    }, [token]);

    const instrument = state.data?.instrument;
    const totalQuestions = useMemo(
        () => (instrument?.characteristics ?? []).reduce((n, c) => n + c.questions.length, 0),
        [instrument]
    );
    const answeredCount = Object.keys(answers).length;

    const setProfileField = f => e => setProfile(p => ({ ...p, [f]: e.target.value }));

    async function submit(e) {
        e.preventDefault();
        setFormError('');
        if (answeredCount < totalQuestions) {
            setFormError('Please answer every rating question before submitting. / Pakisagot ang lahat ng katanungan.');
            return;
        }
        setSaving(true);
        try {
            const payload = {
                respondentName: profile.respondentName,
                position: profile.position,
                age: profile.age === '' ? null : Number(profile.age),
                sex: profile.sex,
                department: profile.department,
                yearsUsing: profile.yearsUsing,
                answers,
                suggestions,
                furtherComments
            };
            if (token) await submitSurvey(token, payload);
            else await submitMySurvey(payload);
            setSubmitted(true);
        } catch (err) {
            setFormError(err.message);
        } finally {
            setSaving(false);
        }
    }

    if (state.loading) {
        return <div className={shellClass}><p className="survey-note">Loading…</p></div>;
    }
    if (state.error) {
        return (
            <div className={shellClass}>
                <div className="survey-card survey-centered">
                    {!embedded && <Wordmark />}
                    <h1>Survey unavailable</h1>
                    <p className="survey-note">{state.error}</p>
                </div>
            </div>
        );
    }
    if (submitted || state.data.completed) {
        return (
            <div className={shellClass}>
                <div className="survey-card survey-centered">
                    {!embedded && <Wordmark />}
                    <h1>Thank you! · Salamat!</h1>
                    <p className="survey-note">
                        Your evaluation has been recorded. We appreciate your feedback on SEN-GEN.<br />
                        Naitala na ang iyong sagot. Salamat sa iyong puna.
                    </p>
                </div>
            </div>
        );
    }
    // Collection has been closed by the Super Admin — don't let anyone fill in a form that
    // the server will only reject.
    if (state.data.isOpen === false) {
        return (
            <div className={shellClass}>
                <div className="survey-card survey-centered">
                    {!embedded && <Wordmark />}
                    <h1>The survey is closed</h1>
                    <p className="survey-note">
                        Enough responses have been gathered, so the evaluation is no longer accepting answers.
                        Thank you for your interest!<br />
                        Sarado na ang sagutan ng pagsusuri. Salamat sa iyong interes!
                    </p>
                </div>
            </div>
        );
    }

    return (
        <div className={shellClass}>
            <form className="survey-card" onSubmit={submit} noValidate>
                <header className="survey-head">
                    {!embedded && <Wordmark />}
                    <h1>{instrument.title}</h1>
                    <p className="survey-sub">{instrument.titleFil}</p>
                    <p className="survey-note">
                        Please rate each statement from 1 to 5. Questions are shown in English and Filipino.<br />
                        Pakisuri ang bawat pahayag mula 1 hanggang 5.
                    </p>
                    {state.data.note && <p className="survey-invite-note">{state.data.note}</p>}
                </header>

                {/* Respondent profile */}
                <section className="survey-section">
                    <h2>About you · Tungkol sa iyo</h2>
                    <div className="survey-grid">
                        <label className="survey-field">
                            <span>Full name · Buong pangalan</span>
                            <input value={profile.respondentName} onChange={setProfileField('respondentName')} required />
                        </label>
                        <label className="survey-field">
                            <span>Position / Designation · Posisyon</span>
                            <input value={profile.position} onChange={setProfileField('position')} placeholder="e.g. Student, Registrar" />
                        </label>
                        <label className="survey-field">
                            <span>Department / Program · Departamento</span>
                            <input value={profile.department} onChange={setProfileField('department')} placeholder="e.g. BSIT, Registrar's Office" />
                        </label>
                        <label className="survey-field">
                            <span>Age · Edad</span>
                            <input type="number" min="1" max="120" value={profile.age} onChange={setProfileField('age')} />
                        </label>
                        <label className="survey-field">
                            <span>Sex · Kasarian</span>
                            <select value={profile.sex} onChange={setProfileField('sex')}>
                                {SEX_OPTIONS.map(o => <option key={o} value={o}>{o || '—'}</option>)}
                            </select>
                        </label>
                        <label className="survey-field">
                            <span>Years using SEN-GEN · Taong paggamit</span>
                            <select value={profile.yearsUsing} onChange={setProfileField('yearsUsing')}>
                                {YEARS_OPTIONS.map(o => <option key={o} value={o}>{o || '—'}</option>)}
                            </select>
                        </label>
                    </div>
                </section>

                {/* Scale legend */}
                <div className="survey-legend">
                    {instrument.scale.map(s => (
                        <span key={s.value}><strong>{s.value}</strong> — {s.en} / {s.fil}</span>
                    ))}
                </div>

                {/* Characteristics */}
                {instrument.characteristics.map((c, ci) => (
                    <section className="survey-section" key={c.code}>
                        <h2>{ci + 1}. {c.nameEn} · {c.nameFil}</h2>
                        <p className="survey-note">{c.hintEn} / {c.hintFil}</p>
                        {c.questions.map(q => (
                            <div className="survey-q" key={q.key}>
                                <div className="survey-q-text">
                                    <span>{q.en}</span>
                                    <span className="survey-q-fil">{q.fil}</span>
                                </div>
                                <div className="survey-scale" role="radiogroup" aria-label={q.en}>
                                    {instrument.scale.map(s => (
                                        <label key={s.value} className={`survey-dot${answers[q.key] === s.value ? ' on' : ''}`}>
                                            <input
                                                type="radio"
                                                name={q.key}
                                                value={s.value}
                                                checked={answers[q.key] === s.value}
                                                onChange={() => setAnswers(a => ({ ...a, [q.key]: s.value }))}
                                            />
                                            <span>{s.value}</span>
                                        </label>
                                    ))}
                                </div>
                            </div>
                        ))}
                    </section>
                ))}

                {/* Open-ended */}
                <section className="survey-section">
                    <label className="survey-field">
                        <span>{instrument.suggestionsEn} · {instrument.suggestionsFil}</span>
                        <textarea rows={3} value={suggestions} onChange={e => setSuggestions(e.target.value)} maxLength={4000} />
                    </label>
                    <label className="survey-field">
                        <span>{instrument.commentsEn} · {instrument.commentsFil}</span>
                        <textarea rows={3} value={furtherComments} onChange={e => setFurtherComments(e.target.value)} maxLength={4000} />
                    </label>
                </section>

                {formError && <div className="alert">{formError}</div>}

                <div className="survey-footer">
                    <span className="survey-note">{answeredCount}/{totalQuestions} answered</span>
                    <button className="btn btn-primary" type="submit" disabled={saving}>
                        {saving && <span className="spinner" aria-hidden="true" />}
                        {saving ? 'Submitting…' : 'Submit evaluation · Ipasa'}
                    </button>
                </div>
            </form>
        </div>
    );
}

export default SurveyPage;
