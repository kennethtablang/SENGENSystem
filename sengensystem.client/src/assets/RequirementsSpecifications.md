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
| **Last synchronized with code** | 25 July 2026 — re-derived from the slices, domain entities, migrations, and routes actually present in the repository |

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

The system enforces **Role-Based Access Control** with six institutional roles, plus a **Super Admin** technical role that owns the research-evaluation surface. Every user interacts only with the functions and data appropriate to their institutional responsibility.

| # | Role | Responsibilities in SEN-GEN |
|---|------|------------------------------|
| 1 | **School Admin** | Institutional system configuration, system parameters, user account management, full analytics visibility |
| 2 | **Academic Head** | Schedule generation, program-level schedule review, faculty load monitoring and compliance, manual schedule overrides, enrollment-cycle stage control, user account management |
| 3 | **Registrar** | Document submission verification, registration data management, ETL-based pre-enrollment import, enlistment (slot) approvals, schedule publishing, reporting |
| 4 | **Admission Officer** | Pre-authorization of incoming/returning students, admission requirements catalog and checklist management, term-activation validation, recording official student numbers |
| 5 | **Faculty Member** | View assigned teaching schedules and section enrollment counts, receive automated notifications |
| 6 | **Student** | Submit enrollment requirements, complete digital SIS registration, browse published schedules, check real-time slot availability, perform subject enlistment |
| 7 | **Super Admin** | *(technical/research role)* Everything a School Admin reaches, **plus** the ISO/IEC 25010 rating-survey dispatch and results (FR-EVAL) |

**Elevation rule (`Common/Auth/SchoolAdminClaimsTransformation`).** A **School Admin** is granted every role's claims *except* `SuperAdmin`, so the admin sidebar shows every institutional function without duplicating role lists on each endpoint. A **Super Admin** is granted every role including `SchoolAdmin`. Super-admin-only screens are withheld from the School Admin wildcard by an explicit `hideFor` in `features/shell/nav.js`.

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
| FR-AUTH-10 | Users shall be able to **recover their own account** without staff intervention: a self-service forgot-password flow that emails a single-use, time-limited reset link. | ✅ |
| FR-AUTH-11 | Users shall be able to **change their own email address**, confirmed by a link sent to the new mailbox before the change takes effect. | ✅ |
| FR-AUTH-12 | Users shall be able to enable **opt-in two-factor authentication** — sign-in then requires a one-time code emailed to the account address. | ✅ |
| FR-AUTH-13 | Student login accounts shall be **provisioned by the system from the SIS submission** (students are not required to self-register), issued a system-generated temporary password that **must be changed at first sign-in**. | ✅ |

*Built: `Features/Auth` (Register, Login, TwoFactor, Me, ForgotPassword) + `Features/Profile` (UpdateProfile, ChangePassword, ChangeEmail, TwoFactor); client `features/auth`, `features/profile`. FR-AUTH-08 complete: every slice now exists and every endpoint carries JWT bearer authentication plus a `RequireRole` policy (or an intentional `AllowAnonymous` for the public SIS/login/survey flows); cross-role access attempts were verified to return 403 across the documents, pre-authorization, enlistment, approvals, dashboard, reports, and preferences endpoints. FR-AUTH-07 user management built: `Features/UserManagement` (account CRUD — create/update/deactivate/reactivate/reset-password across all roles, with guards against deactivating the last active School Admin) + client `features/users` at `/users`.*

*Account-lifecycle additions: **FR-AUTH-10/11** — `Features/Auth/ForgotPassword` (`POST /api/auth/forgot-password`, `POST /api/auth/reset-password`) and `Features/Profile/ChangeEmail` issue hashed, single-use, expiring tokens (`Common/Auth/OneTimeToken`); the forgot-password response is deliberately identical whether or not the address exists, so the endpoint cannot be used to enumerate accounts. Audited as `PasswordResetRequested`/`PasswordResetCompleted`/`EmailChangeRequested`/`EmailChanged`. Client routes `/forgot-password`, `/reset-password`, `/confirm-email`. **FR-AUTH-12** — opt-in 2FA (`Domain/User.TwoFactorEnabled`, migration `AddTwoFactorAuth`): enabling is a two-step email-code confirmation, disabling re-checks the account password, and `POST /api/auth/login` returns `{ twoFactorRequired, challengeToken }` instead of a JWT for a 2FA account — `POST /api/auth/2fa/verify` exchanges the emailed 6-digit code for the token (attempt-limited and expiring, `TwoFactorChallenge`), with `/2fa/resend` for a fresh code. Audited as `TwoFactorEnabled`/`TwoFactorDisabled`/`TwoFactorChallengeIssued`/`TwoFactorFailed`. **FR-AUTH-13** — `Common/Auth/StudentAccountProvisioner` turns a SIS submission (or a validated term activation) into a student login: idempotent, keyed on the SIS email, with a generated temporary password emailed only to that mailbox and `User.MustChangePassword` set (migration `AddConstraintWeightsAndMustChangePassword`). The client forces `FirstLoginPasswordChange` before any other screen; `Features/Profile/ChangePassword` clears the flag. Audited as `StudentAccountProvisioned`. Self-service registration (`POST /api/auth/register`) remains available for staff-created and legacy flows.*

### FR-DOC — Document Submission & Requirements Checklist

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-DOC-01 | The system shall maintain a **digital requirements checklist** per new enrollee covering pertinent papers: **Form 137, birth certificate, good moral certificate** (extensible list). | ✅ |
| FR-DOC-02 | The Admission Officer shall be able to record, verify, and update the submission status of each required document per student. | ✅ |
| FR-DOC-03 | The system shall display each enrollee's completion status (complete / incomplete, per-document state) as an auditable record. | ✅ |
| FR-DOC-04 | The system shall report document submission completion rates on the administrative dashboard. | ✅ |
| FR-DOC-05 | The system shall send automated **document submission reminder** emails for incomplete checklists. | ✅ |
| FR-DOC-06 | The requirements list shall be **configurable at runtime, not hard-coded**: school personnel add, rename, reorder, and archive requirements without a code change. | ✅ |
| FR-DOC-07 | Each requirement shall be **scoped to the programs it applies to**, so an enrollee's checklist contains only the papers their chosen program actually requires. | ✅ |
| FR-DOC-08 | Each requirement shall additionally be **scoped to the student types it applies to**, so a new enrollee is asked only for the papers their high school issues (Form 138/137, good moral) and a transferee only for the papers their previous college issues (transcript, certificate of transfer). | ✅ |
| FR-DOC-09 | Requirements shall declare their **accepted submission forms** — a requirement may accept a **certificate of grades** in place of a photocopy, which the Official Transcript of Records does. | ✅ |

