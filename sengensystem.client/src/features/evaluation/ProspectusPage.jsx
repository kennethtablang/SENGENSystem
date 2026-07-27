import { useEffect, useState } from 'react';
import { listProspectusPrograms, downloadProspectus } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import '../registration/registration.css';
import './evaluation.css';

/* FR-RPT-05: the electronic copy of what a year level takes. Staff pick a program and a year and
   get the prospectus as a PDF — code, title, units, the lecture/laboratory split, and prerequisites,
   term by term. Printing the whole ladder at once is the "give me the program" case. */

const yearLabel = (n) => (['1st year', '2nd year', '3rd year', '4th year'][n - 1] ?? `Year ${n}`);

export default function ProspectusPage() {
    const [programs, setPrograms] = useState(null);
    const [error, setError] = useState('');
    const [busyKey, setBusyKey] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await listProspectusPrograms();
                if (active) setPrograms(data.programs);
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, []);

    async function download(program, yearLevel) {
        const key = `${program.curriculumId}:${yearLevel ?? 'all'}`;
        setBusyKey(key);
        try {
            await downloadProspectus({
                curriculumId: program.curriculumId,
                yearLevel,
                programCode: program.programCode
            });
            notifySuccess(yearLevel
                ? `Downloaded the ${program.programCode} ${yearLabel(yearLevel)} prospectus.`
                : `Downloaded the full ${program.programCode} prospectus.`);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusyKey(null);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Curriculum prospectus</h2>
                    <p className="reg-sub">
                        The subjects a year level takes, as a printable PDF — units, the lecture/laboratory
                        hour split, and prerequisites, term by term. A transferee’s own copy comes with their
                        credited subjects marked; print that from their evaluation.
                    </p>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {programs === null ? (
                <p className="reg-empty">Loading curricula…</p>
            ) : programs.length === 0 ? (
                <p className="reg-empty">
                    No curriculum is set up yet. Add one under Subjects &amp; curriculum.
                </p>
            ) : (
                <div className="pros-grid">
                    {programs.map(program => (
                        <section className="card pros-card" key={program.curriculumId}>
                            <header className="pros-card-head">
                                <div>
                                    <h3>{program.programCode}</h3>
                                    <p>{program.programName}</p>
                                </div>
                                {program.isActive && <span className="chip chip-blue">Active</span>}
                            </header>

                            {program.years.length === 0 ? (
                                <p className="reg-empty">No subjects in this curriculum yet.</p>
                            ) : (
                                <ul className="pros-years">
                                    {program.years.map(year => (
                                        <li key={year.yearLevel}>
                                            <div className="pros-year-main">
                                                <span className="pros-year-label">{year.label}</span>
                                                <span className="pros-year-meta">
                                                    {year.subjectCount} subject{year.subjectCount === 1 ? '' : 's'} · {year.units} units
                                                </span>
                                            </div>
                                            <button
                                                type="button" className="btn btn-sm"
                                                disabled={busyKey !== null}
                                                onClick={() => download(program, year.yearLevel)}
                                            >
                                                {busyKey === `${program.curriculumId}:${year.yearLevel}` ? 'Preparing…' : 'PDF'}
                                            </button>
                                        </li>
                                    ))}
                                </ul>
                            )}

                            <footer className="pros-card-foot">
                                <button
                                    type="button" className="btn btn-primary btn-sm"
                                    disabled={busyKey !== null || program.years.length === 0}
                                    onClick={() => download(program, null)}
                                >
                                    {busyKey === `${program.curriculumId}:all` ? 'Preparing…' : 'Download all year levels'}
                                </button>
                            </footer>
                        </section>
                    ))}
                </div>
            )}
        </div>
    );
}
