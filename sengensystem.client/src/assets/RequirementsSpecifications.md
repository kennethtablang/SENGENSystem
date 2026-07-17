# SEN-GEN — Requirements Specifications

**SEN-GEN: Student Enrollment and Constraint Satisfaction Problem–Based Generative Class Scheduling Engine for STI Alaminos**

| | |
|---|---|
| **Source document** | `SEN-GEN_Revised_Enrollment_v22.docx` (June 2026) |
| **Researcher / Developer** | Kenneth Rey Rallustian Tablang |
| **Methodology** | Agile — Feature-Driven Development (FDD) |
| **Code architecture** | Vertical Slice Architecture (VSA) — one slice per FDD feature |
| **Evaluation standard** | ISO/IEC 25010:2023 |
| **Status** | ✅ Complete — reviewed for completeness and internal consistency |

---

## 1. Purpose and Scope

SEN-GEN is a web-based platform for STI Alaminos that digitizes the first three of the four enrollment stages — **(1) document submission, (2) registration (Student Information Sheet), (3) subject enlistment** — and automatically generates conflict-free class schedules using a **Constraint Satisfaction Problem (CSP)** engine. Stage 4 (**tuition payment**) is explicitly out of scope and remains with the institution's existing cashiering process.

The system replaces paper SIS forms, spreadsheet document checklists, manual slot allocation, and handcrafted schedules with a single, role-differentiated platform where students can browse published schedules, check real-time slot availability, and enlist in their required subjects from any internet-connected device.

### 1.1 Objectives (from the study)

1. Identify the current procedures in student enrollment and class schedule management at STI Alaminos.
2. Describe the features of the proposed Student Enrollment with Generative Scheduling System.
3. Test the usability of the proposed system using the ISO 25010 software quality standard.

---

## 2. User Roles (RBAC)

The system enforces **Role-Based Access Control** with six roles. Every user interacts only with the functions and data appropriate to their institutional responsibility.

| # | Role | Responsibilities in SEN-GEN |
|---|------|------------------------------|
| 1 | **School Admin** | Institutional system configuration, system parameters, user account management, full analytics visibility |
| 2 | **Academic Head** | Schedule generation, program-level schedule review, faculty load monitoring and compliance, manual schedule overrides |
| 3 | **Registrar** | Document submission verification, registration data management, ETL-based pre-enrollment import, enlistment (slot) approvals, schedule publishing, reporting |
| 4 | **Admission Officer** | Pre-authorization of incoming/returning students, document requirements checklist management for new enrollees |
| 5 | **Faculty Member** | View assigned teaching schedules and section enrollment counts, receive automated notifications |
| 6 | **Student** | Submit enrollment requirements, complete digital SIS registration, browse published schedules, check real-time slot availability, perform subject enlistment |

---

## 3. Functional Requirements

Requirement IDs are grouped by module. Each module maps to one FDD feature set and one (or more) vertical slice(s) in the codebase.

**Implementation status legend:** ✅ Done · 🟡 Partial · ⬜ Planned. Status reflects the code actually present in this repository, not intent.

### FR-AUTH — Authentication & User Management

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-AUTH-01 | The system shall allow students to register an account (self-service) with name, email address, and password. | ✅ |
| FR-AUTH-02 | Registration shall require explicit **terms-and-conditions acknowledgment**, and the acknowledgment timestamp shall be persisted (addresses documented apply.sti.edu gap). | ✅ |
| FR-AUTH-03 | The system shall **enforce proper name capitalization** on registration input (addresses documented apply.sti.edu gap). | ✅ |
| FR-AUTH-04 | The system shall **validate all registration inputs** (required fields, email format, password strength) and present clear validation prompts for incomplete/invalid entries (addresses documented apply.sti.edu gap). | ✅ |
| FR-AUTH-05 | The system shall authenticate users via email + password login and issue a session token (JWT) carrying the user's role. | ✅ |
| FR-AUTH-06 | Passwords shall be stored only as salted cryptographic hashes — never in plain text (RA 10173 Data Privacy Act). | ✅ |
| FR-AUTH-07 | The School Admin shall be able to create, update, deactivate, and assign roles to user accounts for all six roles. | ✅ |
| FR-AUTH-08 | Every API endpoint shall be guarded by role-based authorization middleware; users must never access functions or data outside their role. | ✅ |
| FR-AUTH-09 | Duplicate account registration (same email) shall be rejected with a clear message. | ✅ |

