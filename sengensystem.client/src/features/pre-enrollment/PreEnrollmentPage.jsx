import { useRef, useState } from 'react';
import { getToken } from '../auth/api';
import { notifySuccess, notifyError } from '../shell/notify';
import { saveBlob } from '../shell/download';
import '../registration/registration.css';

/* FR-PRE-01/03: the Registrar imports prospective student lists from .xlsx. The server runs
   the ETL pipeline (extract → validate → transform → load with duplicate detection) and
   returns a row-by-row report; valid rows load even when others fail. */

const outcomeChip = {
    Loaded: 'chip chip-blue',
    Skipped: 'chip chip-yellow',
    Failed: 'chip chip-muted'
};

function PreEnrollmentPage() {
    const [file, setFile] = useState(null);
    const [busy, setBusy] = useState(false);
    const [report, setReport] = useState(null);
    const [alert, setAlert] = useState(null);
    const inputRef = useRef(null);

    async function upload(e) {
        e.preventDefault();
        if (!file) return;
        setBusy(true);
        setAlert(null);
        setReport(null);
        try {
            const form = new FormData();
            form.append('file', file);
            const response = await fetch('/api/pre-enrollment/import', {
                method: 'POST',
                headers: { Authorization: `Bearer ${getToken()}` },
                body: form
            });
            const payload = await response.json();
            if (!response.ok) throw new Error(payload?.message || 'Import failed.');
            setReport(payload);
            const text = `Imported ${payload.loaded} of ${payload.totalRows} row(s) into ${payload.semesterName} — ` +
                `${payload.skipped} duplicate(s) skipped, ${payload.failed} failed validation.`;
            setAlert({ kind: 'success', text });
            notifySuccess(text);
            setFile(null);
            if (inputRef.current) inputRef.current.value = '';
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function downloadTemplate() {
        setAlert(null);
        try {
            const response = await fetch('/api/pre-enrollment/template', {
                headers: { Authorization: `Bearer ${getToken()}` }
            });
            if (!response.ok) throw new Error('Could not download the template.');
            const blob = await response.blob();
            saveBlob(blob, 'sengen-preenrollment-template.xlsx');
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Pre-enrollment import</h2>
                    <p className="reg-sub">
                        Import prospective student lists from .xlsx. Each valid row becomes a SIS registration
                        with an issued student number and a seeded document checklist; duplicates (by email or
                        name + birthdate) are skipped and errors are reported per row.
                    </p>
                </div>
                <button className="btn" type="button" onClick={downloadTemplate}>
                    Download template
                </button>
            </header>

            {alert && <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>}

            <form className="card" style={{ padding: '1.1rem 1.3rem', marginBottom: '1.3rem' }} onSubmit={upload}>
                <div style={{ display: 'flex', gap: '0.8rem', alignItems: 'center', flexWrap: 'wrap' }}>
                    <input
                        ref={inputRef}
                        type="file"
                        accept=".xlsx"
                        onChange={e => setFile(e.target.files?.[0] ?? null)}
                    />
                    <button className="btn btn-primary" type="submit" disabled={!file || busy}>
                        {busy ? 'Importing…' : 'Import workbook'}
                    </button>
                </div>
            </form>

            {report && (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <th>Row</th>
                                <th>Name</th>
                                <th>Student no.</th>
                                <th>Outcome</th>
                                <th>Details</th>
                            </tr>
                        </thead>
                        <tbody>
                            {report.rows.map(row => (
                                <tr key={row.row}>
                                    <td className="reg-mono">{row.row}</td>
                                    <td><strong>{row.name || '—'}</strong></td>
                                    <td className="reg-mono">{row.studentNumber || '—'}</td>
                                    <td><span className={outcomeChip[row.outcome] || 'chip chip-muted'}>{row.outcome}</span></td>
                                    <td style={{ whiteSpace: 'normal' }}>
                                        {row.errors.length === 0 ? '—' : row.errors.join(' ')}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}

export default PreEnrollmentPage;
