// Sidebar navigation config. Every item lists the roles that can see it
// (FR-AUTH-08: users only reach functions appropriate to their role).
// The School Admin oversees the whole institution and sees EVERY function —
// enforced as a wildcard in visibleGroups(), so items never need to list it.

const ALL = ['Student', 'FacultyMember', 'AdmissionOfficer', 'Registrar', 'AcademicHead'];

export const navGroups = [
    {
        label: 'Overview',
        items: [
            {
                to: '/',
                end: true,
                label: 'Dashboard',
                icon: 'dashboard',
                roles: ALL,
                desc: 'Your semester at a glance.'
            },
            {
                to: '/notifications',
                label: 'Notifications',
                icon: 'bell',
                roles: ALL,
                desc: 'Enrollment reminders, confirmations, and schedule alerts sent to you.'
            }
        ]
    },
    {
        label: 'Enrollment',
        items: [
            {
                to: '/documents',
                label: 'Document requirements',
                icon: 'file',
                roles: ['Student', 'AdmissionOfficer', 'Registrar'],
                desc: 'Form 137, birth certificate, and good moral certificate — submission status per enrollee.'
            },
            {
                to: '/registrations',
                label: 'Registration (SIS)',
                icon: 'idcard',
                roles: ['Registrar'],
                desc: 'Review digital Student Information Sheets, verify requirements, and confirm registrations.'
            },
            {
                to: '/term-activations',
                label: 'Term activations',
                icon: 'check',
                roles: ['AdmissionOfficer'],
                desc: 'Validate returning students’ requests to activate for the current term.'
            },
            {
                to: '/pre-enrollment',
                label: 'Pre-enrollment import',
                icon: 'upload',
                roles: ['Registrar'],
                desc: 'Import prospective student lists from .xlsx files with validation and duplicate detection.'
            },
            {
                to: '/pre-authorization',
                label: 'Pre-authorization',
                icon: 'check',
                roles: ['AdmissionOfficer', 'Registrar'],
                desc: 'Authorize incoming and returning students for online slot selection.'
            }
        ]
    },
    {
        label: 'Enlistment',
        items: [
            {
                to: '/enlistment',
                label: 'Subject enlistment',
                icon: 'listcheck',
                roles: ['Student'],
                desc: 'Browse published sections with live slot counts and reserve your seats.'
            },
            {
                to: '/approvals',
                label: 'Enlistment approvals',
                icon: 'check',
                roles: ['Registrar'],
                desc: 'Review and approve student slot requests (40 per section).'
            }
        ]
    },
    {
        label: 'Scheduling',
        items: [
            {
                to: '/schedule',
                label: 'My schedule',
                icon: 'calendar',
                roles: ['Student', 'FacultyMember'],
                desc: 'Your weekly timetable for the active semester.'
            },
            {
                to: '/scheduling/board',
                label: 'Schedule board',
                icon: 'board',
                roles: ['AcademicHead'],
                desc: 'Build class schedules on a calendar by dragging allocated subjects into time slots.'
            },
            {
                to: '/scheduling/generate',
                label: 'Generate schedule',
                icon: 'bolt',
                roles: ['AcademicHead'],
                desc: 'Run the CSP engine to produce a conflict-free schedule for all sections.'
            },
            {
                to: '/scheduling/review',
                label: 'Review & overrides',
                icon: 'edit',
                roles: ['AcademicHead'],
                desc: 'Inspect the generated schedule and apply manual overrides with conflict detection.'
            },
            {
                to: '/publishing',
                label: 'Schedule publishing',
                icon: 'send',
                roles: ['Registrar'],
                desc: 'Publish finalized schedules to students and faculty by week, day, and class.'
            },
            {
                to: '/faculty-load',
                label: 'Faculty load',
                icon: 'users',
                roles: ['AcademicHead'],
                desc: 'Allocate subjects to faculty per semester, against each member’s load ceiling.'
            }
        ]
    },
    {
        label: 'Academic setup',
        items: [
            {
                to: '/school-years',
                label: 'School years',
                icon: 'calendar',
                roles: [],
                desc: 'Academic school years, key dates, and the active year.'
            },
            {
                to: '/semesters',
                label: 'Semesters',
                icon: 'clock',
                roles: [],
                desc: 'Semesters within each school year and the active term.'
            },
            {
                to: '/buildings',
                label: 'Buildings',
                icon: 'building',
                roles: [],
                desc: 'Campus buildings that group teaching rooms.'
            },
            {
                to: '/rooms',
                label: 'Rooms',
                icon: 'door',
                roles: [],
                desc: 'Teaching rooms, capacities, and the laboratory flag.'
            },
            {
                to: '/class-sections',
                label: 'Class sections',
                icon: 'users',
                roles: ['AcademicHead'],
                desc: 'Student class blocks by course, year level, and section.'
            },
            {
                to: '/subjects',
                label: 'Subjects & curriculum',
                icon: 'book',
                roles: ['AcademicHead'],
                desc: 'Subject records, units, prerequisites, and program curricula.'
            }
        ]
    },
    {
        label: 'Administration',
        items: [
            {
                to: '/reports',
                label: 'Reports & analytics',
                icon: 'chart',
                roles: ['Registrar', 'AcademicHead'],
                desc: 'Live semester-aware enrollment, enlistment, room, and load statistics.'
            },
            {
                to: '/reports/faculty-load',
                label: 'Faculty load reports',
                icon: 'file',
                roles: ['Registrar', 'AcademicHead'],
                desc: 'Per-faculty academic load workbooks, consolidated loading, and grid schedules.'
            },
            {
                to: '/users',
                label: 'User management',
                icon: 'users',
                roles: [],
                desc: 'Create, update, and deactivate accounts across all six roles.'
            },
            {
                to: '/parameters',
                label: 'System parameters',
                icon: 'gear',
                roles: [],
                desc: 'Unit-load limits, allowable time slots, and section capacities.'
            },
            {
                to: '/audit',
                label: 'Audit trail',
                icon: 'shield',
                roles: [],
                desc: 'Accountability log of registrations, approvals, overrides, and account changes.'
            }
        ]
    }
];

