import { Icon } from '../shell/AppLayout';
import { version } from '../../../package.json';
import logo from '../../assets/SENGENlogo.png';
import './help.css';

/* The enrollment-to-scheduling journey, in order. */
const steps = [
    {
        icon: 'idcard',
        title: 'Register',
        text: 'New students and transferees fill out the digital Student Information Sheet — no account needed — and receive an official student number. Returning students simply request term activation with their student number.'
    },
    {
        icon: 'file',
        title: 'Submit documents',
        text: 'Form 137, PSA birth certificate, and certificate of good moral character. The Admission Office tracks each item on your checklist and sends reminders for anything missing.'
    },
    {
        icon: 'check',
        title: 'Get pre-authorized',
        text: 'Once your registration is confirmed and your documents are complete, staff authorize you for online slot selection.'
    },
    {
        icon: 'listcheck',
        title: 'Enlist in subjects',
        text: 'Browse published sections with live seat counts and request the slots you want. The Registrar approves requests while seats last — approvals are first-come, first-served and conflict-checked.'
    },
    {
        icon: 'bolt',
        title: 'Schedules are generated',
        text: 'The Academic Head allocates subjects to faculty, then the scheduling engine builds a timetable with zero room, faculty, or section conflicts — honoring faculty load limits and time preferences. Manual fine-tuning happens on a drag-and-drop board.'
    },
    {
        icon: 'send',
        title: 'Schedules are published',
        text: 'The Registrar publishes the finalized timetable. Students and faculty are notified by email the moment it goes live.'
    },
    {
        icon: 'calendar',
        title: 'See your week',
        text: 'Your personal weekly timetable appears under My schedule — every approved subject with its room, time, and instructor.'
    }
];

const roles = [
    {
        name: 'Student',
        text: 'Registers, tracks document requirements, enlists in published sections, and views their weekly schedule.'
    },
    {
        name: 'Faculty Member',
        text: 'Sets time preferences, and views their assigned teaching load and weekly schedule once published.'
    },
    {
        name: 'Admission Officer',
        text: 'Validates term activations, manages document checklists, and pre-authorizes students for enlistment.'
    },
    {
        name: 'Registrar',
        text: 'Reviews registrations, imports pre-enrollment lists, approves slot requests, publishes schedules, and runs reports.'
    },
    {
        name: 'Academic Head',
        text: 'Maintains curricula and class sections, allocates faculty load, and generates and refines the class schedule.'
    },
    {
        name: 'School Admin',
        text: 'Oversees everything: school years, buildings and rooms, user accounts, system parameters, and the audit trail.'
    }
];

const faqs = [
    {
        q: 'I’m a new student — do I need an account to register?',
        a: 'No. The digital Student Information Sheet is open to everyone; submitting it issues your student number and starts your document checklist. If you later create an account, you can link it to your registration using your student number and date of birth to follow your progress here.'
    },
    {
        q: 'I studied here before — how do I enroll for the new term?',
        a: 'Use the term activation request with your student number and last name. The Admission Office validates it and you’ll get a confirmation email once you’re active for the semester.'
    },
    {
        q: 'Why can’t I request a slot in a section?',
        a: 'Three common reasons: you’re not yet pre-authorized (registration or documents incomplete), the section is full, or the section overlaps a slot you already hold. The enlistment page tells you which one applies.'
    },
    {
        q: 'When will my class schedule appear?',
        a: 'Under My schedule, as soon as the Registrar publishes the semester’s timetable and your slot requests are approved. Until then, faculty may see a draft preview of their own load.'
    },
    {
        q: 'How are schedules guaranteed conflict-free?',
        a: 'Generation is a constraint-satisfaction search: no room, instructor, or class block can ever be double-booked, room capacity and laboratory requirements are enforced, and faculty load ceilings are respected. Preferences like faculty time windows are optimized on top of those hard guarantees.'
    },
    {
        q: 'Why don’t I see a module someone else has?',
        a: 'Navigation is role-based — you only see the functions your role is allowed to use. If you believe you’re missing something, ask the School Admin to review your account’s role.'
    },
    {
        q: 'I forgot my password. What do I do?',
        a: 'Ask the School Admin (or your department’s office) to reset it from User management. You can change it yourself afterwards under Profile settings.'
    }
];

function HelpPage() {
    return (
        <div className="help-page">
            <section className="card help-hero rise">
                <img src={logo} alt="" className="help-logo" />
                <div className="help-hero-main">
                    <h2>
                        SEN-GEN
                        <span className="chip chip-yellow">v{version}</span>
                    </h2>
                    <p className="help-tagline">
                        Automated class scheduling and online enrollment for STI College Alaminos —
                        from your first registration form to your finished weekly timetable.
                    </p>
                </div>
            </section>

            <section className="help-section rise rise-1">
                <h3>How enrollment works</h3>
                <p className="help-section-sub">The whole journey, from sign-up to schedule.</p>
                <ol className="help-steps">
                    {steps.map((step, i) => (
                        <li className="card help-step" key={step.title}>
                            <span className="help-step-num" aria-hidden="true">{i + 1}</span>
                            <span className="help-step-icon">
                                <Icon name={step.icon} />
                            </span>
                            <div>
                                <h4>{step.title}</h4>
                                <p>{step.text}</p>
                            </div>
                        </li>
                    ))}
                </ol>
            </section>

            <section className="help-section rise rise-2">
                <h3>Who does what</h3>
                <p className="help-section-sub">Every account has one role; your sidebar shows only your functions.</p>
                <div className="help-roles">
                    {roles.map(role => (
                        <div className="card help-role" key={role.name}>
                            <span className="chip chip-blue">{role.name}</span>
                            <p>{role.text}</p>
                        </div>
                    ))}
                </div>
            </section>

            <section className="help-section rise rise-3">
                <h3>Frequently asked questions</h3>
                <div className="card help-faq">
                    {faqs.map(faq => (
                        <details key={faq.q}>
                            <summary>
                                {faq.q}
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                                    strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                                    <path d="m6 9 6 6 6-6" />
                                </svg>
                            </summary>
                            <p>{faq.a}</p>
                        </details>
                    ))}
                </div>
            </section>

            <section className="card help-about rise rise-4">
                <h3>About this system</h3>
                <p>
                    SEN-GEN was built as a Master&rsquo;s capstone project for STI College Alaminos to replace
                    manual, spreadsheet-based scheduling and paper enrollment with a single conflict-free,
                    auditable workflow. For questions about your enrollment, visit the Registrar&rsquo;s or
                    Admission Office on campus.
                </p>
                <div className="help-tech" aria-label="Technology">
                    <span className="chip chip-muted">React</span>
                    <span className="chip chip-muted">.NET minimal APIs</span>
                    <span className="chip chip-muted">SQL Server</span>
                    <span className="chip chip-muted">FullCalendar</span>
                    <span className="chip chip-muted">CSP scheduling engine</span>
                </div>
            </section>
        </div>
    );
}

export default HelpPage;