*Built: `Features/Auth` (Register, Login, Me) + `Features/Profile` (UpdateProfile, ChangePassword); client `features/auth`, `features/profile`. FR-AUTH-08 complete: every slice now exists and every endpoint carries JWT bearer authentication plus a `RequireRole` policy (or an intentional `AllowAnonymous` for the public SIS/login flows); cross-role access attempts were verified to return 403 across the documents, pre-authorization, enlistment, approvals, dashboard, reports, and preferences endpoints. FR-AUTH-07 user management built: `Features/UserManagement` (School-Admin-only account CRUD — create/update/deactivate/reactivate/reset-password across all six roles, with guards against deactivating the last active School Admin) + client `features/users` at `/users`.*

### FR-DOC — Document Submission & Requirements Checklist

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-DOC-01 | The system shall maintain a **digital requirements checklist** per new enrollee covering pertinent papers: **Form 137, birth certificate, good moral certificate** (extensible list). | ✅ |
| FR-DOC-02 | The Admission Officer shall be able to record, verify, and update the submission status of each required document per student. | ✅ |
| FR-DOC-03 | The system shall display each enrollee's completion status (complete / incomplete, per-document state) as an auditable record. | ✅ |
| FR-DOC-04 | The system shall report document submission completion rates on the administrative dashboard. | ✅ |
| FR-DOC-05 | The system shall send automated **document submission reminder** emails for incomplete checklists. | ✅ |

*Built: the checklist (`Domain/RegistrationDocument`, 9 `DocumentType`s) is auto-seeded per SIS registration; `Features/Documents/Checklist` gives the **Admission Officer** (plus Registrar/School Admin) a dedicated board (`/documents`, `GET/PUT /api/documents`) — per-enrollee expandable checklist with per-paper status dropdowns, complete/incomplete filter, and audited updates (`DocumentChecklistUpdated`, FR-DOC-02/03). `Features/Documents/Reminders` (`POST /api/documents/reminders`) emails exactly the missing papers to every incomplete checklist, or one enrollee (`DocumentEmails`, FR-DOC-05). Students see their own checklist at `/documents` via the account link (FR-ENL-05). The Registrar's Registration screen still updates statuses too. The dashboard completion-rate report (FR-DOC-04) is live on the administrative dashboard and in the document-completion report.*

### FR-SIS — Digital Student Information Sheet (Registration)

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-SIS-01 | The system shall provide a **fully digital SIS registration module** replacing the paper SIS form, with a structured data-entry workflow for personal and academic details. | ✅ |
| FR-SIS-02 | SIS submission shall require terms-and-conditions acknowledgment with a captured timestamp. | ✅ |
| FR-SIS-03 | SIS fields shall enforce formatting rules (proper name capitalization, required-field validation, completeness validation) before acceptance. | ✅ |
| FR-SIS-04 | The Registrar shall manage (view, correct, confirm) registration data captured through the SIS module. | ✅ |
| FR-SIS-05 | Registration confirmation shall trigger an automated email notification to the student. | ✅ |

*Built: `Features/Registration` — a public, account-less digital SIS. New students/transferees self-submit the structured SIS form at `/register-sis` (anonymous `POST /api/registration`) → issued a student number, document checklist seeded, and a confirmation email sent (FR-SIS-05); T&C acknowledgment is captured with a timestamp (`StudentRegistration.TermsAcceptedAtUtc`, FR-SIS-02). Inputs enforce proper-case names and required-field/completeness validation (FR-SIS-03). The Registrar views, corrects, and confirms records at `/registrations` (`Features/Registration/Manage`, FR-SIS-04). Returning students self-serve term activation (`/term-activation`), validated by the Admission Officer. Client `features/registration`. (`Features/Profile` self-service account editing remains separate account maintenance.)*