*Built: `Features/Documents/Checklist` gives the **Admission Officer** (plus Registrar/School Admin) a dedicated board (`/documents`, `GET/PUT /api/documents`) — per-enrollee expandable checklist with per-paper status dropdowns, complete/incomplete filter, and audited updates (`DocumentChecklistUpdated`, FR-DOC-02/03). `Features/Documents/Reminders` (`POST /api/documents/reminders`) emails exactly the missing papers to every incomplete checklist, or one enrollee (`DocumentEmails`, FR-DOC-05). Students see their own checklist at `/documents` via the account link (FR-ENL-05). The dashboard completion-rate report (FR-DOC-04) is live on the administrative dashboard and in the document-completion report.*

*FR-DOC-06/07 built — the checklist is now **catalog-driven** (migration `AddRequirementCatalog`). The former fixed `DocumentType` enum was replaced by `Domain/AdmissionRequirement` (code, name, description, sort order, active flag) and `Domain/AdmissionRequirementProgram` (the `ProgramTrack`s it applies to); `RegistrationDocument` now carries a stable `RequirementCode` string, and the nine built-in requirements keep the old enum names so historical checklist rows still resolve. `Features/Documents/Requirements` (`GET/POST/PUT/DELETE /api/requirements`, Admission Officer + Registrar + School Admin) is the management slice — archive rather than delete, so existing checklists keep their history; audited as `RequirementCreated`/`RequirementUpdated`/`RequirementArchived`. A SIS submission seeds one checklist row per **active** requirement whose program list contains the enrollee's program (`DocumentChecklist.SeedDocuments`) — so, for example, ITP enrollees skip the health papers only HRS/HRA need. Client: the `RequirementsModal` on `/documents`.*

*FR-DOC-08/09 built (migration `AddRequirementApplicabilityAndClassStart`). `AdmissionRequirement` gained `AppliesToNewStudents`/`AppliesToTransferees`, `IsRequiredForAuthorization`, and `AcceptsCertificateOfGrades`, all editable in the `RequirementsModal`. Seeding now filters on student type as well as program, and every read path filters through `DocumentChecklist.Applicable` so a checklist seeded before the distinction existed still shows — and counts — only what applies. The built-ins were reclassified to the school's practice: **new students** are asked for the Form 138, Form 137, and good moral; **transferees** for the Official Transcript of Records and the certificate of transfer (honorable dismissal); the PSA birth certificate and the health papers are asked of both. The migration deletes the now-inapplicable historical rows rather than leaving them permanently unsubmitted. `DocumentStatus` gained `CertificateOfGrades`: a requirement offers it **instead of** `XeroxCopy` (never both), so the transcript is accepted as an original or against a certificate of grades but never as a photocopy — the server rejects the option a requirement does not offer, on both the checklist board and the Registrar's drawer, and existing "xerox copy" transcript rows were carried over to the new status.*

### FR-SIS — Digital Student Information Sheet (Registration)

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-SIS-01 | The system shall provide a **fully digital SIS registration module** replacing the paper SIS form, with a structured data-entry workflow for personal and academic details. | ✅ |
| FR-SIS-02 | SIS submission shall require terms-and-conditions acknowledgment with a captured timestamp. | ✅ |
| FR-SIS-03 | SIS fields shall enforce formatting rules (proper name capitalization, required-field validation, completeness validation) before acceptance. | ✅ |
| FR-SIS-04 | The Registrar shall manage (view, correct, confirm) registration data captured through the SIS module. | ✅ |
| FR-SIS-05 | Registration confirmation shall trigger an automated email notification to the student. | ✅ |
| FR-SIS-06 | The SIS shall capture a **structured Philippine address** (region → province → city/municipality → barangay) rather than free text. | ✅ |
| FR-SIS-07 | SIS data shall be stored in the institution's **canonical ALL-CAPS form**, matching the paper SIS and the downstream student-records system. | ✅ |
| FR-SIS-08 | The Admission Officer shall be able to record the **official student number** issued by the separate student-records system against a SEN-GEN registration. | ✅ |

*Built: `Features/Registration` — a public, account-less digital SIS. New students/transferees self-submit the structured SIS form at `/register-sis` (anonymous `POST /api/registration`) → issued a registration number, a login account provisioned with a temporary password (FR-AUTH-13), document checklist seeded from the requirements catalog, and a confirmation email sent (FR-SIS-05); T&C acknowledgment is captured with a timestamp (`StudentRegistration.TermsAcceptedAtUtc`, FR-SIS-02). Inputs enforce required-field/completeness validation (FR-SIS-03). The Registrar views, corrects, and confirms records at `/registrations` (`Features/Registration/Manage`, FR-SIS-04). Returning students self-serve term activation (`/term-activation`), validated by the Admission Officer at `/term-activations`. Client `features/registration`. (`Features/Profile` self-service account editing remains separate account maintenance.)*

*FR-SIS-06/07: the SIS is stored **ALL-CAPS** end to end — this is how FR-AUTH-03's "proper capitalization" requirement is met for SIS data specifically, because the institution's paper SIS and downstream records system are both upper-case; the client uppercases on entry and the server normalises on save, while login accounts stay keyed on the lower-cased email so the two still match. The address is a **bundled PSGC cascading picker** (`features/registration/AddressPicker.jsx`) sourced from a local dataset — no external API call, so the SIS works on a poor connection. **FR-SIS-08**: `Features/Registration/AssignStudentNumber` (`GET /api/registration/student-number`, `POST /api/registration/{id}/student-number`, Admission Officer) — SEN-GEN issues its own registration number, and once the enrollee exists in the separate student-records system the officer records the **official** number against the registration (`StudentRegistration.OfficialStudentNumber`, migration `AddOfficialStudentNumber`); uniqueness is enforced across registrations and every assignment is audited (`StudentNumberAssigned`). Client `/assign-student-number`, defaulting to the work queue of registrations still missing a number, with a **Show** switch for *Still to number / Already numbered / All registrations* (`?status=`) so the students who already hold a number can be reviewed and corrected without hunting for them. Each row states both statuses — the SIS registration status and whether a number is on file — and the page carries whole-queue tallies (numbered / pending / total) that stand independent of the current view and search.*

### FR-PRE — Pre-Enrollment (ETL Import)

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-PRE-01 | The Registrar shall be able to **import prospective student lists from `.xlsx` files** via an ETL pipeline: **Extract** from .xlsx → **Validate** field completeness and format consistency → **Transform** to target schema → **Load** with duplicate detection. | ✅ |
| FR-PRE-02 | Imported students shall be **pre-authorized** for online subject slot selection only after their SIS registration is Registrar-confirmed **and the requirements flagged as required for authorization are on file**; the remaining admission documents are tracked for follow-up rather than used as a hard gate. | ✅ |
| FR-PRE-03 | Import errors (missing fields, format issues, duplicates) shall be reported row-by-row without aborting valid rows. | ✅ |
| FR-PRE-04 | The Admission Officer shall be able to pre-authorize incoming and returning students. | ✅ |

