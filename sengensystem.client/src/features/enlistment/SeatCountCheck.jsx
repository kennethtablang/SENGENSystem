import { useEffect, useState } from 'react';
import { seatCountMismatches, reconcileSeatCount } from './api';
import { notifySuccess, notifyError } from '../shell/notify';

/* F-08 follow-up: a section's EnrolledCount is a stored counter, and counts that drifted before
   seats could be released are still wrong. This lists every active-term section whose counter
   disagrees with its live approved requests, and lets the Registrar correct one on purpose — the
   server never rewrites it silently. Renders nothing when every count agrees. */
export default function SeatCountCheck({ refreshKey }) {
    const [data, setData] = useState(null);
    const [busy, setBusy] = useState(null);
    const [reload, setReload] = useState(0);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const result = await seatCountMismatches();
                if (active) setData(result);
            } catch {
                // Advisory panel: a failure here must not take the approvals queue down with it.
            }
        })();
        return () => { active = false; };
    }, [refreshKey, reload]);

    if (!data || data.count === 0) return null;

    async function reconcile(section) {
        setBusy(section.sectionId);
        try {
            const result = await reconcileSeatCount(section.sectionId);
            notifySuccess(result.changed
                ? `${section.sectionCode}: seat count corrected ${result.before} → ${result.enrolledCount}.`
                : `${section.sectionCode} already agrees.`);
            setReload(r => r + 1);
        } catch (ex) {
            notifyError(ex.message);
        } finally { setBusy(null); }
    }

    return (
        <div className="alert" role="status">
            <p>
                <strong>{data.count} section{data.count === 1 ? '' : 's'}</strong> show a seat count that
                disagrees with {data.count === 1 ? 'its' : 'their'} approved students. Capacity checks and
                fill figures read the stored count, so review and correct {data.count === 1 ? 'it' : 'them'}:
            </p>
            <ul className="enl-seatcheck">
                {data.sections.map(s => (
                    <li key={s.sectionId}>
                        <span>
                            <strong>{s.sectionCode}</strong> ({s.subjectCode}) — stored {s.enrolledCount},
                            approved {s.approvedRequests}, capacity {s.capacity}
                        </span>
                        <button type="button" className="btn btn-sm" disabled={busy === s.sectionId}
                            onClick={() => reconcile(s)}>
                            {busy === s.sectionId ? 'Correcting…' : `Set to ${s.approvedRequests}`}
                        </button>
                    </li>
                ))}
            </ul>
        </div>
    );
}