### FR-PRE — Pre-Enrollment (ETL Import)

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-PRE-01 | The Registrar shall be able to **import prospective student lists from `.xlsx` files** via an ETL pipeline: **Extract** from .xlsx → **Validate** field completeness and format consistency → **Transform** to target schema → **Load** with duplicate detection. | ✅ |
| FR-PRE-02 | Imported students shall be **pre-authorized** for online subject slot selection only after completing document submission and SIS registration. | ✅ |
| FR-PRE-03 | Import errors (missing fields, format issues, duplicates) shall be reported row-by-row without aborting valid rows. | ✅ |
| FR-PRE-04 | The Admission Officer shall be able to pre-authorize incoming and returning students. | ✅ |

*Built. **Pre-authorization**: `Features/PreEnrollment/PreAuthorize` (`/api/pre-authorization`, Admission Officer + Registrar/School Admin) — clears students at `/pre-authorization`; the server enforces the gate (Registrar-**confirmed** SIS **and** complete document checklist, else 400 with the exact blockers), grants are audited (`StudentPreAuthorized`), idempotent, and revocable (FR-PRE-02/04). The **identity link**: `StudentRegistration.UserId` (unique, nullable; migration `AddStudentLinkAndPreAuthorization`) — students explicitly claim their record with **three matching facts**: student number (delivered only to the SIS email), account email equal to the SIS email, and date of birth (`Features/Registration/LinkAccount`, audited `StudentAccountLinked`). There is deliberately **no auto-link at sign-up** — self-service registration never proves mailbox ownership, so linking on the email string alone would enable pre-registration account takeover (RA 10173 exposure); a full email-verification flow is the recommended future hardening. **ETL import**: `Features/PreEnrollment/Import` (`POST /api/pre-enrollment/import`, Registrar; ClosedXML) — Extract from .xlsx → Validate per row (required fields, email/date formats, enum values) → Transform (proper-cased names, issued student numbers, seeded document checklists) → Load with duplicate detection (by email and by name+birthdate, both in-file and against the DB); the row-by-row report loads valid rows even when others fail (FR-PRE-03, verified with a mixed workbook and an idempotent re-import), audited `PreEnrollmentImported`. Client `/pre-enrollment` with a downloadable .xlsx template (`GET /api/pre-enrollment/template`). Imported students flow through the same confirm → checklist → pre-authorization gate as everyone else.*

### FR-SCHED — CSP-Based Generative Scheduling Engine

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-SCHED-01 | The system shall automatically generate **conflict-free class schedules** by modeling sections, rooms, time slots, and faculty as CSP variables assigned values that satisfy all hard constraints. | ✅ |
| FR-SCHED-02 | **Hard constraints** (must never be violated): no room double-booking; no faculty double-assignment; no time-slot overlap within a student section; room capacity respected; faculty academic load limits respected. | ✅ |
| FR-SCHED-03 | **Soft constraints** (optimized, not mandatory): faculty time-slot preferences; minimized idle periods between consecutive classes; **balanced faculty load distribution consistent with STI's institutional loading guidelines**. | ✅ |
| FR-SCHED-04 | The engine shall be **curriculum-aware**: section assignments must respect prerequisite and co-requisite structures per program so that fixed-curriculum cohorts have feasible schedules. | ✅ |
| FR-SCHED-05 | Engine inputs: subject records, section configurations, faculty profiles and availability, room records (capacity/attributes), academic calendar, and system parameters (unit-load limits, allowable time slots, section capacities). | ✅ |
| FR-SCHED-06 | The Academic Head shall trigger schedule generation and review generated schedules before publishing. | ✅ |
| FR-SCHED-07 | Schedule generation shall complete within operationally practical time for STI Alaminos-scale datasets (incremental constraint evaluation and early-termination heuristics; target well under 30 seconds for typical datasets). | ✅ |
| FR-SCHED-08 | The engine is **deterministic, rule-based AI** — no machine learning / predictive models (explicit design exclusion). | ✅ |