*Built. **Pre-authorization**: `Features/PreEnrollment/PreAuthorize` (`/api/pre-authorization`, Admission Officer + Registrar/School Admin) — clears students at `/pre-authorization`; the server enforces the gate (Registrar-**confirmed** SIS, else 400 with the exact blocker), grants are audited (`StudentPreAuthorized`), idempotent, and revocable (FR-PRE-02/04). **Refinement:** a *fully* complete checklist no longer blocks clearance, but a **named subset** does. Admission papers keep arriving through the term, and holding a confirmed student out of enlistment over a pending health certificate was the exact bottleneck SEN-GEN exists to remove — yet the papers that establish a student's academic standing cannot wait. So each requirement carries `IsRequiredForAuthorization` (FR-DOC-08): a **new enrollee** needs their report card and good moral, a **transferee** their transcript and certificate of transfer, and everything else is shown as submitted/total counts for follow-up. `DocumentChecklist.MissingAuthorizationRequirements` drives both the 400 (which names each outstanding paper) and the client's Blocked/Ready state, so the button and the server agree on why. This matches the FR-CYC stage order (Registration → Document submission) and the enlistment gate in `EnlistmentEligibility`. The **identity link**: `StudentRegistration.UserId` (unique, nullable; migration `AddStudentLinkAndPreAuthorization`) — students explicitly claim their record with **three matching facts**: student number (delivered only to the SIS email), account email equal to the SIS email, and date of birth (`Features/Registration/LinkAccount`, audited `StudentAccountLinked`). There is deliberately **no auto-link at sign-up** — self-service registration never proves mailbox ownership, so linking on the email string alone would enable pre-registration account takeover (RA 10173 exposure); a full email-verification flow is the recommended future hardening. **ETL import**: `Features/PreEnrollment/Import` (`POST /api/pre-enrollment/import`, Registrar; ClosedXML) — Extract from .xlsx → Validate per row (required fields, email/date formats, enum values) → Transform (proper-cased names, issued student numbers, seeded document checklists) → Load with duplicate detection (by email and by name+birthdate, both in-file and against the DB); the row-by-row report loads valid rows even when others fail (FR-PRE-03, verified with a mixed workbook and an idempotent re-import), audited `PreEnrollmentImported`. Client `/pre-enrollment` with a downloadable .xlsx template (`GET /api/pre-enrollment/template`). Imported students flow through the same confirm → checklist → pre-authorization gate as everyone else.*

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
| FR-SCHED-09 | The engine shall respect **room kind** as a hard constraint: laboratory hours may only be placed in the laboratory kind the subject requires (computer vs. kitchen), and lecture hours only in lecture rooms. | ✅ |
| FR-SCHED-10 | A **lecture–laboratory subject shall be scheduled as two separate meetings** (its lecture hours and its laboratory hours), not one contiguous block. | ✅ |
| FR-SCHED-11 | The Academic Head shall be able to **finalize** a reviewed draft (locking it against regeneration and board edits) and **reopen** it for further edits, before the Registrar publishes. | ✅ |
| FR-SCHED-12 | The engine's **soft-constraint weights and search budgets shall be tunable** by staff, not compiled in. | ✅ |
| FR-SCHED-13 | The Academic Head shall set **when the teaching day opens** — the earliest a generated class may start — without editing the period grid. | ✅ |
| FR-SCHED-14 | On the manual board, dropping a subject while the read-across **"All rooms"** view is active shall open a **room selection** prompt showing each room's availability for that exact slot, with unavailable rooms disabled and the reason stated. | ✅ |

*Built: `Features/Scheduling` — `Engine/CspScheduler` (backtracking search, most-constrained-variable ordering, time- and step-budget early-termination guards), `GenerateSchedule` (AcademicHead-only) and `GetSchedule` endpoints; domain entities Subject/Section/Room/TimeSlot/FacultyProfile/ScheduleAssignment + migrations. Client `features/scheduling` — Generate page (trigger + animated progress overlay + placement summary + result table) and Review page (cohort-grouped, draft/finalized/published status), both Academic-Head routes. Hard constraints enforced in `SearchState.IsConsistent`; soft scoring in `SearchState.SoftScore`. FR-SCHED-03 complete: `SearchState.SoftScore` balances load, minimizes cohort idle-gaps, rewards right-sized room fit, and consumes **faculty time-slot preferences** (`Domain/FacultyTimePreference`, migration `AddFacultyTimePreferences`; Academic Head records windows via `Features/FacultyLoad/Preferences` and the Preferences modal on `/faculty-load`) — verified: a member's in-preferred-window ratio rose from 0.33 to 1.0 after setting windows, with zero hard-constraint violations. FR-SCHED-04 complete: generation validates the semester's offerings against `SubjectPrerequisite` edges before solving — a prerequisite placed later in the curriculum than its dependent fails with the exact sequencing reason (422), and a same-year/term prerequisite is treated as a **co-requisite** that every cohort must be offered alongside the dependent; class sections carry their curriculum (`AddClassSectionCurriculum`) so a cohort is solved against the curriculum it actually follows.*

*FR-SCHED-09/10 built (migration `AddRoomKindAndSubjectDelivery`). `Domain/RoomKind` distinguishes **LectureRoom / ComputerLaboratory / KitchenLaboratory** — the campus has exactly one computer lab and one kitchen lab against many lecture rooms, so a plain "is a lab" flag would let an ITP programming class land in the kitchen and let a pure lecture consume the one lab the campus shares. `Domain/SubjectDelivery` (LectureOnly / LaboratoryOnly / **LectureLaboratory**) drives the derived-component rule: a lecture–laboratory subject enters the CSP as **two independent meeting variables** (`ClassComponent.Lecture`, `ClassComponent.Laboratory`), each with its own hours and its own room-kind requirement, so the two halves are placed independently and never merged into one long block. Room-kind mismatch is rejected in `SearchState.IsConsistent` alongside the other hard constraints, and on the manual Schedule Board too.*

*FR-SCHED-11 built: `Features/Scheduling/Finalize` (`POST /api/scheduling/{semesterId}/finalize`, `/reopen`, Academic Head + School Admin) makes the lifecycle explicit — **Draft → Finalized → Published**. Finalizing locks the semester's draft rows against regeneration and board edits until reopened; audited as `ScheduleFinalized`/`ScheduleReopened` (migration `AddScheduleFinalization`). FR-SCHED-12 built: the soft weights (preference, idle gap, room fit, gap saturation) and the engine's wall-clock and search-step budgets live on `Domain/SystemSettings` and are edited on `/parameters` — see FR-PARAM. The `SoftConstraints` endpoint (`GET /api/scheduling/soft-constraints`) reports what the engine actually optimised against on the last run.*