// Minor functions pinned to the sidebar footer.
export const navMinor = [
    {
        to: '/settings',
        label: 'Settings',
        icon: 'sliders',
        roles: ALL,
        desc: 'Application preferences.'
    },
    {
        to: '/profile',
        label: 'Profile settings',
        icon: 'user',
        roles: ALL,
        desc: 'Your name, email, and password.'
    },
    {
        to: '/help',
        label: 'Help & about',
        icon: 'help',
        roles: ALL,
        desc: 'How SEN-GEN works, from enrollment to scheduling.'
    }
];

export function canSee(item, role) {
    return role === 'SchoolAdmin' || item.roles.includes(role);
}

export function visibleGroups(role) {
    return navGroups
        .map(group => ({ ...group, items: group.items.filter(i => canSee(i, role)) }))
        .filter(group => group.items.length > 0);
}

export function visibleMinor(role) {
    return navMinor.filter(i => canSee(i, role));
}

/* Everything the given role may open — powers the top-bar search. */
export function searchableItems(role) {
    return allNavItems().filter(i => canSee(i, role));
}

export function allNavItems() {
    return [...navGroups.flatMap(g => g.items), ...navMinor];
}

// Single-path stroke icons (24×24 viewBox).
export const iconPaths = {
    dashboard: 'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',
    bell: 'M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0',
    file: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6',
    idcard: 'M2 5h20v14H2zM6 9.5h5M6 13h4M14 9.5h4M14 13h4',
    upload: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 9l5-5 5 5M12 4v12',
    check: 'M22 11.1V12a10 10 0 1 1-5.9-9.1M22 4 12 14l-3-3',
    listcheck: 'M10 6h11M10 12h11M10 18h11M3 6l1.5 1.5L7 5M3 12l1.5 1.5L7 11M3 18l1.5 1.5L7 17',
    calendar: 'M3 5h18v16H3zM3 9.5h18M8 3v4M16 3v4',
    board: 'M3 5h18v14H3zM3 9.5h18M9 9.5V19M15 9.5V19',
    bolt: 'M13 2 3 14h7l-1 8 10-12h-7z',
    edit: 'M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z',
    send: 'M22 2 11 13M22 2l-7 20-4-9-9-4z',
    users: 'M17 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9.5 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8M22 21v-2a4 4 0 0 0-3-3.87M15.5 3.1a4 4 0 0 1 0 7.8',
    book: 'M4 19.5A2.5 2.5 0 0 1 6.5 17H20V2H6.5A2.5 2.5 0 0 0 4 4.5zM4 19.5A2.5 2.5 0 0 0 6.5 22H20v-2.5',
    building: 'M3 21h18M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16M9 7h1M14 7h1M9 11h1M14 11h1M9 15h1M14 15h1',
    door: 'M6 21V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v17M3 21h18M14 12h.01',
    clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20M12 6v6l4 2',
    chart: 'M3 3v18h18M8 17v-6M13 17V7M18 17v-4',
    gear: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.9 2.9l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.9-2.9l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.9-2.9l.1.1a1.7 1.7 0 0 0 1.9.3h.1a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.9 2.9l-.1.1a1.7 1.7 0 0 0-.3 1.9v.1a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1',
    shield: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10',
    sliders: 'M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6',
    user: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 3a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9',
    chevrons: 'M11 17l-5-5 5-5M18 17l-5-5 5-5',
    help: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01',
    signout: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9'
};