*Built: `Features/Scheduling` — `Engine/CspScheduler` (backtracking search, most-constrained-variable ordering, `MaxSteps` early-termination guard), `GenerateSchedule` (AcademicHead-only) and `GetSchedule` endpoints; domain entities Subject/Section/Room/TimeSlot/FacultyProfile/ScheduleAssignment + migration. Client `features/scheduling` — Generate page (trigger + placement summary + result table) and Review page (cohort-grouped, draft/published status), both Academic-Head routes. Hard constraints enforced in `SearchState.IsConsistent`; soft scoring in `SearchState.SoftScore`. FR-SCHED-03 complete: `SearchState.SoftScore` balances load, minimizes cohort idle-gaps, and consumes **faculty time-slot preferences** (`Domain/FacultyTimePreference`, migration `AddFacultyTimePreferences`; Academic Head records windows via `Features/FacultyLoad/Preferences` and the Preferences modal on `/faculty-load`) — verified: a member's in-preferred-window ratio rose from 0.33 to 1.0 after setting windows, with zero hard-constraint violations. FR-SCHED-04 complete: generation validates the semester's offerings against `SubjectPrerequisite` edges before solving — a prerequisite placed later in the curriculum than its dependent fails with the exact sequencing reason (422), and a same-year/term prerequisite is treated as a **co-requisite** that every cohort must be offered alongside the dependent.*

### FR-FAC — Faculty Assignment & Load Management

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-FAC-01 | The system shall support assignment of faculty members to subject loads. | ✅ |
| FR-FAC-02 | The system shall provide **manual class scheduling override** capability (adjust algorithmically generated assignments) while maintaining automatic conflict detection on every override. | ✅ |
| FR-FAC-03 | Faculty load shall be validated against applicable regulatory/institutional limits at assignment time. | ✅ |
| FR-FAC-04 | The system shall continuously **monitor faculty load distribution** so the Academic Head can verify balance without manual cross-referencing, and shall surface imbalances/overloading. | ✅ |
| FR-FAC-05 | Faculty members shall have a digital interface to view their finalized assigned schedules and section enrollment counts. | ✅ |

*Built: `Features/FacultyLoad` — the Academic Head allocates subject loads to faculty members per semester through the **Faculty Load Management** screen and its **Assign Load** modal. Assignment is per **class section** (student block): a row is a subject taught to a specific block (e.g. CS301 → BSCS 3-A), a (subject, class-section) pair is exclusive to one faculty member (an already-assigned row is shown with its holder and muted), and the running unit total is validated against each member's `MaxLoadUnits` ceiling at assignment time (FR-FAC-03). Class sections (course · year · section, created per semester) are managed under **Academic setup** (`Features/AcademicSetup/ClassSections`). The CSP engine additionally assigns faculty to sections automatically during generation (respecting `MaxLoadUnits`, enforced in `SearchState.IsConsistent`). FR-FAC-04 ✅ — the load-management list surfaces each member's assigned units against their ceiling with a load bar and over-ceiling indicator, and the administrative dashboard adds the distribution analysis: per-member load bars with mean-units context and Balanced / AboveAverage / BelowAverage / Overloaded / Unassigned flags, plus the exportable faculty-load report.*

*FR-FAC-02 built: `Features/Scheduling/Board` — the **Schedule Board** (`/scheduling/board`, Academic Head / School Admin) is a calendar-based, drag-and-drop timetable builder (FullCalendar). Faculty-allocated subjects are dragged from an "Assigned Subjects" pool onto a weekly grid; every placement, move, and resize runs live conflict detection (no room double-booking, no faculty double-assignment, no same-cohort overlap → 409 with a human-readable reason). Placements persist as `ScheduleAssignment`s (`IsManualOverride`), so they share the review/publish pipeline. A right-hand **Weekly Hours Tracker** shows plotted vs. required hours per subject×faculty×section (required from the new `Subject.Hours` field), and calendar blocks are colour-coded per subject. FR-FAC-05 built: `Features/Scheduling/MySchedule` — a Faculty Member opens **My schedule** (`/schedule`) to view their own read-only weekly timetable (calendar + day-by-day list with per-section seat counts); students get a forward-compatible empty state until enlistment lands.*

### FR-ENL — Student Subject Enlistment

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-ENL-01 | Students shall browse **published** class schedules showing subjects, sections, times, rooms, and faculty. | ✅ |
| FR-ENL-02 | The interface shall display **real-time slot availability** per section. | ✅ |
| FR-ENL-03 | Each class section shall be capped at a **maximum of 40 slots**, enforced at the system/database level at the moment of enlistment — requests beyond capacity are automatically rejected. | ✅ |
| FR-ENL-04 | Slot selection shall be routed through an **approval workflow** (student requests a seat → Registrar approves), with slot-approval email confirmation. | ✅ |
| FR-ENL-05 | Only **pre-authorized, registered** students (document submission + SIS registration complete) may enlist. | ✅ |
| FR-ENL-06 | Enlistment shall be available online 24/7 from any internet-connected device during the enlistment window — no in-person visit required. | ✅ |
| FR-ENL-07 | The system shall prevent a student from enlisting in sections with overlapping time slots. | ✅ |