*FR-SCHED-13/14 built (migration `AddRequirementApplicabilityAndClassStart`). **Class start time**: `SystemSettings.ClassDayStartMinutes` (default 07:00) is set in the Generation settings panel on `/generate-schedule` and carried by the same `soft-constraints/weights` PUT; generation builds its blocks only from allowable periods starting at or after it, so moving the day's opening never means re-cutting the period grid. Values are validated to the half hour, the panel reports how many periods the setting trims (and refuses to leave the engine with none), and the manual board starts its calendar axis at the same time so both views are read against one day. **Room selection**: the board's "All rooms" view used to refuse a drop outright. It now opens `RoomPickerModal` for the dropped day/time — every room listed, each either selectable or muted with the reason (wrong room kind for the meeting per FR-SCHED-09, or already booked in that window, quoting the class that holds it), available rooms first and smallest first so the snuggest fit leads. A faculty or cohort clash rules out every room, so it is reported instead of opening a picker where nothing could be chosen. The server re-checks all of it — the modal only spares the officer a refused drop.*

### FR-FAC — Faculty Assignment & Load Management

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-FAC-01 | The system shall support assignment of faculty members to subject loads. | ✅ |
| FR-FAC-02 | The system shall provide **manual class scheduling override** capability (adjust algorithmically generated assignments) while maintaining automatic conflict detection on every override. | ✅ |
| FR-FAC-03 | Faculty load shall be validated against applicable regulatory/institutional limits at assignment time. | ✅ |
| FR-FAC-04 | The system shall continuously **monitor faculty load distribution** so the Academic Head can verify balance without manual cross-referencing, and shall surface imbalances/overloading. | ✅ |
| FR-FAC-05 | Faculty members shall have a digital interface to view their finalized assigned schedules and section enrollment counts. | ✅ |

*Built: `Features/FacultyLoad` — the Academic Head allocates subject loads to faculty members per semester through the **Faculty Load Management** screen and its **Assign Load** modal. Assignment is per **class section** (student block): a row is a subject taught to a specific block (e.g. CS301 → BSCS 3-A), a (subject, class-section) pair is exclusive to one faculty member (an already-assigned row is shown with its holder and muted), and the running unit total is validated against each member's `MaxLoadUnits` ceiling at assignment time (FR-FAC-03). Class sections (course · year · section, created per semester) are managed under **Academic setup** (`Features/AcademicSetup/ClassSections`). The CSP engine additionally assigns faculty to sections automatically during generation (respecting `MaxLoadUnits`, enforced in `SearchState.IsConsistent`). FR-FAC-04 ✅ — the load-management list surfaces each member's assigned units against their ceiling with a load bar and over-ceiling indicator, and the administrative dashboard adds the distribution analysis: per-member load bars with mean-units context and Balanced / AboveAverage / BelowAverage / Overloaded / Unassigned flags, plus the exportable faculty-load report. The institution's own **Confirmation of Faculty Loading** form — per member, consolidated, and as a printable PDF — plus per-member grid schedules are produced by `Features/Reports/FacultyLoading` at `/reports/faculty-load` (FR-RPT-03), so the Academic Head signs off on the familiar instrument rather than a new one.*

*FR-FAC-02 built: `Features/Scheduling/Board` — the **Schedule Board** (`/scheduling/board`, Academic Head / School Admin) is a calendar-based, drag-and-drop timetable builder (FullCalendar). Faculty-allocated subjects are dragged from an "Assigned Subjects" pool onto a weekly grid; every placement, move, and resize runs live conflict detection (no room double-booking, no faculty double-assignment, no same-cohort overlap → 409 with a human-readable reason). Placements persist as `ScheduleAssignment`s (`IsManualOverride`), so they share the review/publish pipeline. A right-hand **Weekly Hours Tracker** shows plotted vs. required hours per subject×faculty×section (required from the new `Subject.Hours` field), and calendar blocks are colour-coded per subject. FR-FAC-05 built: `Features/Scheduling/MySchedule` — a Faculty Member opens **My schedule** (`/schedule`) to view their own read-only weekly timetable (calendar + day-by-day list with per-section seat counts); students see the same view built from their approved enlistments. Both the generated schedule and the board honour room kinds and treat a lecture–laboratory subject as two separate meetings (FR-SCHED-09/10), and a finalized semester is locked against board edits until reopened (FR-SCHED-11).*

### FR-ENL — Student Subject Enlistment

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-ENL-01 | Students shall browse **published** class schedules showing subjects, sections, times, rooms, and faculty. | ✅ |
| FR-ENL-02 | The interface shall display **real-time slot availability** per section. | ✅ |
| FR-ENL-03 | Each class section shall be capped at a **maximum of 40 slots**, enforced at the system/database level at the moment of enlistment — requests beyond capacity are automatically rejected. | ✅ |
| FR-ENL-04 | Slot selection shall be routed through an **approval workflow** (student requests a seat → Registrar approves), with slot-approval email confirmation. | ✅ |
| FR-ENL-05 | Only **pre-authorized, registered** students (account linked to a Registrar-confirmed SIS record, then cleared by the Admission Office) may enlist. | ✅ |
| FR-ENL-06 | Enlistment shall be available online 24/7 from any internet-connected device during the enlistment window — no in-person visit required. | ✅ |
| FR-ENL-07 | The system shall prevent a student from enlisting in sections with overlapping time slots. | ✅ |
| FR-ENL-08 | The institution shall be able to **open and close enlistment** system-wide between periods, without deactivating individual students. | ✅ |
| FR-ENL-09 | The institution shall be able to cap the **total subject units** a single student may hold in a term. | ✅ |
| FR-ENL-10 | A section's seat cap shall be **overridable by authorized staff** to complete a section, within the institutional ceiling, with the override recorded. | ✅ |

*Built: `Features/Enlistment` + `Domain/SlotRequest` (migration `AddEnlistment`). Students browse published-only sections with per-section seat counts and meetings at `/enlistment` (`Browse`, FR-ENL-01/02) and request seats (`RequestSlot`) — refused server-side when not linked + pre-authorized + confirmed (FR-ENL-05, via `EnlistmentEligibility`), when a live request for the section or subject already exists, when the section is full, or when times overlap the student's other requested/approved sections (FR-ENL-07, verified with a forced-overlap test). Requests route through the Registrar's queue at `/approvals` (`Approvals`, FR-ENL-04): approving consumes a seat under **optimistic concurrency** (`Section.RowVersion`) with a **database CHECK constraint** (`EnrolledCount <= Capacity`, `Capacity ≤ 40`) as the backstop — a two-thread race for the last seat was tested and exactly one approval wins (FR-ENL-03); decisions are emailed (`EnlistmentEmails`) and audited (`SlotRequested/SlotApproved/SlotRejected`). Students can cancel pending requests (`MyEnlistment`); approved classes appear on **My schedule** with live seat counts. FR-ENL-06 holds by design — the platform is a web application; 24/7 availability is a deployment property.*

