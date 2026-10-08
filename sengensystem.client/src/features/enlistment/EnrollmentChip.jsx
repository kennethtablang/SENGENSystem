/* F-14: the per-student "enrolled for the term" marker. Derived on the server from the student's
   own plan (every subject due this term holds an approved seat) — never stored, so it cannot
   disagree with the seats it summarises. Renders nothing when there is nothing to say. */
const LABELS = {
    Enrolled: { text: 'Enrolled', cls: 'chip chip-green' },
    Partial: { text: 'Partly enlisted', cls: 'chip chip-yellow' },
    NotStarted: { text: 'Not enlisted', cls: 'chip chip-muted' }
};

export default function EnrollmentChip({ completion }) {
    if (!completion) return null;
    const meta = LABELS[completion.state];
    if (!meta) return null;
    if (completion.state === 'NotStarted' && completion.plannedSubjects === 0) {
        return <span className="chip chip-muted" title="No subjects are due this term">No subjects due</span>;
    }

    const title = completion.state === 'Partial'
        ? `${completion.coveredSubjects}/${completion.plannedSubjects} subjects approved — still to enlist: ${completion.missingCodes.join(', ')}`
        : `${completion.coveredSubjects}/${completion.plannedSubjects} subjects approved`;
    return <span className={meta.cls} title={title}>{meta.text}</span>;
}