*Built: `Features/Enlistment` + `Domain/SlotRequest` (migration `AddEnlistment`). Students browse published-only sections with per-section seat counts and meetings at `/enlistment` (`Browse`, FR-ENL-01/02) and request seats (`RequestSlot`) — refused server-side when not linked + pre-authorized + confirmed (FR-ENL-05, via `EnlistmentEligibility`), when a live request for the section or subject already exists, when the section is full, or when times overlap the student's other requested/approved sections (FR-ENL-07, verified with a forced-overlap test). Requests route through the Registrar's queue at `/approvals` (`Approvals`, FR-ENL-04): approving consumes a seat under **optimistic concurrency** (`Section.RowVersion`) with a **database CHECK constraint** (`EnrolledCount <= Capacity`, `Capacity ≤ 40`) as the backstop — a two-thread race for the last seat was tested and exactly one approval wins (FR-ENL-03); decisions are emailed (`EnlistmentEmails`) and audited (`SlotRequested/SlotApproved/SlotRejected`). Students can cancel pending requests (`MyEnlistment`); approved classes appear on **My schedule** with live seat counts. FR-ENL-06 holds by design — the platform is a web application; 24/7 availability is a deployment property.*

### FR-PUB — Schedule Publishing & Distribution

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-PUB-01 | The Registrar shall publish finalized, constraint-verified schedules **before the enrollment period opens**. | ✅ |
| FR-PUB-02 | Finalized schedules shall be distributable to students and faculty **by week, by day, and by class**. | ✅ |
| FR-PUB-03 | Schedule publication shall trigger automated email notifications to affected students and faculty. | ✅ |

*Built: `Features/Publishing` — `PublishSchedule` (Registrar/School-Admin-only `POST /api/publishing/{semesterId}/publish`) flips `ScheduleAssignment.IsPublished` for the semester's draft rows (idempotent; generation never disturbs published rows), audits `SchedulePublished`, and sends best-effort publication emails (`PublishingEmails`) to every assigned faculty member and every confirmed registrant of the semester, auditing `NotificationDispatched` on success (FR-PUB-03). `GetPublishedSchedule` (`GET /api/publishing/schedule`, any authenticated role) serves the published-only view with `day`/`cohort` filters plus distinct day/cohort pickers — the by-week / by-day / by-class distribution (FR-PUB-02) and the base for the student browse slice (FR-ENL-01). Client `features/publishing` at `/publishing`: draft-vs-published stats, confirm-to-publish, and By class / By week / By day tabs.*

### FR-DASH — Semester-Aware Administrative Dashboard

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-DASH-01 | The dashboard shall automatically filter all displayed metrics to the **active (or selected) semester**. | ✅ |
| FR-DASH-02 | The dashboard shall show real-time: enrollment and enlistment statistics (counts by section), room utilization analysis, faculty academic load reports, document submission completion rates, and pre-enrollment application volume. | ✅ |
| FR-DASH-03 | The dashboard shall expose the constraints/preferences that influenced scheduling assignment decisions (scheduling transparency). | ✅ |

*Built: `Features/Dashboard` — `GET /api/dashboard/metrics` (staff roles) returns live metrics scoped to the active semester by default and re-scoped by an explicit `semesterId` (FR-DASH-01, verified across two semesters): registration/application volume and status funnel, document completion rate (FR-DOC-04), pre-authorization counts, enlistment statistics with per-section seat counts and fill %, room utilization (hours per week against the Mon–Fri 07:00–18:00 board window), and per-faculty load vs. ceiling with imbalance/overload flags (FR-FAC-04). `GET /api/dashboard/scheduling-transparency` (FR-DASH-03) exposes per-assignment provenance (CSP engine vs. manual override, draft vs. published) plus the engine's hard constraints and soft factors. Client `features/dashboard` is fully live and role-differentiated: staff see the metric panels with a semester selector; students see a live enrollment-journey stepper (link → documents → clearance → enlistment); faculty see their teaching-week summary.*