*Eligibility refinement (`EnlistmentEligibility`): the gate is **linked account + Registrar-confirmed SIS + pre-authorization**. An **incomplete document checklist deliberately does not block slot selection** — admission papers keep arriving after enlistment opens, and the Admission Office tracks and follows up on the pending ones instead. This is the stage-order decision recorded in FR-CYC: Registration precedes Document submission in the cycle.*

*FR-ENL-08/09 built on `Domain/SystemSettings` (see FR-PARAM): `EnlistmentOpen` is the institution-wide switch — with it off, `RequestSlot` refuses every new request regardless of individual eligibility; `MaxEnlistmentUnitsPerStudent` (0 = no ceiling) is checked against the student's already-requested/approved units before a new seat is reserved, and the refusal names the ceiling. `MinSectionEnrollment` is advisory — under-enrolled sections are surfaced to the admin rather than blocked. FR-ENL-10 built into the approvals slice: when a section is full, a Registrar / Academic Head / School Admin may raise `Section.Capacity` (never above the institutional `SectionCapacityCap`, and the DB CHECK still holds) to let a pending request through; the override is audited as `SectionCapacityOverridden` and notifies the affected staff.*

### FR-CYC — Enrollment Cycle Stage Control

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-CYC-01 | The active term shall carry an explicit **enrollment stage** reflecting where the institution is in the cycle: Preparation → Registration → Document submission → Subject enlistment → Enrollment closed. | ✅ |
| FR-CYC-02 | The current stage shall be **visible to every signed-in user**, so a student knows what they can do right now without asking staff. | ✅ |
| FR-CYC-03 | Only the Academic Head (and School Admin) shall advance the term to the next stage, or correct it back to an earlier one. | ✅ |
| FR-CYC-04 | Stage changes shall be recorded in the audit trail. | ✅ |

*Built: `Features/EnrollmentCycle` (`GET /api/enrollment-stage` for every authenticated role; `POST /api/enrollment-stage` and `POST /api/enrollment-stage/advance` for Academic Head + School Admin), backed by `Semester.EnrollmentStage` (`Domain/EnrollmentStage`, migration `AddEnrollmentStage`). The API owns the stage wording so every screen agrees. `/advance` steps exactly one place forward; the explicit set is for corrections, including stepping back. Client `features/enrollment-stage` — an **EnrollmentTicker** in the top bar shows every user the active term and its stage, and the `StageModal` is the Academic Head's control. Audited as `EnrollmentStageChanged` (FR-CYC-04).*

*Note the stage **order**: **Registration comes before Document submission.** A student files the SIS first; admission papers may keep arriving afterwards and do not gate enlistment (see FR-ENL). The cycle ends at *Enrollment closed* — tuition payment is out of scope (§6).*

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

*Deep-dive analytics split out into `Features/Analytics/RoomUtilization` (`GET /api/analytics/room-utilization`, Registrar + Academic Head + School Admin, with an `/export` .xlsx twin) — institution-wide classroom usage that ranks rooms by utilised hours against the teaching window and flags critically underutilised rooms, so the Academic Head can justify room reallocation. Client `/analytics/room-utilization`.*

### FR-PARAM — System Parameters

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-PARAM-01 | The School Admin shall maintain the institutional scheduling and enrollment parameters named in FR-SCHED-05 — **unit-load limits, allowable time slots, and section capacities** — from a screen, without a code or database change. | ✅ |
| FR-PARAM-02 | Lowering a parameter shall never silently rewrite existing records; conflicts shall be **reported** to the admin instead. | ✅ |
| FR-PARAM-03 | Parameter changes shall be recorded in the audit trail. | ✅ |

*Built: `Features/SystemParameters` (`/api/parameters`, School Admin) over the singleton `Domain/SystemSettings` (migrations `SystemParametersDomainMigrate`, `AddConstraintWeightsAndMustChangePassword`, `AddTimeSlotIsAllowable`, `AddEnrollmentAndEngineParameters`). The screen at `/parameters` tunes four families:*

- ***Enlistment governance*** — `SectionCapacityCap` (the institutional seat ceiling, default 40, FR-ENL-03), `EnlistmentOpen`, `MaxEnlistmentUnitsPerStudent`, `MinSectionEnrollment`.
- ***Allowable time slots*** — create/update/delete `TimeSlot` rows and mark them allowable, defining the grid the CSP engine and the Schedule Board may place classes into.
- ***Faculty unit-load limits*** — per-member `FacultyProfile.MaxLoadUnits` ceilings, editable in place.
- ***Engine tuning*** — the soft-constraint weights (preference / idle gap / room fit, plus gap saturation hours) and the generation budgets (`ScheduleTimeBudgetSeconds`, `ScheduleMaxStepsThousands`) that bound one CSP run (FR-SCHED-07/12).

*FR-PARAM-02 holds by construction: lowering the seat cap cannot drop a section below its `EnrolledCount` without violating the `CK_Sections_EnrolledCount` constraint, so sections already above a lowered cap are **listed back to the admin** rather than changed. Values that belong to an individual record stay on that record — a section's own seat count on `Section.Capacity`, a member's ceiling on `FacultyProfile.MaxLoadUnits` — and only institutional defaults live in the singleton. Audited as `SystemParametersUpdated`, `SectionCapacityCapChanged`, `TimeSlotSaved`, `FacultyLoadLimitChanged`, `SoftConstraintWeightsChanged` (FR-PARAM-03).*

### FR-NOTIF — Automated Notifications

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-NOTIF-01 | The system shall dispatch automated emails for at least these lifecycle events: **document submission reminders, registration confirmations, slot approvals, schedule publication / schedule milestones**. | ✅ |
| FR-NOTIF-02 | Notification dispatches shall be logged (see FR-AUD). | ✅ |
| FR-NOTIF-03 | Signed-in users shall additionally receive **in-app notices** on a notification bell, readable and dismissible without leaving the system. | ✅ |
| FR-NOTIF-04 | The navigation shall surface **outstanding-work counts** per role (unread notices, pending approvals, registrations awaiting confirmation, term activations awaiting validation), scoped to the active term. | ✅ |

*Built: the email subsystem — `Common/Notifications` `IEmailSender`/`SmtpEmailSender` (Gmail SMTP; config in appsettings, password in user-secrets) with a logging fallback. All four required lifecycle events are wired: **registration + term-activation confirmations** (`Features/Registration/RegistrationEmails`), **document-submission reminders** (`Features/Documents/DocumentEmails`), **slot approval/rejection notices** (`Features/Enlistment/EnlistmentEmails`), and **schedule-publication notices** to faculty and confirmed students (`Features/Publishing/PublishingEmails`); account-lifecycle mail (temporary passwords, password reset, email confirmation, 2FA codes, survey invitations) lives in `Features/Auth/AccountEmails`. Every successful dispatch is logged as `AuditAction.NotificationDispatched` (FR-NOTIF-02); sends are best-effort after the underlying action commits, so a mail failure never loses data.*

