// Shared option lists + labels for the SIS registration UI. Values MUST match the server enum
// member names (they are parsed back on the server).

export const programOptions = [
    { value: 'ITP', label: 'ITP — Information Technology Program' },
    { value: 'HRS', label: 'HRS — Hospitality and Restaurant Services' },
    { value: 'HRA', label: 'HRA — Hotel and Restaurant Administration' }
];

export const studentTypeOptions = [
    { value: 'NewStudent', label: 'New student' },
    { value: 'Transferee', label: 'Transferee' }
];

export const civilStatusOptions = [
    { value: 'Single', label: 'Single' },
    { value: 'Married', label: 'Married' },
    { value: 'Widowed', label: 'Widowed' },
    { value: 'Separated', label: 'Separated' }
];

export const genderOptions = [
    { value: 'Male', label: 'Male' },
    { value: 'Female', label: 'Female' }
];

export const lastSchoolLevelOptions = [
    { value: 'HighSchool', label: 'High school' },
    { value: 'JuniorHighSchool', label: 'Junior high school' },
    { value: 'SeniorHighSchool', label: 'Senior high school' },
    { value: 'AlsAePept', label: 'ALS A&E / PEPT' },
    { value: 'College', label: 'College' }
];

export const yearGradeOptions = [
    { value: 'Grade12', label: 'Grade 12' },
    { value: 'FirstYear', label: '1st year' },
    { value: 'SecondYear', label: '2nd year' },
    { value: 'ThirdYear', label: '3rd year' },
    { value: 'FourthYear', label: '4th year' },
    { value: 'Other', label: 'Other' }
];

export const termOptions = [
    { value: 'First', label: '1st' },
    { value: 'Second', label: '2nd' },
    { value: 'Third', label: '3rd' },
    { value: 'Other', label: 'Other' }
];

export const guardianRelationshipOptions = [
    { value: 'Father', label: 'Father' },
    { value: 'Mother', label: 'Mother' },
    { value: 'Other', label: 'Other' }
];

// Admission-requirement documents (order matches the SIS).
export const documentTypes = [
    { value: 'Form138_SF9', label: 'Form 138 / SF9-SHS (Report Card)' },
    { value: 'Form137_SF10', label: 'Form 137 / SF10-SHS (Permanent Record)' },
    { value: 'GoodMoral', label: 'Good Moral' },
    { value: 'PsaBirthCertificate', label: 'PSA Birth Certificate' },
    { value: 'OfficialTranscript', label: 'Official Transcript of Records' },
    { value: 'HonorableDismissal', label: 'Certificate of Transfer (Honorable Dismissal)' },
    { value: 'HepaA', label: 'Hepatitis A' },
    { value: 'HepaB', label: 'Hepatitis B' },
    { value: 'Xray', label: 'X-ray' }
];

// Every submission state a checklist line can carry. Which of them a *particular* paper offers is
// the server's call (it depends on the requirement), and arrives on each checklist row as
// `statuses` — the transcript takes a certificate of grades where other papers take a photocopy.
export const documentStatusOptions = [
    { value: 'NotSubmitted', label: 'Not submitted' },
    { value: 'Submitted', label: 'Submitted (original)' },
    { value: 'XeroxCopy', label: 'Xerox copy' },
    { value: 'CertificateOfGrades', label: 'Certificate of grades' }
];

const documentStatusLabelMap = Object.fromEntries(documentStatusOptions.map(o => [o.value, o.label]));
export const documentStatusLabel = (value) => documentStatusLabelMap[value] ?? humanize(value);

/** The options one checklist line may be set to, honouring the server's per-requirement list. */
export function statusOptionsFor(statuses) {
    if (!statuses?.length) return documentStatusOptions.filter(o => o.value !== 'CertificateOfGrades');
    return statuses.map(value => ({ value, label: documentStatusLabel(value) }));
}

const documentLabelMap = Object.fromEntries(documentTypes.map(d => [d.value, d.label]));
export const documentLabel = (value) => documentLabelMap[value] ?? value;

// "NewStudent" -> "New student"
export function humanize(pascal) {
    if (!pascal) return '';
    const spaced = pascal.replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

// Timestamps arrive as UTC ISO strings; SEN-GEN always reads in Philippine time (UTC+8).
export function formatPHT(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString('en-PH', {
        timeZone: 'Asia/Manila',
        year: 'numeric', month: 'short', day: '2-digit',
        hour: '2-digit', minute: '2-digit', hour12: true, timeZoneName: 'short'
    });
}