### FR-NOTIF — Automated Email Notifications

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-NOTIF-01 | The system shall dispatch automated emails for at least these lifecycle events: **document submission reminders, registration confirmations, slot approvals, schedule publication / schedule milestones**. | ✅ |
| FR-NOTIF-02 | Notification dispatches shall be logged (see FR-AUD). | ✅ |

*Built: the email subsystem — `Common/Notifications` `IEmailSender`/`SmtpEmailSender` (Gmail SMTP; config in appsettings, password in user-secrets) with a logging fallback. All four required lifecycle events are wired: **registration + term-activation confirmations** (`Features/Registration/RegistrationEmails`), **document-submission reminders** (`Features/Documents/DocumentEmails`), **slot approval/rejection notices** (`Features/Enlistment/EnlistmentEmails`), and **schedule-publication notices** to faculty and confirmed students (`Features/Publishing/PublishingEmails`). Every successful dispatch is logged as `AuditAction.NotificationDispatched` (FR-NOTIF-02); sends are best-effort after the underlying action commits, so a mail failure never loses data.*

### FR-RPT — Reports & Analytics

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-RPT-01 | The system shall produce: validated registration reports, enlistment results, faculty load summaries, room utilization reports, and document checklist completion reports. | ✅ |
| FR-RPT-02 | Reports shall be semester-scoped and exportable for institutional planning. | ✅ |

*Built: `Features/Reports` (`/api/reports/*`, Registrar + Academic Head + School Admin) — all five required reports (validated registrations, enlistment results, faculty load summary, room utilization, document completion), each semester-scoped (default active, selectable) and delivered as JSON or as a ClosedXML **.xlsx export** via `?format=xlsx` (FR-RPT-02). Client `features/reports` at `/reports`: report switcher, semester selector, inline table, and one-click Excel export.*

### FR-AUD — Audit Trail

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-AUD-01 | The system shall keep accountability logs (audit trail entries) for security- and data-relevant actions: registrations, checklist changes, slot requests/approvals, schedule generation/overrides/publication, user management actions, and notification dispatches. | ✅ |

*Built: the audit-trail mechanism is complete and every mutating action that currently exists writes to it. Server — `Domain/AuditEntry` + `AuditAction` enum (persisted as string; the enum already declares the not-yet-built categories), `Common/Auditing/AuditLog` (resolves the actor from the request, staged in the same transaction as the action it records), `AuditEntries` table via `AddAuditTrail` migration, and a School-Admin-only read slice `Features/Audit/GetAuditTrail` (GET `/api/audit`, newest-first, action filter). Client — `features/audit` accountability table with action filter and refresh, wired at `/audit`. Instrumented: account registration, profile update, password change, schedule generation/overrides/publication, checklist changes (`DocumentChecklistUpdated`), account linking (`StudentAccountLinked`), pre-authorization (`StudentPreAuthorized`), slot requests/approvals/rejections (`SlotRequested`/`SlotApproved`/`SlotRejected`), user-management actions, and notification dispatches — every listed category now writes to the trail.*

---

## 4. Non-Functional Requirements (ISO/IEC 25010:2023)

The system will be evaluated by 45 purposively sampled respondents (30 students, 10 faculty, 5 administrative staff) using a 5-point Likert ISO 25010 questionnaire across six dimensions. **Target: overall weighted mean ≥ 4.00 ("Very Good")** — the empirically established threshold for sustainable adoption.

**Per-point status uses the same legend:** ✅ Done · 🟡 Partial · ⬜ Planned.

### NFR-1 Functional Suitability
- ✅ Generated schedules must have **zero hard-constraint violations** (100% hard-constraint satisfaction; benchmark systems fulfill ~78% of soft constraints). — enforced by construction: `SearchState.IsConsistent` rejects any assignment that would break a hard constraint.
- ✅ Automated outputs (schedules, enrollment confirmations, capacity counts) must be accurate. — schedule output is accurate, confirmation emails are sent, and enlistment capacity counts are transactional (`EnrolledCount` under optimistic concurrency + DB CHECK).

