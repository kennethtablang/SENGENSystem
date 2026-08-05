import { useRef, useState } from 'react';
import { getToken } from '../auth/api';
import { notifySuccess, notifyError } from '../shell/notify';
import { saveBlob } from '../shell/download';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
import '../registration/registration.css';

/* FR-PRE-01/03: the Registrar imports prospective student lists from .xlsx. The server runs
   the ETL pipeline (extract → validate → transform → load with duplicate detection) and
   returns a row-by-row report; valid rows load even when others fail. */

const outcomeChip = {
    Loaded: 'chip chip-blue',
    Skipped: 'chip chip-yellow',
    Failed: 'chip chip-muted'
};

/* The import outcome, row by row. A workbook can carry hundreds of rows and what the Registrar
   actually wants is the failures — so it sorts by outcome (errors first) and filters, rather than
   making them scroll a wall of successes to find the three that did not land. */
function ImportReportTable({ rows }) {
    const [search, setSearch] = useState('');
    const table = useTableControls(rows, {
        columns: {
            row: r => r.row,
            name: r => r.name,
            studentNumber: r => r.studentNumber,
            // Anything that is not an outright success sorts to the top.
            outcome: r => (r.errors.length > 0 ? 0 : 1),
            details: r => r.errors.join(' ')
        },
        initialSort: { key: 'row', dir: 'asc' },
        search,
        searchFields: [r => r.name, r => r.studentNumber, r => r.outcome, r => r.errors.join(' ')]
    });

    return (
        <div className="card reg-table-wrap">
            <div className="table-toolbar">
                <TableSearch value={search} onChange={setSearch} placeholder="Filter name, number, or error…" />
            </div>
            {table.total === 0 ? (
                <p className="reg-empty">No imported rows match your filter.</p>
            ) : (
                <>
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Row" sortKey="row" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Outcome" sortKey="outcome" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Details" sortKey="details" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(row => (
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
                    <Pagination {...table} />
                </>
            )}
        </div>
    );
}

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

            {report && <ImportReportTable rows={report.rows} />}
        </div>
    );
}

export default PreEnrollmentPage;