*FR-NOTIF-03 built: `Domain/Notification` + `NotificationKind` (migration `AddNotifications`) and the `Notifier` service that mutation slices call alongside their email. `Features/Notifications` (`GET /api/notifications`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`, any authenticated user — each user reads only their own rows). In-app notices **complement, never replace** the emails: mail also reaches account-less registrants, while these rows exist only for signed-in users. `Common/Notifications/NotificationRecipients` resolves the staff who should hear about an event (e.g. every Registrar for a capacity override). Client `features/notifications` — the `NotificationsBell` in the top bar plus a full `/notifications` page. FR-NOTIF-04 built: `Features/Navigation` (`GET /api/nav/badges`) returns the four counts, each **role-scoped** so a role only gets a number for a function it can actually open, and each filtered to the active semester so a past term's backlog never leaks into the new one. Client `features/shell/useNavBadges.js` renders them in the sidebar.*

### FR-RPT — Reports & Analytics

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-RPT-01 | The system shall produce: validated registration reports, enlistment results, faculty load summaries, room utilization reports, and document checklist completion reports. | ✅ |
| FR-RPT-02 | Reports shall be semester-scoped and exportable for institutional planning. | ✅ |
| FR-RPT-03 | The system shall reproduce the institution's existing paper instruments — the **Confirmation of Faculty Loading** form (per faculty and consolidated) and the **class scheduling grid/matrix** — as printable documents. | ✅ |
| FR-RPT-04 | The system shall export a **whole-semester data bundle** and a **system parameters / master-data bundle** as single workbooks. | ✅ |
| FR-RPT-05 | Report pages shall refresh **live** when the underlying data changes, without a manual reload. | ✅ |

*Built: `Features/Reports` (`/api/reports/*`, Registrar + Academic Head + School Admin) — all five required reports (validated registrations, enlistment results, faculty load summary, room utilization, document completion), each semester-scoped (default active, selectable) and delivered as JSON or as a ClosedXML **.xlsx export** via `?format=xlsx` (FR-RPT-02). Client `features/reports` at `/reports`: report switcher, semester selector, inline table, per-row actions, and one-click Excel export.*

*FR-RPT-03 built: `Features/Reports/FacultyLoading` mirrors the institution's own **CONF_FACLTY_LOADNG** instrument — `faculty-loading` (list), `faculty-loading/{id}` (one member's loading), `faculty-loading/consolidated` (all members), `faculty-loading/bulk`, plus **PDF** renderings (`consolidated.pdf`, `{id}/pdf`) and `{id}/schedule-grid` for the individual grid. `Features/Reports/RoomGrid` (`GET /api/reports/room-grid-schedule`) renders the **class scheduling grid/matrix** by room. Client `/reports/faculty-load`. FR-RPT-04 built: `Features/Reports/SemesterExport` (`GET /api/reports/semester-export`) bundles a whole term into one workbook, and `Features/Reports/SystemExport` (`GET /api/reports/system-export`, School Admin) exports the setup master data and parameters; audited as `SemesterExported` / `SystemParametersExported`. FR-RPT-05 built: `Features/Reports/Live/ReportsHub` — a **SignalR** hub at `/hubs/reports`; mutation endpoints inject `ReportsBroadcaster` and push a lightweight `reportsChanged` signal, and the open report refetches itself. The broadcaster is deliberately fire-and-forget and never throws, so live refresh can never fail a committed write.*

### FR-AUD — Audit Trail

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-AUD-01 | The system shall keep accountability logs (audit trail entries) for security- and data-relevant actions: registrations, checklist changes, slot requests/approvals, schedule generation/overrides/publication, user management actions, and notification dispatches. | ✅ |

*Built: the audit-trail mechanism is complete and every mutating action that currently exists writes to it. Server — `Domain/AuditEntry` + `AuditAction` enum (persisted as string, so the member names are the stable contract — appended, never renumbered), `Common/Auditing/AuditLog` (resolves the actor from the request, staged in the same transaction as the action it records; `RecordAnonymous` covers the account-less public flows), `AuditEntries` table via `AddAuditTrail` migration, and a School-Admin-only read slice `Features/Audit/GetAuditTrail` (GET `/api/audit`, newest-first, action filter). Client — `features/audit` accountability table with action filter and refresh, wired at `/audit`.*

*The trail now spans **64 action categories**, grouped as: authentication and account lifecycle (registration, login success/failure, profile, password change, password reset, email change, 2FA enable/disable/challenge/failure, student account provisioning); SIS and enrollment (student registered, registration confirmed, term activation requested/validated, student number assigned, account linked, pre-authorized, pre-enrollment imported); admission requirements (created/updated/archived) and checklist changes; academic setup and curriculum (school year, semester, building, room, class section, curriculum, subject, plus archive/restore for subjects, curricula, and schedules); faculty load and preferences; scheduling (generated, generation failed, overridden, finalized, reopened, published); enlistment (slot requested/approved/rejected, section capacity overridden); enrollment-cycle stage changes; system parameters (capacity cap, time slots, load limits, soft-constraint weights, parameters updated); exports (semester, system parameters); notification dispatches; and the rating survey (invitations sent, response submitted).*

### FR-EVAL — ISO/IEC 25010 Rating Survey (research instrument)

| ID | Requirement | Status |
|----|-------------|:------:|
| FR-EVAL-01 | The system shall dispatch the **ISO/IEC 25010 evaluation questionnaire** to selected respondents by email, each with a unique single-use link. | ✅ |
| FR-EVAL-02 | Respondents shall answer **without signing in**, on a 5-point Likert scale across the ISO 25010 characteristics, in **English and Filipino**. | ✅ |
| FR-EVAL-03 | Each invitation shall be answerable **exactly once**, and results shall be aggregated into weighted means for the study. | ✅ |
| FR-EVAL-04 | Dispatch and results shall be restricted to the **Super Admin** — they are research data, not institutional operations. | ✅ |

*Built: `Features/Survey` (migration `AddRatingSurvey`). `Domain/SurveyInvitation` stores only a **hash** of the opaque link token; `Domain/SurveyResponse` stores the submitted ratings as JSON. The public taker is `GET/POST /api/survey/{token}` (`AllowAnonymous`) — the token in the URL is the identity, so respondents need no account; a completed invitation returns 409 on a second submission (FR-EVAL-03), and the server keeps only known question keys so a tampered payload cannot store junk. `SurveyContent` holds the fixed **bilingual (English/Filipino)** instrument — the ISO 25010 characteristics, their hints, and the 1–5 scale — served to both the taker and the results view so the two can never drift. The admin side is `/api/admin/survey/*` (**Super Admin only**, FR-EVAL-04): send invitations, list them, and read aggregated results. Audited as `SurveyInvitationsSent` / `SurveyResponseSubmitted`. Client: public `/survey/:token` and `/survey-admin`.*

*This module exists to collect the §8 evaluation data (45 respondents, weighted-mean interpretation) inside the system under test, rather than on paper or a third-party form.*

---

## 4. Non-Functional Requirements (ISO/IEC 25010:2023)

The system will be evaluated by 45 purposively sampled respondents (30 students, 10 faculty, 5 administrative staff) using a 5-point Likert ISO 25010 questionnaire across six dimensions. **Target: overall weighted mean ≥ 4.00 ("Very Good")** — the empirically established threshold for sustainable adoption.

**Per-point status uses the same legend:** ✅ Done · 🟡 Partial · ⬜ Planned.

### NFR-1 Functional Suitability
- ✅ Generated schedules must have **zero hard-constraint violations** (100% hard-constraint satisfaction; benchmark systems fulfill ~78% of soft constraints). — enforced by construction: `SearchState.IsConsistent` rejects any assignment that would break a hard constraint.
- ✅ Automated outputs (schedules, enrollment confirmations, capacity counts) must be accurate. — schedule output is accurate, confirmation emails are sent, and enlistment capacity counts are transactional (`EnrolledCount` under optimistic concurrency + DB CHECK).

### NFR-2 Performance Efficiency
- ✅ Schedule generation completes in practical time for STI Alaminos-scale data (dozens of sections; benchmark: comparable engines flagged when generation exceeded 30 s). — most-constrained-variable ordering plus **wall-clock and search-step budgets** bound the search; both budgets are staff-tunable in system parameters (default 20 s), so the operational ceiling is a configuration decision rather than a code constant.
- 🟡 The system remains responsive under concurrent multi-user access during peak enrollment periods. — the enlistment slice is built; formal load testing is still to be run.
- ✅ Real-time capacity counts must be consistent under concurrent enlistment (transactional enforcement of the 40-slot cap). — optimistic concurrency (`Section.RowVersion`) + DB CHECK constraint; a concurrent-approval race test confirms exactly one approval wins the last seat.

### NFR-3 Usability
- ✅ Role-differentiated interfaces tailored to each user type's functional needs. — RBAC-filtered navigation (`features/shell/nav.js`) shows each role only its functions.
- ✅ Student-facing modules optimized for minimal cognitive load and intuitive navigation. — auth, shell, live journey dashboard, document checklist, enlistment (cards with availability badges and one-click requests), and My schedule are all built to this standard; no student placeholder pages remain.
- ✅ Mobile-responsive web interface (accessible via web and mobile browsers). — responsive shell with off-canvas sidebar and mobile breakpoints.
- ✅ Users can find and understand functions without training. — a top-bar **search** over everything the signed-in role may open, per-item descriptions carried in the nav config, an **enrollment-stage ticker** telling every user where the term stands, role-scoped **badges** for outstanding work, a `/help` "how SEN-GEN works" page, and a `/settings` page for application preferences. Shared **table controls** (`features/shell/useTableControls.js`) give every list the same search / sort / filter / paging behaviour, so a screen learned once is every screen learned.

### NFR-4 Reliability
- ✅ Data integrity through transactional database operations. — EF Core `SaveChanges` unit-of-work throughout; the enlistment cap adds optimistic concurrency and a database CHECK constraint.
- ⬜ Reliable availability during peak enrollment periods (IIS-hosted application layer). — deployment/hosting not yet exercised.
- ✅ ETL import must not corrupt or duplicate records (duplicate detection on load). — duplicates are detected by email and by name+birthdate (in-file and against the DB); a re-import of the same workbook was verified to load nothing twice.

### NFR-5 Maintainability
- ✅ Modular, component-based architecture: ASP.NET Core dependency injection; React component hierarchy; **vertical slices per feature** so features can be built, tested, and accepted independently (FDD). — established: `Features/*` slices server-side, `src/features/*` client-side.
- ✅ EF Core code-first migrations keep schema consistent and evolvable. — 32 code-first migrations track every schema change to date (auth, scheduling, academic setup, curriculum, class sections, faculty load, subject hours, audit, registration, enlistment, notifications, archiving, enrollment stage, room kinds and subject delivery, schedule finalization, official student numbers, requirement catalog, constraint weights, time-slot allowability, enrollment and engine parameters, two-factor auth, rating survey).
- ✅ Behaviour that varies by institution is **data, not code**. — the requirements checklist, the teaching time grid, seat caps, unit ceilings, faculty load limits, and the engine's soft weights and budgets are all editable rows rather than constants, so adapting SEN-GEN to a policy change is a screen edit rather than a redeployment.

### NFR-6 Portability
- ✅ Cross-platform runtime (ASP.NET Core) and component-based front-end (React) working across diverse device and browser configurations.

### NFR-7 Security & Compliance (cross-cutting)
- ✅ **RA 10173 (Data Privacy Act of 2012)**: secure handling of student and faculty personal data; passwords hashed; least-privilege RBAC; audit trail. — passwords hashed, least-privilege RBAC on every endpoint, and the audit trail (FR-AUD) records every listed action category.
- ✅ Authentication middleware at the application layer; role-based middleware validation on every request. — JWT authentication + `RequireRole` authorization applied across all slices (FR-AUTH-08).
- ✅ Account-takeover resistance on the public, account-less flows. — account linking requires **three matching facts** and there is deliberately no auto-link at sign-up; every one-time credential (password reset, email confirmation, 2FA code, survey link) is stored **hashed**, single-use, and expiring; forgot-password answers identically for known and unknown addresses so it cannot enumerate accounts; provisioned student passwords are temporary and must be changed at first sign-in; 2FA is available for any account.
- ✅ No financial data is stored in the system (out of scope). — holds by design.

---

## 5. Data Requirements (core entities)

Derived from the paper's data inputs and document analysis (SIS, Room Utilization Report, Class Scheduling Grid/Matrix, Confirmation of Faculty Loading, Student Master List, Registration records, Subjects & Units records):

- **User** (role, credentials, status, forced-password-change flag, two-factor state)
- **Student profile / SIS registration record** (structured PSGC address, T&C acknowledgment timestamp, SEN-GEN registration number, official student number, pre-authorization, linked account)
- **Admission requirement** (code, name, sort order, active flag) + **requirement × program** scoping
- **Registration document / submission status** (checklist row per student per requirement code)
- **Term activation** (returning student's request to join a term; pending → validated)
- **Subject / Course** (units, weekly contact hours, program, curriculum, term/year level, prerequisites/co-requisites, **delivery mode**: lecture / laboratory / lecture–laboratory)
- **Class section / student block** (program/course, year level, section, curriculum, per semester — the cohort a curriculum's subjects are delivered to)
- **Section** (subject, capacity ≤ institutional cap, enrolled count, row version, semester)
- **Faculty profile** (load limits, availability/time preferences)
- **Faculty load assignment** (faculty × subject × class section × semester; a subject-for-a-class is exclusive to one faculty)
- **Room** (capacity, **room kind**: lecture room / computer laboratory / kitchen laboratory)
- **Building** (groups teaching rooms)
- **Academic calendar / School year / Semester** (active-term awareness, **enrollment stage**)
- **Time slot** (the allowable teaching grid)
- **Schedule assignment** (section × **class component** × room × time slot × faculty; manual-override, finalized, and published flags)
- **Slot request / Enlistment transaction** (status: requested → approved/rejected)
- **Notification** (in-app notice per user) and **notification dispatch log**, **Audit trail entry**
- **System parameters** (singleton: unit-load limits, allowable time slots, section capacity cap, enlistment switch and unit ceiling, soft-constraint weights, engine budgets)
- **Survey invitation + survey response** (ISO 25010 evaluation data, FR-EVAL)

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
| **Client (Front-End)** | React JS (Vite), React Router, FullCalendar | Role-differentiated interfaces; REST over HTTPS plus a SignalR subscription for live report refresh |
| **Application (Back-End)** | ASP.NET Core Minimal APIs + EF Core, SignalR, ClosedXML (.xlsx) and PDF rendering, hosted on IIS | Business logic, CSP scheduling engine, RBAC validation, ETL pipeline, email dispatch, authentication and two-factor middleware, reporting and exports; OpenAPI/Swagger with JWT auth in development |
| **Data** | Microsoft SQL Server | All persistent data: users, documents, SIS records, sections, rooms, schedules, slot transactions, notification logs, audit trail |

### 7.1 Vertical Slice Architecture mapping

Each FDD feature is implemented as a self-contained vertical slice (request → validation → handler → persistence → response), organized under `Features/` on the server and `src/features/` on the client:

```
SENGENSystem.Server/
  Common/            // cross-cutting: persistence (DbContext + migrations), auth (JWT,
                     //   claims elevation, one-time tokens, student provisioning),
                     //   auditing, notifications (email + in-app), validation
  Domain/            // entities and enums shared across slices
  Features/
    Auth/            // Register, Login, TwoFactor, Me, ForgotPassword   (FR-AUTH)
    Profile/         // UpdateProfile, ChangePassword, ChangeEmail, 2FA  (FR-AUTH)
    UserManagement/  // admin account management                        (FR-AUTH-07)
    Documents/       // requirements catalog + checklist + reminders     (FR-DOC)
    Registration/    // digital SIS, term activation, student number     (FR-SIS)
    PreEnrollment/   // ETL .xlsx import + pre-authorization             (FR-PRE)
    AcademicSetup/   // school years, semesters, buildings, rooms, class sections
    Curriculum/      // curricula + subjects                            (FR-SCHED-04)
    FacultyLoad/     // load allocation + time preferences               (FR-FAC)
    Scheduling/      // CSP engine, board overrides, finalize, my schedule
    Enlistment/      // slot selection + approval                        (FR-ENL)
    EnrollmentCycle/ // term stage control                               (FR-CYC)
    Publishing/      // schedule publish/distribute                      (FR-PUB)
    Dashboard/       // semester-aware metrics + transparency            (FR-DASH)
    Analytics/       // room utilization deep-dive                       (FR-DASH)
    Reports/         // reports, faculty loading, room grid, exports, live hub (FR-RPT)
    SystemParameters/// institutional parameters                         (FR-PARAM)
    Notifications/   // in-app notification feed                         (FR-NOTIF)
    Navigation/      // role-scoped nav badges                           (FR-NOTIF-04)
    Audit/           // accountability log read                          (FR-AUD)
    Survey/          // ISO 25010 rating survey                          (FR-EVAL)

sengensystem.client/src/
  features/
    auth/ profile/ users/ documents/ registration/ pre-enrollment/
    academic/ curriculum/ faculty/ scheduling/ enlistment/ enrollment-stage/
    publishing/ dashboard/ analytics/ reports/ parameters/ notifications/
    audit/ survey/ settings/ help/
    shell/           // app layout, role-filtered nav, table controls, nav badges
```

### 7.2 FDD build order (Plan by Feature — dependency-aware)

1. ✅ **Foundation**: user authentication, RBAC, SQL Server schema (Register/Login/Me, Profile, JWT + role authorization, app shell with role-filtered navigation, EF Core migrations).
2. ✅ **Core CSP scheduling engine** — engine + generate/get-schedule API and the Academic-Head generate/review UI; supporting setup (Academic Setup, Curriculum & Subjects incl. weekly hours, Faculty Load allocation); manual overrides via the drag-and-drop Schedule Board (FR-FAC-02); faculty My-schedule view (FR-FAC-05); **faculty time-slot preferences (FR-SCHED-03) and curriculum prerequisite/co-requisite awareness (FR-SCHED-04)**.
3. ✅ **Student-facing enlistment interface** (FR-DOC, FR-SIS, FR-PRE, FR-ENL, FR-PUB) — digital SIS registration, document checklist board + reminders, term activation, schedule publishing, identity link + pre-authorization, **subject enlistment with Registrar slot approvals**, and the pre-enrollment .xlsx ETL import.
4. ✅ **Administrative dashboard and notification modules** — audit trail (FR-AUD) covering every listed category; user management (FR-AUTH-07); the full email/notification subsystem (FR-NOTIF); **live semester-scoped dashboard metrics (FR-DASH) and exportable reports (FR-RPT)**.
5. ✅ **Institutional hardening and evaluation** — enrollment-cycle stage control (FR-CYC); configurable admission-requirement catalog (FR-DOC-06/07); School-Admin system parameters incl. engine tuning (FR-PARAM, FR-SCHED-12); room-kind and lecture–laboratory scheduling (FR-SCHED-09/10) and draft finalization (FR-SCHED-11); account-lifecycle self-service — password reset, email change, opt-in 2FA, SIS-provisioned student logins (FR-AUTH-10…13); in-app notifications and nav badges (FR-NOTIF-03/04); the institution's own reporting instruments and bulk exports (FR-RPT-03/04) with live SignalR refresh (FR-RPT-05); and the built-in ISO 25010 rating survey (FR-EVAL).

---

## 8. Acceptance & Evaluation Criteria

- Each feature is documented with acceptance criteria derived from stakeholder interviews and ISO 25010 dimensions, and is validated/accepted by stakeholders (FDD Phase 5).
- Unit tests are written for all scheduling engine functions and API endpoints prior to integration.
- Post-deployment: ISO 25010 questionnaire (content-validated by a panel of 3 IT experts) administered to the 45 respondents; scores computed by average weighted mean and interpreted: 4.50–5.00 Excellent, 3.50–4.49 Very Good, 2.50–3.49 Good, 1.50–2.49 Fair, 1.00–1.49 Poor.
- Primary success benchmark: **overall weighted mean ≥ 4.00**.