### NFR-2 Performance Efficiency
- ✅ Schedule generation completes in practical time for STI Alaminos-scale data (dozens of sections; benchmark: comparable engines flagged when generation exceeded 30 s). — most-constrained-variable ordering + `MaxSteps` guard bound the search.
- 🟡 The system remains responsive under concurrent multi-user access during peak enrollment periods. — the enlistment slice is built; formal load testing is still to be run.
- ✅ Real-time capacity counts must be consistent under concurrent enlistment (transactional enforcement of the 40-slot cap). — optimistic concurrency (`Section.RowVersion`) + DB CHECK constraint; a concurrent-approval race test confirms exactly one approval wins the last seat.

### NFR-3 Usability
- ✅ Role-differentiated interfaces tailored to each user type's functional needs. — RBAC-filtered navigation (`features/shell/nav.js`) shows each role only its functions.
- ✅ Student-facing modules optimized for minimal cognitive load and intuitive navigation. — auth, shell, live journey dashboard, document checklist, enlistment (cards with availability badges and one-click requests), and My schedule are all built to this standard; no student placeholder pages remain.
- ✅ Mobile-responsive web interface (accessible via web and mobile browsers). — responsive shell with off-canvas sidebar and mobile breakpoints.

### NFR-4 Reliability
- ✅ Data integrity through transactional database operations. — EF Core `SaveChanges` unit-of-work throughout; the enlistment cap adds optimistic concurrency and a database CHECK constraint.
- ⬜ Reliable availability during peak enrollment periods (IIS-hosted application layer). — deployment/hosting not yet exercised.
- ✅ ETL import must not corrupt or duplicate records (duplicate detection on load). — duplicates are detected by email and by name+birthdate (in-file and against the DB); a re-import of the same workbook was verified to load nothing twice.

### NFR-5 Maintainability
- ✅ Modular, component-based architecture: ASP.NET Core dependency injection; React component hierarchy; **vertical slices per feature** so features can be built, tested, and accepted independently (FDD). — established: `Features/*` slices server-side, `src/features/*` client-side.
- ✅ EF Core code-first migrations keep schema consistent and evolvable. — code-first migrations track every schema change (auth, scheduling, academic setup, curriculum, class sections, faculty load, subject hours, audit, registration).

### NFR-6 Portability
- ✅ Cross-platform runtime (ASP.NET Core) and component-based front-end (React) working across diverse device and browser configurations.

### NFR-7 Security & Compliance (cross-cutting)
- ✅ **RA 10173 (Data Privacy Act of 2012)**: secure handling of student and faculty personal data; passwords hashed; least-privilege RBAC; audit trail. — passwords hashed, least-privilege RBAC on every endpoint, and the audit trail (FR-AUD) records every listed action category.
- ✅ Authentication middleware at the application layer; role-based middleware validation on every request. — JWT authentication + `RequireRole` authorization applied across all slices (FR-AUTH-08).
- ✅ No financial data is stored in the system (out of scope). — holds by design.

---

## 5. Data Requirements (core entities)

Derived from the paper's data inputs and document analysis (SIS, Room Utilization Report, Class Scheduling Grid/Matrix, Confirmation of Faculty Loading, Student Master List, Registration records, Subjects & Units records):

- **User** (all six roles; credentials, role, status)
- **Student profile / SIS registration record** (with T&C acknowledgment timestamp)
- **Document requirement + submission status** (checklist per student)
- **Subject / Course** (units, weekly contact hours, program, curriculum, term/year level, prerequisites/co-requisites)
- **Class section / student block** (program/course, year level, section, per semester — the cohort a curriculum's subjects are delivered to)
- **Section** (subject, capacity ≤ 40, semester)
- **Faculty profile** (load limits, availability/time preferences)
- **Faculty load assignment** (faculty × subject × class section × semester; a subject-for-a-class is exclusive to one faculty)
- **Room** (capacity, attributes/equipment)
- **Academic calendar / Semester** (active-semester awareness)
- **Schedule assignment** (section × room × time slot × faculty)
- **Slot request / Enlistment transaction** (status: requested → approved/rejected)
- **Notification log**, **Audit trail entry**
- **System parameters** (unit-load limits, allowable time slots, section capacities)

---

## 6. Explicit Exclusions (Out of Scope)

1. **Tuition payment / financial functions** — down payments, fee computation, official receipts, all financial transaction management (handled by existing cashiering under separate accounting controls; no financial data stored in SEN-GEN).
2. **Multi-campus network integration** and cross-institutional enrollment.
3. **External government portal integration** (e.g., TESDA Registry System).
4. **Direct integration with apply.sti.edu** (SEN-GEN is a purpose-built alternative, not a patch).
5. **Machine learning / predictive AI** — the CSP engine is deterministic, rule-based AI requiring no training data (deliberate design decision; a stable foundation for possible future predictive features).
6. Evaluation generalization beyond STI Alaminos.

---

## 7. System Architecture & Technology Stack

Three-tier web application:

| Layer | Technology | Responsibility |
|-------|-----------|----------------|
| **Client (Front-End)** | React JS (Vite) | Six role-differentiated interfaces; communicates via REST over HTTPS |
| **Application (Back-End)** | ASP.NET Core Web API + EF Core, hosted on IIS | Business logic, CSP scheduling engine, RBAC validation, ETL pipeline, email dispatch, authentication middleware |
| **Data** | Microsoft SQL Server | All persistent data: users, documents, SIS records, sections, rooms, schedules, slot transactions, notification logs, audit trail |

### 7.1 Vertical Slice Architecture mapping

Each FDD feature is implemented as a self-contained vertical slice (request → validation → handler → persistence → response), organized under `Features/` on the server and `src/features/` on the client:

```
SENGENSystem.Server/
  Common/            // cross-cutting: persistence (DbContext), auth (JWT), results
  Domain/            // entities shared across slices
  Features/
    Auth/            // Register, Login (this iteration)
    Documents/       // requirements checklist        (FR-DOC)
    Registration/    // digital SIS                    (FR-SIS)
    PreEnrollment/   // ETL .xlsx import               (FR-PRE)
    Scheduling/      // CSP engine + overrides         (FR-SCHED, FR-FAC)
    Enlistment/      // slot selection + approval      (FR-ENL)
    Publishing/      // schedule publish/distribute    (FR-PUB)
    Dashboard/       // semester-aware analytics       (FR-DASH)
    Notifications/   // email dispatch + log           (FR-NOTIF)
    Reports/         // reporting                      (FR-RPT)
    UserManagement/  // admin account management       (FR-AUTH-07)

sengensystem.client/src/
  features/
    auth/            // login, register (this iteration)
    ...one folder per feature above
```

### 7.2 FDD build order (Plan by Feature — dependency-aware)

1. ✅ **Foundation**: user authentication, RBAC, SQL Server schema (Register/Login/Me, Profile, JWT + role authorization, app shell with role-filtered navigation, EF Core migrations).
2. ✅ **Core CSP scheduling engine** — engine + generate/get-schedule API and the Academic-Head generate/review UI; supporting setup (Academic Setup, Curriculum & Subjects incl. weekly hours, Faculty Load allocation); manual overrides via the drag-and-drop Schedule Board (FR-FAC-02); faculty My-schedule view (FR-FAC-05); **faculty time-slot preferences (FR-SCHED-03) and curriculum prerequisite/co-requisite awareness (FR-SCHED-04)**.
3. ✅ **Student-facing enlistment interface** (FR-DOC, FR-SIS, FR-PRE, FR-ENL, FR-PUB) — digital SIS registration, document checklist board + reminders, term activation, schedule publishing, identity link + pre-authorization, **subject enlistment with Registrar slot approvals**, and the pre-enrollment .xlsx ETL import.
4. ✅ **Administrative dashboard and notification modules** — audit trail (FR-AUD) covering every listed category; user management (FR-AUTH-07); the full email/notification subsystem (FR-NOTIF); **live semester-scoped dashboard metrics (FR-DASH) and exportable reports (FR-RPT)**.

---

## 8. Acceptance & Evaluation Criteria

- Each feature is documented with acceptance criteria derived from stakeholder interviews and ISO 25010 dimensions, and is validated/accepted by stakeholders (FDD Phase 5).
- Unit tests are written for all scheduling engine functions and API endpoints prior to integration.
- Post-deployment: ISO 25010 questionnaire (content-validated by a panel of 3 IT experts) administered to the 45 respondents; scores computed by average weighted mean and interpreted: 4.50–5.00 Excellent, 3.50–4.49 Very Good, 2.50–3.49 Good, 1.50–2.49 Fair, 1.00–1.49 Poor.
- Primary success benchmark: **overall weighted mean ≥ 4.00**.
