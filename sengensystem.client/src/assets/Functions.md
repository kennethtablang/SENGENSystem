# SEN-GEN — Functions List

**What the system can do, module by module, and who can do it.**

| | |
|---|---|
| **System** | SEN-GEN: Student Enrollment and Constraint Satisfaction Problem–Based Generative Class Scheduling Engine for STI Alaminos |
| **Companion document** | `RequirementsSpecifications.md`, which holds the formal requirement IDs (FR-xxx). This file is the plain-language list of functions. |
| **Derived from** | The server feature slices (`SENGENSystem.Server/Features`), the client screens (`sengensystem.client/src/features`), and the role-based sidebar (`features/shell/nav.js`) as they exist in the repository |
| **Date** | 27 November 2026 |

---

## Contents

1. [What SEN-GEN does](#1-what-sen-gen-does)
2. [Users and what each can do](#2-users-and-what-each-can-do)
3. [Public functions (no sign-in needed)](#3-public-functions-no-sign-in-needed)
4. [Account and personal functions (every signed-in user)](#4-account-and-personal-functions-every-signed-in-user)
5. [Enrollment functions](#5-enrollment-functions)
6. [Enlistment functions](#6-enlistment-functions)
7. [Scheduling functions](#7-scheduling-functions)
8. [Academic setup functions](#8-academic-setup-functions)
9. [Administration, reports and analytics](#9-administration-reports-and-analytics)
10. [Research evaluation (ISO/IEC 25010 survey)](#10-research-evaluation-isoiec-25010-survey)
11. [Automatic functions the system does by itself](#11-automatic-functions-the-system-does-by-itself)
12. [Rules the system enforces](#12-rules-the-system-enforces)
13. [What SEN-GEN does not do](#13-what-sen-gen-does-not-do)
14. [Appendix A: Pages and their endpoints](#appendix-a-pages-and-their-endpoints)
15. [Appendix B: Endpoint reference](#appendix-b-endpoint-reference)

---

## 1. What SEN-GEN does

SEN-GEN is a web platform that digitizes the first three of STI Alaminos' four enrollment stages and builds conflict-free class schedules automatically.

| Enrollment stage | Handled by SEN-GEN? |
|---|---|
| 1. Document submission | ✅ Digital requirements checklist (papers are handed in person and recorded by the Admission Office) |
| 2. Registration (Student Information Sheet) | ✅ Fully digital SIS |
| 3. Subject enlistment | ✅ Online seat requests with live slot counts and Registrar approval |
| 4. Tuition payment | ❌ Out of scope; stays with the cashier |

On top of enrollment, SEN-GEN provides:

- a **CSP scheduling engine** that places classes into rooms and time slots with zero room, faculty or section clashes;
- a **drag-and-drop schedule board** for manual adjustments, with live conflict checks;
- **faculty load management** against each member's unit ceiling;
- **schedule publishing** to students and faculty;
- a **semester-aware dashboard**, reports, PDF and Excel exports;
- **email and in-app notifications**, a full **audit trail**, and a built-in **ISO/IEC 25010 rating survey** for the study.

---

## 2. Users and what each can do

SEN-GEN uses role-based access control. Every screen and every API endpoint checks the user's role.

| Role | Main functions |
|---|---|
| **Student** | Track their document checklist, view and correct their SIS, enlist in subjects, view their schedule, download their prospectus and certificate of registration |
| **Faculty Member** | View their teaching schedule with section seat counts, receive notifications |
| **Admission Officer** | Manage the requirements catalog and document checklists, validate term activations, assign official student numbers, pre-authorize students, evaluate transferees |
| **Registrar** | Confirm SIS registrations, import pre-enrollment lists, approve enlistment requests, record academic history, publish schedules, run reports, open or close term activation |
| **Academic Head** | Allocate faculty loads, generate, review, finalize and adjust schedules, manage subjects, curricula and class sections, control the enrollment stage, evaluate transferees, manage user accounts |
| **School Admin** | Everything above (except the research survey), plus school years, semesters, buildings, rooms, system parameters, email outbox and audit trail |
| **Super Admin** | Everything a School Admin can do, plus the ISO/IEC 25010 rating survey (choose recipients, send, view results) |

> **School Admin elevation:** the School Admin automatically gets every institutional role's permissions, so the admin sidebar shows every function. The Super Admin also gets the survey screens, which are hidden from the School Admin.

---

## 3. Public functions (no sign-in needed)

| Function | Screen | What it can do |
|---|---|---|
| **Sign in** | `/login` | Email and password login. Repeated failed attempts are throttled. If two-factor authentication is on, asks for the 6-digit emailed code and can resend it. |
| **Self-register an account** | `/register` | Create an account with name, email and password. Requires accepting the terms and conditions (timestamp saved). Rejects duplicate emails. Validates every field. |
| **Forgot / reset password** | `/forgot-password`, `/reset-password` | Emails a single-use, time-limited reset link. The reply is the same whether or not the email exists, so it can't be used to check which accounts exist. |
| **Confirm email change** | `/confirm-email` | Completes an email change from the link sent to the new address. |
| **Digital Student Information Sheet (SIS)** | `/register-sis` | New students and transferees fill in the full SIS: personal details, structured Philippine address (region → province → city/municipality → barangay, works offline from a bundled dataset), guardian, program and student type. Text is stored in ALL CAPS. Requires terms acceptance. On submit, the system:<br>• issues a registration number;<br>• creates a student login with a temporary password (changed at first sign-in);<br>• builds the student's document checklist from the requirements catalog;<br>• sends a confirmation email.<br>Refused while the term is in *Preparation* or *Enrollment closed*. |
| **Term activation (returning students)** | `/term-activation` | A returning student identifies themselves with their **official student number + last name** (falls back to the registration number). The system shows the year level and term they are activating into, and the student confirms both before the request is filed. Only available while term activation is open. |
| **Rating survey (by link)** | `/survey/:token` | Answer the ISO/IEC 25010 questionnaire from an emailed single-use link, in English or Filipino, on a 1–5 scale. Each link can be answered once. |
| **Terms and Privacy pages** | `terms.html`, `privacy.html` | Full terms-and-conditions and privacy policy (RA 10173), also shown in the consent pop-up. |

---

## 4. Account and personal functions (every signed-in user)

| Function | Screen | What it can do |
|---|---|---|
| **Dashboard** | `/` | Role-specific home page:<br>• **Staff:** live semester metrics (see §9);<br>• **Student:** enrollment journey stepper (link account → documents → clearance → transferee evaluation if any → enlistment) with "enrolled for the term" status;<br>• **Faculty:** teaching-week summary. |
| **Enrollment stage ticker** | Top bar | Shows everyone the active term and its current stage (Preparation → Registration → Document submission → Subject enlistment → Enrollment closed). |
| **Notifications** | Bell icon, `/notifications` | In-app notices (approvals, schedule changes, evaluations, survey invites). Mark one or all as read. |
| **Sidebar work counters** | Sidebar badges | Live counts of outstanding work for the user's role: unread notices, pending approvals, registrations awaiting confirmation, term activations to validate, transferee evaluations, open survey. All scoped to the active term. |
| **Quick search** | Top bar | Finds any screen the user is allowed to open. |
| **Profile** | `/profile` | • Edit name.<br>• Change password.<br>• Change email (confirmed by a link to the new address).<br>• Turn two-factor authentication on (confirmed with an emailed code) or off (requires password).<br>• **Students:** view their student record card: official student number (or *Not yet assigned*), registration number, program, year level. |
| **Forced first-login password change** | Automatic | A student whose account was created from the SIS must replace the temporary password before any other screen opens. |
| **Settings** | `/settings` | Per-device preferences: theme (light / dark / system), density (comfortable / compact), collapsed sidebar, accent color, text size, behavior options (for example "Confirm heavy actions"), notification and schedule display preferences. |
| **Help & about** | `/help` | How enrollment works, who does what, FAQ, about the system. |
| **Evaluation survey (signed in)** | `/survey` | Appears only while the user has an open survey invitation; answer it without the emailed link. |

---

## 5. Enrollment functions

### 5.1 Document requirements (checklist)

**Screen:** `/documents` · **Roles:** Admission Officer, Registrar, School Admin (manage); Student (view own)

- View every enrollee's checklist for the active term, expandable per student.
- Filter by complete or incomplete; search widens past the active term.
- Set each paper's status (for example *not submitted*, *original*, *photocopy*, *certificate of grades*). Records who verified it and when.
- Papers are handed in person and recorded here; nothing is uploaded.
- **Send reminders:** email every incomplete enrollee of the active term the exact list of papers they still owe, or remind one named enrollee.
- **Students** see their own checklist (read-only) to track what the Admission Office has received.

### 5.2 Requirements catalog

**Screen:** Requirements modal on `/documents` · **Roles:** Admission Officer, Registrar, School Admin

- Add, rename, describe, reorder and archive admission requirements (no code change needed).
- Choose which **programs** each requirement applies to (for example, health papers only for HRS/HRA, not ITP).
- Choose which **student types** it applies to: new students (Form 138/137, good moral), transferees (transcript, certificate of transfer), or both (PSA birth certificate, health papers).
- Flag requirements as **required for pre-authorization**; the rest are followed up but don't block enlistment.
- Allow a **certificate of grades** in place of a photocopy (used by the transcript).
- Archiving keeps old checklists intact.

### 5.3 Registration (SIS) management

**Screen:** `/registrations` · **Role:** Registrar

- View the queue of submitted SIS records for the active term; search widens to all terms.
- Open a record and **correct** any field.
- **Confirm** a registration (emails the student).
- See **possible duplicates**: records with the same name and date of birth are flagged, never refused.
- See and override the student's **year level** (audited).
- View the student's document checklist inside the record drawer.
- Download a confirmed student's **Certificate of Registration (COR)** PDF from the record drawer.

### 5.4 My SIS (student self-service)

**Screen:** `/my-registration` · **Role:** Student

- View their own SIS.
- Correct their own names, birth details, mobile number, address and guardian while it is still awaiting Registrar confirmation. Read-only once confirmed.
- Program, student type and email are not student-editable.
- **Link account:** a student with a self-registered account claims their SIS record using three matching facts (student number, SIS email, date of birth).

### 5.5 Term activations

**Screen:** `/term-activations` · **Role:** Admission Officer

- View returning students' activation requests for the active term with official number, last name and year level.
- Flags requests where the year level the student confirmed **disagrees** with the record.
- **Validate** a request, accepting the derived year level or overriding it (for a repeating student or one returning from leave). The student's login is provisioned if needed.

### 5.6 Term activation control

**Screen:** `/term-activation-control` · **Roles:** Registrar, Academic Head (plus School Admin, Super Admin)

- Open or close the public term-activation form for the whole institution.
- Shows how many requests are pending and when the switch was last changed.
- Closing it does not affect requests already filed.

### 5.7 Assign student number

**Screen:** `/assign-student-number` · **Role:** Admission Officer

- Record the **official student number** issued by the separate student-records system against a SEN-GEN registration.
- Numbers are unique across registrations; every assignment is audited.
- Views: *Still to number* (default), *Already numbered*, *All registrations*, with search and whole-queue tallies (numbered / pending / total).

### 5.8 Transferee credit evaluation

**Screen:** `/evaluate-transferee` · **Roles:** Registrar, Admission Officer, Academic Head, School Admin

- Queue of transferees with each one's progress (Pending / In progress / Completed).
- Evaluation sheet: the student's whole curriculum in order; mark each subject **Credited** (recording the previous school's subject and grade) or **To take**.
- "Credit all" / "All to take" shortcuts per term block.
- Live running totals of credited units and the **derived year level**.
- Save in several sittings; **Complete** only when every subject has a verdict. Completing sets the year level (overridable) and opens the student's enlistment.
- **Reopen** for corrections without losing decisions (closes enlistment again until re-completed).
- Print the evaluation as a signable PDF.
- Until the evaluation is completed, a transferee cannot enlist.

### 5.9 Academic records

**Screen:** `/academic-records` · **Roles:** Registrar, Academic Head

- Per-student sheet, read against their own curriculum, recording each subject's result per term: **passed, failed or dropped**. Results only, not grades.
- Save one term at a time; retakes keep every attempt.
- **Import from .xlsx** (with downloadable template) to backfill history in bulk.
- Shows how many students have no history on file (prerequisite checks only apply once a student has some history).
- This record drives prerequisite checks, repeat subjects, "already passed" subjects and year-level advancement.

### 5.10 Curriculum prospectus

**Screen:** `/prospectus` · **Roles:** Registrar, Academic Head, Admission Officer

- Choose a program/curriculum and year level (or the whole program) and download a printable **PDF prospectus**: subjects term by term with units, lecture/lab split, prerequisites and running totals.
- A student's **transferee evaluation sheet** PDF is downloaded from *Evaluate transferee*, and their **certificate of registration** PDF from the *Registration (SIS)* record drawer.

### 5.11 Pre-enrollment import

**Screen:** `/pre-enrollment` · **Role:** Registrar

- Download the .xlsx template.
- Upload a prospective-student list. The system extracts, validates (required fields, email/date formats, valid values), transforms (names, student numbers, checklists) and loads it.
- Detects duplicates by email and by name + birthdate, both inside the file and against the database.
- Row-by-row report: valid rows load even when other rows fail. Re-importing the same file is safe.

### 5.12 Pre-authorization

**Screen:** `/pre-authorization` · **Roles:** Admission Officer, Registrar, School Admin

- Queue of the active term's students with **Ready / Blocked / Cleared** state and submitted/total paper counts.
- **Authorize** a student for online slot selection. Requires a Registrar-confirmed SIS and the papers flagged *required for authorization*. The reason for any block is named.
- **Bulk authorize:** a selection mode to clear many students at once. Blocked students are skipped and listed with their reasons; the rest are cleared together.
- **Revoke** a clearance.
- Search widens past the active term.

---

## 6. Enlistment functions

### 6.1 Subject enlistment (student)

**Screen:** `/enlistment` · **Role:** Student

- **My plan for the term:** a checklist of exactly the subjects the student owes. This is their program's curriculum at their year level for this term, minus credited or passed subjects, plus failed subjects due for retake. It shows units, how many classes are published for each, and the request status. Subjects with no published class yet are flagged.
- Browse **published** sections for those subjects with day, time, room, faculty and **live seat counts**.
- "Show every section" switch to widen the browse (it never widens what can be requested).
- **Request a seat.** The server refuses when:
  - the student is not linked, confirmed and pre-authorized;
  - a transferee's evaluation is not complete;
  - enlistment is closed or the term is not in *Subject enlistment*;
  - the subject is not in their plan;
  - a prerequisite is not passed (co-requisites can be taken together in the same term);
  - the section is full (maximum 40);
  - times overlap another requested or approved class;
  - they already have a request for that subject;
  - it would exceed the per-student unit ceiling.
- **Cancel** a pending request.
- See whether they are **Enrolled for the term** (every owed subject holds an approved seat).

### 6.2 My subjects

**Screen:** `/my-subjects` · **Role:** Student

- View the subjects their year level takes.
- Download their own **curriculum prospectus PDF** (credited subjects shaded for an evaluated transferee).
- Download their **Certificate of Registration (COR) PDF**: approved subjects with schedule, room, faculty, total units, enrollment status, and an amendment flag on any class changed after publishing.

### 6.3 Enlistment approvals

**Screen:** `/approvals` · **Role:** Registrar (plus Academic Head, School Admin for capacity override)

- Queue of pending seat requests for the active term.
- **Approve** or **reject** one request; the student is emailed and notified.
- **Bulk approve:** a selection, a student's whole outstanding load, or the entire queue. Every request still passes every check.
- **Drop** an already-approved seat (releases it).
- **Capacity override:** raise a full section's seat cap to let a request through, up to the institutional cap and the smallest room the section meets in. Audited, and staff are notified.
- Prerequisites and co-requisites are re-checked at approval time.
- **Seat-count check:** finds sections whose stored seat count disagrees with their live approved requests and lets staff correct them deliberately.
- Each student shows their "enrolled for the term" status.
- Seats are protected from double-booking under heavy load, and a database rule makes it impossible to go over capacity.

---

## 7. Scheduling functions

### 7.1 Faculty load

**Screen:** `/faculty-load` · **Roles:** Academic Head, School Admin

- Per semester, assign subjects to faculty members **per class section** (for example CS301 → BSCS 3-A).
- A subject + section pair can belong to only one faculty member; already-assigned rows are shown with their holder.
- Running units shown against each member's **maximum load**; going over the ceiling is refused.
- Load bar and over-ceiling indicator per member.
- **Time preferences:** record each member's preferred teaching windows, which the engine tries to honor.
- This allocation is what the scheduling engine places.

### 7.2 Generate schedule (CSP engine)

**Screen:** `/scheduling/generate` · **Roles:** Academic Head, School Admin

- Run the engine for a semester. It places every allocated class into a room and time slot.
- **Hard rules (never broken):**
  - no room double-booking;
  - no faculty double-booking;
  - no overlapping classes for the same section;
  - room capacity respected;
  - faculty load ceilings respected;
  - correct **room kind** (lecture room, computer lab or kitchen lab).
- **Soft goals (optimized):**
  - faculty time preferences;
  - fewer idle gaps between classes, for both students and faculty;
  - right-sized rooms.
- Load balance is set by the Academic Head's faculty load allocation, which the engine places as given.
- Lecture–laboratory subjects are scheduled as **two separate meetings**.
- **Curriculum-aware:** checks prerequisite ordering before solving and keeps co-requisites offered together.
- **Starts a new term from the faculty load alone:** creates the needed sections automatically.
- **Generation settings:** set when the teaching day opens (shows how many periods are trimmed), plus soft-goal weights and time/step budgets.
- **Arrangement number:** re-enter a past arrangement number to reproduce the same timetable from unchanged inputs.
- Progress overlay while thinking, then a placement summary and optimization report.
- **Replaces** the semester's timetable. If a published schedule would be discarded, it asks twice and states how many classes, sections and enlisted students are affected.
- **Finalize** a reviewed draft, which locks it against regeneration and board edits so the Registrar can publish it.
- **Reopen** a finalized schedule for further changes.
- See what the engine optimized against on the last run.
- Typical runs finish well under 30 seconds. The engine is rule-based, with no machine learning.

### 7.3 Review & overrides

**Screen:** `/scheduling/review` · **Role:** Academic Head

- Inspect the generated schedule grouped by cohort, with Draft / Finalized (ready to publish) / Published status.
- This page is read-only. Manual changes are made on the **Schedule board** (§7.4), and finalizing and reopening on **Generate schedule** (§7.2).

### 7.4 Schedule board (manual scheduling)

**Screen:** `/scheduling/board` · **Roles:** Academic Head, School Admin

- Calendar-style, drag-and-drop weekly timetable.
- Drag allocated subjects from the **Assigned Subjects** pool onto the grid; move, resize or delete blocks.
- Every change is checked live for room, faculty and section clashes and for room kind. A refused change states the reason.
- **"All rooms" view:** dropping a subject opens a **room picker** for that exact slot. Unavailable rooms are greyed out with the reason, and the best-fitting room is listed first.
- **Weekly Hours Tracker:** plotted vs. required hours per subject × faculty × section.
- Color-coded blocks per subject; hover tooltips.
- **Amendments:** changing an already-published class asks for confirmation, flags the class as amended (dashed outline), and notifies the faculty member and every student holding a seat by bell notice and email, stating the move in plain words.
- Locked while the schedule is finalized or the semester archived.

### 7.5 Schedule publishing

**Screen:** `/publishing` · **Roles:** Registrar, School Admin

- Draft vs. published statistics for the semester.
- **Preview** what a publish will do (classes, faculty and students to notify) before confirming.
- **Publish** (only a finalized schedule). Emails every assigned faculty member and confirmed student.
- View the published schedule **By class / By week / By day**, with day and cohort filters.

### 7.6 My schedule

**Screen:** `/schedule` · **Roles:** Student, Faculty Member

- Read-only weekly timetable for the active semester, as a calendar plus a day-by-day list.
- **Faculty:** their assigned classes with per-section enrollment counts.
- **Students:** the classes they hold approved seats in.
- Amended classes are flagged.

---

## 8. Academic setup functions

| Function | Screen | Roles | What it can do |
|---|---|---|---|
| **School years** | `/school-years` | School Admin | Create, edit and delete school years with key dates; set the **active** school year. |
| **Semesters** | `/semesters` | School Admin | Create, edit and delete semesters; **activate** one (exactly one is active at a time and every queue follows it); **archive** a finished term (schedule frozen, data kept for reports) or unarchive it. The active term can't be archived. |
| **Buildings** | `/buildings` | School Admin | Create, edit and delete campus buildings that group rooms. |
| **Rooms** | `/rooms` | School Admin | Create, edit and delete rooms with capacity and **room kind** (lecture room, computer laboratory, kitchen laboratory). |
| **Class sections** | `/class-sections` | Academic Head, School Admin | Create, edit and delete student blocks (course · year level · section) per semester, linked to the curriculum they follow. |
| **Subjects & curriculum** | `/subjects` | Academic Head, School Admin | **Subjects:** create and edit code, title, units, hours, delivery (lecture only / laboratory only / lecture–laboratory), required lab kind and prerequisites; archive, restore or delete.<br>**Curricula:** create and edit program curricula, place subjects by year level and term, set the **active** curriculum per program, archive or restore. Continuing students stay pinned to the curriculum of the school year they entered. |

---

## 9. Administration, reports and analytics

### 9.1 Administrative dashboard (staff)

**Screen:** `/` · **Roles:** staff

- Every number is scoped to the **active semester** by default, with a semester selector.
- Registration / application volume and status funnel.
- Document completion rate.
- Pre-authorization counts.
- Enlistment statistics: per-section seat counts and fill %.
- Room utilization (hours per week against the teaching window).
- Faculty load distribution: load bars with Balanced / Above average / Below average / Overloaded / Unassigned flags.
- **Scheduling transparency** (Academic Head, Registrar, School Admin):
  - counts of classes placed by the CSP engine vs. manual overrides, and how many are published or amended after publishing;
  - the hard rules every placement satisfies and the soft goals the engine optimizes;
  - a filterable table of every class (subject, cohort, day and time, room, faculty, placed by, status).

### 9.2 Reports & analytics

**Screen:** `/reports` · **Roles:** Registrar, Academic Head, School Admin

- Five reports, each semester-scoped and exportable to **Excel (.xlsx)**:
  1. validated registrations;
  2. enlistment results;
  3. faculty load summary;
  4. room utilization;
  5. document checklist completion.
- **Live refresh:** an open report updates automatically when the data changes.
- **Semester export:** the whole term's data in one workbook.
- **System export** (School Admin): master data and parameters in one workbook.
- **Room grid schedule:** the class scheduling grid/matrix by room.

### 9.3 Room utilization analysis

**Screen:** `/analytics/room-utilization` · **Roles:** Registrar, Academic Head, School Admin

- Ranks every room by utilized hours against the teaching window.
- Flags **critically underutilized** rooms to support room reallocation.
- Excel export.

### 9.4 Faculty load reports

**Screen:** `/reports/faculty-load` · **Roles:** Registrar, Academic Head, School Admin

- Reproduces the school's own **Confirmation of Faculty Loading** form:
  - per faculty member, consolidated (all members), or in bulk;
  - as **PDF** (individual and consolidated), with program-head sign-off names.
- Per-member **grid schedule** workbook and an all-faculty grid-schedules workbook.

### 9.5 User management

**Screen:** `/users` · **Roles:** Academic Head, School Admin

- List and search accounts across all roles.
- **Create** an account with any role.
- **Edit** name, email and role.
- **Deactivate / reactivate** an account. The last active School Admin can't be deactivated.
- **Reset** a user's password.

### 9.6 System parameters

**Screen:** `/parameters` · **Role:** School Admin

- **Enlistment governance:**
  - institutional section seat cap (default 40);
  - enlistment open/closed switch;
  - maximum units per student per term;
  - minimum section enrollment (advisory).
- **Institution:** the institution or branch name printed on official documents (faculty-loading confirmation, prospectus, transferee evaluation, registration form).
- **Faculty-loading memo:**
  - laboratory teaching-credit factor (teaching hours = lecture hours + laboratory hours × factor);
  - the **Program Head** for each program, who signs the "THRU" line of the faculty-loading form (the Academic Head signs when none is set).
- **Scheduling engine budgets:** time and step limits for one generation run.
- **Allowable time slots:** create, edit and delete periods for the engine and the board. A slot used by a published schedule can't be changed.
- **Faculty load limits:** each member's maximum units, edited in place.
- The engine's soft-goal weights and the teaching-day start time are set in the **Generation settings** panel on *Generate schedule* (§7.2), not here.
- Lowering a value never silently rewrites records. For example, sections already above a lowered seat cap are listed back to the admin.

### 9.7 Enrollment stage control

**Where:** Stage modal from the top-bar ticker · **Roles:** Academic Head, School Admin

- **Advance** the active term one stage forward, or **set** any stage to correct a mistake.
- The stage is enforced: the SIS only accepts submissions in the right stages, and seat requests only in *Subject enlistment* while enlistment is open.

### 9.8 Email outbox

**Screen:** `/outbox` · **Role:** School Admin

- See every queued email, whether delivered or permanently failed, answering "did that student get the email?".
- **Retry** one failed email, or **retry all failures** after a mail outage.

### 9.9 Audit trail

**Screen:** `/audit` · **Role:** School Admin

- Newest-first accountability log with an action filter. It covers 64+ action types:
  - logins and account changes;
  - SIS, registrations, term activations, student numbers, pre-authorization and imports;
  - requirements and checklists;
  - academic setup and curriculum changes;
  - faculty load;
  - schedule generation, overrides, amendments, finalize, publish;
  - seat requests, approvals and capacity overrides;
  - stage changes;
  - parameter changes;
  - exports;
  - notification dispatches;
  - survey activity.

---

## 10. Research evaluation (ISO/IEC 25010 survey)

**Screens:** `/survey-recipients`, `/survey-admin` · **Role:** Super Admin only

- **Choose recipients:** pick individual users (or whole roles as a shortcut) and see each person's invite status.
- **Send** the survey as an in-app bell notice and/or an emailed single-use link, with an optional personal note.
- **Remind** people who haven't answered (a fresh link replaces the old one).
- **Withdraw** an invitation.
- **Collection window:** open or close collection and set the response goal.
- **Results:** overall weighted mean and per-characteristic scores with verbal interpretation (Usability headlined), plus respondents' comments.
- **Export** raw responses to CSV (opens correctly in Excel, including Filipino text).
- Respondents answer in **English or Filipino** on a 5-point scale, once per invitation.

---

## 11. Automatic functions the system does by itself

| Function | What happens |
|---|---|
| **Student account provisioning** | A SIS submission or validated term activation creates the student's login automatically, emails a temporary password to the SIS address, and forces a password change at first sign-in. |
| **Document checklist seeding** | A new SIS gets one checklist row per active requirement that applies to the student's program and student type. |
| **Year level derivation** | New students start at year 1; transferees get the level their credited units earn; returning students advance by units earned when the school year changes. Staff can override. |
| **Enlistment plan** | Works out each student's owed subjects for the term from curriculum, year level, history and credits. |
| **Enrollment completion** | Works out whether each student is "enrolled for the term". This is computed live, never stored. |
| **Duplicate detection** | Flags SIS records with the same name and birth date. |
| **Emails** | Sent for:<br>• registration and term-activation confirmations;<br>• document reminders;<br>• slot approvals and rejections;<br>• schedule publication;<br>• schedule amendments;<br>• temporary passwords, password resets, email confirmation and 2FA codes;<br>• survey invitations.<br>Emails go through a retrying outbox, so a mail failure never loses data. |
| **In-app notifications** | Bell notices for the same events to signed-in users, plus capacity overrides and transferee evaluation completion. |
| **Live report refresh** | Changes push a signal to open report pages so they refetch. |
| **Audit logging** | Every data-changing action is written to the audit trail in the same transaction. |
| **Term rollover** | Activating a new semester empties last term's queues out of every work list, scopes enlistment and reminders to the new term, and keeps institutional settings unchanged. |

---

## 12. Rules the system enforces

- **Role-based access** on every endpoint; cross-role attempts are refused.
- **Passwords** stored only as salted hashes; login throttling; optional 2FA.
- **Section capacity** of at most 40 (institutional cap), enforced at database level.
- **No time overlaps** in a student's enlistment.
- **Prerequisites** must be passed (co-requisites may be taken together).
- **Transferees** can't enlist before their evaluation is complete.
- **Only finalized schedules** can be published; finalized and archived schedules are locked.
- **Zero hard-constraint violations** in generated and manually edited schedules.
- **Stage and switches:** the enrollment stage, the enlistment switch and the term-activation switch decide what can be filed when.
- **One active semester** at a time; queues follow it, searches can widen past it.
- **SIS data in ALL CAPS**, matching the paper SIS and the records system.
- **Data privacy (RA 10173):** consent captured with timestamps, terms and privacy pages, and no account enumeration on password reset.

---

## 13. What SEN-GEN does not do

- **Tuition payment / assessment.** Stage 4 stays with the cashier.
- **Grades.** SEN-GEN records only passed / failed / dropped; grades stay in the student-records system.
- **Document upload.** Papers are handed in person and recorded on the checklist.
- **Machine learning.** The scheduler is deterministic and rule-based.
- **Official student number issuance.** Numbers come from the separate student-records system and are recorded here.

---

## Appendix A: Pages and their endpoints

Every page in SEN-GEN, grouped as in the sidebar. For each page: its route, who can open it, its purpose, the API endpoints it calls, and a complete description of what it does.

> **School Admin** opens every page except the two Super Admin survey pages and the personal *My schedule*. **Super Admin** opens every page. Endpoint details are in [Appendix B](#appendix-b-endpoint-reference).

### A.1 Public pages (no sign-in)

#### A.1.1 Sign in

| | |
|---|---|
| **Route** | `/login` |
| **Who** | Everyone (public) |
| **Purpose** | Let a user sign in to SEN-GEN. |
| **Endpoints** | `POST /api/auth/login`<br>`POST /api/auth/2fa/verify`<br>`POST /api/auth/2fa/resend` |

**Description.** The user enters their email and password. If the credentials are correct and two-factor authentication is off, the system issues a session token carrying the user's role and opens the dashboard. If two-factor authentication is on, the page asks for the 6-digit code emailed to the account and can resend a fresh code. Repeated failed attempts are throttled. Links lead to *Create account* and *Forgot password*. A student signing in for the first time with a system-issued temporary password is sent straight to the forced password change.

#### A.1.2 Create account

| | |
|---|---|
| **Route** | `/register` |
| **Who** | Everyone (public) |
| **Purpose** | Self-register a login account. |
| **Endpoints** | `POST /api/auth/register` |

**Description.** The user enters their name, email and password. Every field is validated (required fields, email format, password strength), and invalid entries are explained next to the field. The terms and conditions must be accepted; the time of acceptance is saved. An email that already has an account is refused with a clear message. Students normally don't need this page, because their account is created automatically from their SIS.

#### A.1.3 Forgot password

| | |
|---|---|
| **Route** | `/forgot-password` |
| **Who** | Everyone (public) |
| **Purpose** | Request a password-reset link. |
| **Endpoints** | `POST /api/auth/forgot-password` |

**Description.** The user enters their email address and the system emails a single-use, time-limited reset link. The page always shows the same reply whether or not the address has an account, so it can't be used to find out which emails are registered.

#### A.1.4 Reset password

| | |
|---|---|
| **Route** | `/reset-password` |
| **Who** | Holder of a valid reset link |
| **Purpose** | Set a new password from a reset link. |
| **Endpoints** | `POST /api/auth/reset-password` |

**Description.** Opened from the emailed link. The user enters and confirms a new password. The link works once and expires after a set time. On success the user can sign in with the new password.

#### A.1.5 Confirm email change

| | |
|---|---|
| **Route** | `/confirm-email` |
| **Who** | Holder of a valid confirmation link |
| **Purpose** | Complete a change of account email address. |
| **Endpoints** | `POST /api/profile/email/confirm`<br>`GET /api/auth/me` |

**Description.** Opened from the link sent to the new email address. Confirming the link switches the account to the new address, proving the user owns that mailbox. The page then refreshes the signed-in user's details.

#### A.1.6 Digital Student Information Sheet (SIS)

| | |
|---|---|
| **Route** | `/register-sis` |
| **Who** | Everyone (public): new students and transferees |
| **Purpose** | File the Student Information Sheet online instead of on paper. |
| **Endpoints** | `POST /api/registration` |

**Description.** A structured form for personal details, birth details, contact details, guardian, program and student type (new or transferee). The address is picked from cascading lists (region → province → city/municipality → barangay) using a bundled PSGC dataset, so it works on a poor connection. Text is converted to ALL CAPS to match the paper SIS. Required fields are checked before submission, and the terms must be accepted (time saved). On submission the system:

- issues a registration number;
- creates the student's login account and emails a temporary password;
- builds the student's document checklist from the requirements that apply to their program and student type;
- sends a confirmation email.

Submissions are refused while the term is in *Preparation* or *Enrollment closed*.

#### A.1.7 Term activation

| | |
|---|---|
| **Route** | `/term-activation` |
| **Who** | Everyone (public): returning students |
| **Purpose** | Let a returning student activate for the new term. |
| **Endpoints** | `POST /api/registration/term-activation/lookup`<br>`POST /api/registration/term-activation` |

**Description.** A two-step form.

1. The student identifies themselves with their **official student number and last name**. The registration number is accepted if no student number was ever issued.
2. The system shows the **year level** and the **term** they are activating into. The student confirms both, and the request is filed for the Admission Officer to validate.

The confirmed year level is kept with the request. The form only works while term activation is open; a closed window shows a plain message. A form left open across a term change is refused rather than filed into the wrong term.

#### A.1.8 Rating survey (by link)

| | |
|---|---|
| **Route** | `/survey/:token` |
| **Who** | Holder of a survey invitation link |
| **Purpose** | Answer the ISO/IEC 25010 evaluation without signing in. |
| **Endpoints** | `GET /api/survey/{token}`<br>`POST /api/survey/{token}` |

**Description.** Opened from the emailed invitation. It shows the questionnaire grouped by ISO/IEC 25010 characteristic, in English or Filipino, each question rated on a 1–5 scale, with an optional comment. Each link can be answered exactly once; a second submission is refused. Only known questions are stored.

### A.2 Overview pages (every signed-in user)

#### A.2.1 Dashboard

| | |
|---|---|
| **Route** | `/` |
| **Who** | All signed-in users |
| **Purpose** | Show each user their semester at a glance. |
| **Endpoints** | `GET /api/dashboard/metrics` (staff)<br>`GET /api/dashboard/scheduling-transparency` (Academic Head, Registrar, School Admin)<br>`GET /api/registration/link` (student)<br>`GET /api/enlistment/mine` (student)<br>`GET /api/scheduling/my-schedule` (student, faculty)<br>`/hubs/reports` (live refresh) |

**Description.** The content depends on the role.

- **Staff** see live metrics for the active semester (or a chosen semester):
  - registration volume and status funnel;
  - document completion rate;
  - pre-authorization counts;
  - enlistment statistics with per-section seat counts and fill %;
  - room utilization;
  - faculty load distribution with Balanced / Above average / Below average / Overloaded / Unassigned flags;
  - **scheduling transparency** (Academic Head, Registrar, School Admin): how many classes the CSP engine placed vs. manual overrides, how many are published or amended, the hard rules and soft goals behind the schedule, and a table of every class filterable by how it was placed.

  The page refreshes itself when data changes.
- **Students** see their enrollment journey as a stepper: link account → documents → clearance → transferee evaluation (transferees only) → enlistment. It shows whether they are enrolled for the term.
- **Faculty** see a summary of their teaching week.

#### A.2.2 Notifications

| | |
|---|---|
| **Route** | `/notifications` (and the bell in the top bar) |
| **Who** | All signed-in users |
| **Purpose** | Read in-app notices. |
| **Endpoints** | `GET /api/notifications`<br>`POST /api/notifications/{id}/read`<br>`POST /api/notifications/read-all`<br>`/hubs/reports` (bell refresh) |

**Description.** Lists the user's own notices, for example:

- seat approvals and rejections;
- schedule publication and amendments;
- capacity overrides;
- transferee evaluation completed;
- survey invitations.

Each notice can be marked as read, or all at once. The bell shows the unread count and the latest notices without leaving the current page. Users only ever see their own notices.

#### A.2.3 Evaluation survey (signed in)

| | |
|---|---|
| **Route** | `/survey` |
| **Who** | Signed-in users with an open survey invitation |
| **Purpose** | Answer the ISO/IEC 25010 evaluation from inside the system. |
| **Endpoints** | `GET /api/survey/mine`<br>`POST /api/survey/mine` |

**Description.** The same questionnaire as the link version, opened while signed in. It appears in the sidebar only while the user still owes a response. Once answered, it disappears.

#### A.2.4 Settings

| | |
|---|---|
| **Route** | `/settings` |
| **Who** | All signed-in users |
| **Purpose** | Set personal display and behavior preferences. |
| **Endpoints** | None (saved on the device) |

**Description.** Preferences stored on the current device only:

- **Appearance:** light / dark / system theme, comfortable or compact density, collapsed sidebar, accent color, text size.
- **Behavior:** for example "Confirm heavy actions".
- **Notifications and schedule:** display options.

#### A.2.5 Profile settings

| | |
|---|---|
| **Route** | `/profile` |
| **Who** | All signed-in users |
| **Purpose** | Maintain one's own account. |
| **Endpoints** | `PUT /api/profile`<br>`PUT /api/profile/password`<br>`POST /api/profile/email/request`<br>`POST /api/profile/2fa/start`<br>`POST /api/profile/2fa/enable`<br>`POST /api/profile/2fa/disable`<br>`GET /api/registration/link` (student) |

**Description.**

- Edit display name.
- Change password (current password required).
- Request an email change; the change takes effect only after the link sent to the new address is confirmed.
- Turn on two-factor authentication: start → enter the code emailed to the account → enabled.
- Turn off two-factor authentication (requires the account password).
- **Students** also see a read-only **Student record** card: official student number (or *Not yet assigned*), registration number, program and year level.

#### A.2.6 Forced first-login password change

| | |
|---|---|
| **Route** | Shown automatically before any other page |
| **Who** | Students whose account was created from their SIS |
| **Purpose** | Replace the system-issued temporary password. |
| **Endpoints** | `PUT /api/profile/password`<br>`GET /api/auth/me` |

**Description.** Blocks every other screen until the student sets their own password. Saving clears the "must change password" flag on the account.

#### A.2.7 Help & about

| | |
|---|---|
| **Route** | `/help` |
| **Who** | All signed-in users |
| **Purpose** | Explain how SEN-GEN works. |
| **Endpoints** | None |

**Description.** Static guide: how enrollment works step by step, who does what (by role), frequently asked questions, and information about the system.

#### A.2.8 App shell (top bar and sidebar)

| | |
|---|---|
| **Route** | Every signed-in page |
| **Who** | All signed-in users |
| **Purpose** | Navigation, current term, and work counters. |
| **Endpoints** | `GET /api/auth/me`<br>`GET /api/nav/badges`<br>`GET /api/enrollment-stage`<br>`POST /api/enrollment-stage` · `POST /api/enrollment-stage/advance` (stage modal, Academic Head / School Admin) |

**Description.**

- **Sidebar:** shows only the pages the user's role may open, with badges counting outstanding work for the active term: unread notices, pending approvals, registrations to confirm, term activations to validate, transferee evaluations, open survey.
- **Enrollment ticker** (top bar): shows everyone the active term and its stage. For the Academic Head and School Admin it opens the **Stage modal**, where they advance the stage one step or set it directly to correct a mistake.
- **Quick search:** finds any page the user may open.

### A.3 Enrollment pages

#### A.3.1 Document requirements

| | |
|---|---|
| **Route** | `/documents` |
| **Who** | Admission Officer, Registrar, School Admin (manage); Student (own checklist) |
| **Purpose** | Track the admission papers each enrollee has handed in. |
| **Endpoints** | `GET /api/documents`<br>`PUT /api/documents/{documentId}`<br>`POST /api/documents/reminders`<br>`GET /api/registration/link` (student)<br>`POST /api/registration/link` (student)<br>Requirements modal: `GET` / `POST /api/requirements`, `PUT` / `DELETE /api/requirements/{id}` |

**Description.**

**Staff** see every enrollee of the active term with an expandable checklist. Search widens to every term.

- Filter by complete or incomplete.
- Set each paper's status (not submitted, original, photocopy, or certificate of grades where allowed). Each change records who verified it and when, and is audited.
- **Send reminders** emails every incomplete enrollee of the active term the exact papers they still owe, or reminds one chosen enrollee.

Papers are handed in person; nothing is uploaded.

The **Requirements** modal manages the catalog:

- add, rename, describe, reorder and archive requirements;
- choose the programs and student types (new / transferee) each applies to;
- flag requirements needed for pre-authorization;
- allow a certificate of grades in place of a photocopy.

**Students** see their own checklist read-only. If their account isn't linked to their SIS yet, they link it here (student number + date of birth, with matching email).

#### A.3.2 My SIS

| | |
|---|---|
| **Route** | `/my-registration` |
| **Who** | Student |
| **Purpose** | View and correct one's own SIS. |
| **Endpoints** | `GET /api/registration/mine`<br>`PUT /api/registration/mine` |

**Description.** Shows the student's own Student Information Sheet. While it is still awaiting the Registrar's confirmation, the student can correct:

- names;
- birth details;
- mobile number;
- address;
- guardian.

Program, student type and email can't be changed by the student. Once confirmed, the sheet is read-only. A name correction is also applied to the student's login account. Every correction is audited.

#### A.3.3 Registration (SIS)

| | |
|---|---|
| **Route** | `/registrations` |
| **Who** | Registrar (and School Admin) |
| **Purpose** | Review, correct and confirm submitted SIS records. |
| **Endpoints** | `GET /api/registration`<br>`GET /api/registration/{id}`<br>`PUT /api/registration/{id}`<br>`GET /api/prospectus/students/{registrationId}/registration-form.pdf` |

**Description.** A queue of the active term's SIS submissions; search widens to every term. Opening a record shows every SIS field plus the student's document checklist. The Registrar can:

- correct any field (saved in ALL CAPS);
- override the year level (audited);
- **confirm** the registration, which emails the student;
- download a confirmed student's **Certificate of Registration (COR)** PDF: their approved subjects with day, time, room, faculty and total units.

Records with the same name and date of birth as another record are flagged as **possible duplicates**, never refused.

#### A.3.4 Term activations

| | |
|---|---|
| **Route** | `/term-activations` |
| **Who** | Admission Officer (and School Admin) |
| **Purpose** | Validate returning students' requests to activate for the term. |
| **Endpoints** | `GET /api/registration/term-activation`<br>`POST /api/registration/term-activation/{id}/validate` |

**Description.** Lists pending activation requests for the active term with official student number, last name and the year level validation will set. A request where the student's confirmed year level **disagrees** with the record is highlighted. Validating accepts the derived year level, or overrides it (for example for a repeating student or one returning from leave). It also creates the student's login if they don't have one, and emails a confirmation.

#### A.3.5 Term activation control

| | |
|---|---|
| **Route** | `/term-activation-control` |
| **Who** | Registrar, Academic Head (and School Admin, Super Admin) |
| **Purpose** | Open or close the public term-activation form. |
| **Endpoints** | `GET /api/registration/term-activation/control`<br>`PUT /api/registration/term-activation/control` |

**Description.** One institution-wide switch. Closing it refuses new lookups and requests on the public form with a plain message, but leaves requests already filed untouched, so the Admission Office can keep validating. The page shows how many requests are pending and when, and by whom, the switch was last changed. Every change is audited.

#### A.3.6 Assign student number

| | |
|---|---|
| **Route** | `/assign-student-number` |
| **Who** | Admission Officer (and School Admin) |
| **Purpose** | Record official student numbers against registrations. |
| **Endpoints** | `GET /api/registration/student-number`<br>`POST /api/registration/{id}/student-number` |

**Description.** Records the official number issued by the separate student-records system against each SEN-GEN registration. The **Show** switch offers:

- *Still to number* (default work queue);
- *Already numbered* (to review or correct);
- *All registrations*.

Each row shows the SIS status and whether a number is on file. Whole-queue tallies (numbered / pending / total) don't change with the view or search. Numbers must be unique, and every assignment is audited.

#### A.3.7 Evaluate transferee

| | |
|---|---|
| **Route** | `/evaluate-transferee` |
| **Who** | Registrar, Admission Officer, Academic Head (and School Admin) |
| **Purpose** | Rule on a transferee's credits subject by subject. |
| **Endpoints** | `GET /api/transferee-evaluations`<br>`GET /api/transferee-evaluations/{registrationId}`<br>`PUT /api/transferee-evaluations/{registrationId}`<br>`POST /api/transferee-evaluations/{registrationId}/complete`<br>`POST /api/transferee-evaluations/{registrationId}/reopen`<br>`GET /api/prospectus/students/{registrationId}/evaluation.pdf` |

**Description.** A queue of the active term's transferees with each one's progress (Pending / In progress / Completed). The evaluation sheet lists the student's whole curriculum, term by term. Each subject is marked:

- **Credited**, recording the previous school's subject and grade; or
- **To take**.

"Credit all" and "All to take" apply to a whole term block. Credited units and the **derived year level** update as decisions are made. Work can be saved and continued later.

**Complete** is allowed only when every subject has a verdict. It sets the year level (overridable), opens the student's enlistment and notifies them. **Reopen** allows corrections without losing earlier decisions, and closes enlistment again until re-completed. The sheet prints as a signable PDF.

#### A.3.8 Academic records

| | |
|---|---|
| **Route** | `/academic-records` |
| **Who** | Registrar, Academic Head (and School Admin) |
| **Purpose** | Keep each student's subject results per term. |
| **Endpoints** | `GET /api/academic-records`<br>`GET /api/academic-records/{registrationId}`<br>`PUT /api/academic-records/{registrationId}`<br>`GET /api/academic-records/template`<br>`POST /api/academic-records/import` |

**Description.** A per-student sheet read against the student's own curriculum. The Registrar records how each subject ended in a given term: **passed, failed or dropped** (results only, no grades). Records are saved one term at a time, and retakes keep every attempt.

An .xlsx template can be downloaded and filled in to **import** history in bulk, for example when adopting SEN-GEN mid-program. The page shows how many students have no history on file.

This record drives prerequisite checks, retake offers, "already passed" subjects and year-level advancement.

#### A.3.9 Curriculum prospectus

| | |
|---|---|
| **Route** | `/prospectus` |
| **Who** | Registrar, Academic Head, Admission Officer (and School Admin) |
| **Purpose** | Print the subjects a year level takes. |
| **Endpoints** | `GET /api/prospectus/programs`<br>`GET /api/prospectus/curriculum.pdf` |

**Description.** Choose a program/curriculum and a year level (or the whole program) and download a printable PDF: subjects term by term with units, lecture/laboratory split, prerequisites and running totals.

#### A.3.10 Pre-enrollment import

| | |
|---|---|
| **Route** | `/pre-enrollment` |
| **Who** | Registrar (and School Admin) |
| **Purpose** | Load prospective-student lists from Excel. |
| **Endpoints** | `GET /api/pre-enrollment/template`<br>`POST /api/pre-enrollment/import` |

**Description.** Download the .xlsx template, fill it in, and upload it. The system:

1. **Extracts** the rows.
2. **Validates** each row: required fields, email and date formats, allowed values.
3. **Transforms** it: names, student numbers, document checklists.
4. **Loads** it, detecting duplicates by email and by name + birthdate, both within the file and against existing records.

A row-by-row report lists what loaded and why any row failed. Valid rows load even when others fail, and re-importing the same file creates no duplicates. Imported students go through the same confirmation and clearance steps as everyone else.

#### A.3.11 Pre-authorization

| | |
|---|---|
| **Route** | `/pre-authorization` |
| **Who** | Admission Officer, Registrar (and School Admin) |
| **Purpose** | Clear students for online subject enlistment. |
| **Endpoints** | `GET /api/pre-authorization`<br>`POST /api/pre-authorization/{registrationId}`<br>`POST /api/pre-authorization/bulk`<br>`DELETE /api/pre-authorization/{registrationId}` |

**Description.** A queue of the active term's students, each shown as **Ready**, **Blocked** (with the exact reason) or **Cleared**, with submitted/total paper counts. Search widens to every term.

**Authorize** clears a student. It requires a Registrar-confirmed SIS and the papers flagged *required for authorization*: the report card and good moral for a new student, or the transcript and certificate of transfer for a transferee. Other papers are followed up but don't block clearance.

**Start selecting students** enables bulk clearance: blocked students are skipped and listed with their reasons, and the rest are cleared together. A clearance can be **revoked**. Every grant and revoke is audited.

### A.4 Enlistment pages

#### A.4.1 Subject enlistment

| | |
|---|---|
| **Route** | `/enlistment` |
| **Who** | Student |
| **Purpose** | Reserve seats in the term's subjects online. |
| **Endpoints** | `GET /api/enlistment/sections`<br>`POST /api/enlistment/requests`<br>`GET /api/enlistment/mine`<br>`DELETE /api/enlistment/requests/{requestId}` |

**Description.** The page opens with the student's **plan for the term**: exactly the subjects they owe. That is their curriculum at their year level for this term, minus credited or passed subjects, plus failed subjects due for retake. Each subject shows its units, how many classes are published for it, and the request status. A subject with no published class yet is flagged.

Below the plan are the **published** sections for those subjects, with day, time, room, faculty and **live seat counts**. "Show every section" widens the list but never what may be requested.

**Request a seat** is checked by the server, which refuses with the reason when:

- the student isn't linked, confirmed and pre-authorized;
- a transferee's evaluation is incomplete;
- enlistment is closed or the stage isn't *Subject enlistment*;
- the subject isn't in their plan;
- a prerequisite isn't passed (a co-requisite can be requested alongside);
- the section is full;
- the times overlap another class;
- they already requested the subject;
- the unit ceiling would be exceeded.

Pending requests can be cancelled. The page shows whether the student is **Enrolled for the term**.

#### A.4.2 My subjects

| | |
|---|---|
| **Route** | `/my-subjects` |
| **Who** | Student |
| **Purpose** | See and print one's own subjects. |
| **Endpoints** | `GET /api/registration/link`<br>`GET /api/prospectus/me/curriculum.pdf`<br>`GET /api/prospectus/me/registration-form.pdf` |

**Description.** Shows the subjects the student's year level takes. Two downloads are available:

- the **curriculum prospectus** PDF, with credited subjects shaded for an evaluated transferee;
- the **Certificate of Registration (COR)** PDF, showing:
  - every approved subject with day, time, room and faculty;
  - total units;
  - enrollment status;
  - an amendment flag on any class changed after publishing.

Both are resolved from the signed-in account, so a student can only download their own.

#### A.4.3 Enlistment approvals

| | |
|---|---|
| **Route** | `/approvals` |
| **Who** | Registrar (and School Admin; Academic Head for capacity override) |
| **Purpose** | Decide students' seat requests. |
| **Endpoints** | `GET /api/enlistment/approvals`<br>`POST /api/enlistment/approvals/{requestId}/approve`<br>`POST /api/enlistment/approvals/{requestId}/reject`<br>`POST /api/enlistment/approvals/{requestId}/drop`<br>`POST /api/enlistment/approvals/bulk-approve`<br>`POST /api/enlistment/approvals/sections/{sectionId}/capacity`<br>Seat-count check: `GET /api/enlistment/seat-counts/mismatches`, `POST /api/enlistment/seat-counts/{sectionId}/reconcile` |

**Description.** A queue of the active term's pending seat requests, with each student's "enrolled for the term" status. Actions:

- **Approve** a request. It takes a seat safely even under heavy load (the database makes it impossible to exceed capacity) and re-checks prerequisites and co-requisites.
- **Reject** a request.
- **Drop** an approved seat, releasing it.
- **Bulk approve** a selection, a student's whole outstanding load, or the entire queue. Every request still passes every check.
- **Capacity override:** raise a full section's seat cap, up to the institutional cap and the smallest room the section meets in. Audited, and staff are notified.

Decisions are emailed and notified to the student. The **Seat-count check** panel finds sections whose stored seat count disagrees with their live approved requests and lets staff correct each one deliberately.

### A.5 Scheduling pages

#### A.5.1 My schedule

| | |
|---|---|
| **Route** | `/schedule` |
| **Who** | Student, Faculty Member |
| **Purpose** | View one's own weekly timetable. |
| **Endpoints** | `GET /api/scheduling/my-schedule` |

**Description.** A read-only weekly calendar plus a day-by-day list for the active semester. **Faculty** see the classes assigned to them with each section's enrollment count. **Students** see the classes they hold approved seats in. Classes changed after publishing are flagged as amended.

#### A.5.2 Schedule board

| | |
|---|---|
| **Route** | `/scheduling/board` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Build or adjust the timetable by hand. |
| **Endpoints** | `GET /api/scheduling/board`<br>`POST /api/scheduling/board`<br>`PUT /api/scheduling/board/{assignmentId}`<br>`DELETE /api/scheduling/board/{assignmentId}` |

**Description.** A drag-and-drop weekly calendar. Faculty-allocated subjects wait in the **Assigned Subjects** pool and are dragged onto a room's grid; blocks can be moved, resized or removed. Every change is checked live for:

- room double-booking;
- faculty double-booking;
- same-section overlap;
- the right room kind (lecture room, computer lab, kitchen lab).

A refused change states the reason. In the **All rooms** view, dropping a subject opens the **room picker** for that exact slot: unavailable rooms are greyed out with the reason, and available rooms are listed smallest-fit first.

The **Weekly Hours Tracker** shows plotted vs. required hours per subject, faculty and section. Lecture–laboratory subjects are placed as two separate meetings.

Editing an already-published class asks for confirmation. The class is then flagged as an **amendment** (dashed outline), and the faculty member and every student with a seat are notified by bell and email. The board is locked while the schedule is finalized or the semester is archived.

#### A.5.3 Generate schedule

| | |
|---|---|
| **Route** | `/scheduling/generate` |
| **Who** | Academic Head, School Admin |
| **Purpose** | Run the CSP engine to build the timetable automatically. |
| **Endpoints** | `POST /api/scheduling/generate`<br>`GET /api/scheduling/schedule`<br>`GET /api/scheduling/soft-constraints`<br>`PUT /api/scheduling/soft-constraints/weights`<br>`POST /api/scheduling/{semesterId}/finalize`<br>`POST /api/scheduling/{semesterId}/reopen` |

**Description.** Choose a semester and run the engine. It places every class from the faculty load allocation into a room and time slot. If the term has no sections yet, it creates them from the allocation.

**Hard rules (never broken):**

- no room or faculty double-booking;
- no overlap within a section;
- room capacity respected;
- faculty load ceilings respected;
- correct room kind;
- prerequisite ordering and co-requisites valid.

**Soft goals (optimized):**

- faculty time preferences;
- fewer idle gaps, for both students and faculty;
- right-sized rooms.

Load balance comes from the faculty load allocation, which the engine places as given.

A progress overlay shows while it works. The result shows a placement summary, an optimization report and the schedule table.

The **Generation settings** panel sets:

- the teaching-day start time (showing how many periods it trims);
- the soft-goal weights;
- the time and step budgets.

An **arrangement number** reproduces a previous result from unchanged inputs.

Generation **replaces** the semester's timetable. If that would discard a published schedule, the page asks twice and states the published classes, sections and enlisted students affected. **Finalize** locks the reviewed draft for publishing; **Reopen** unlocks it for more changes.

#### A.5.4 Review & overrides

| | |
|---|---|
| **Route** | `/scheduling/review` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Inspect the generated schedule before publishing. |
| **Endpoints** | `GET /api/scheduling/schedule` |

**Description.** Shows the semester's schedule grouped by cohort (program, year and section) with its status: Draft, Finalized (ready to publish) or Published. Manual changes are made on the Schedule board; finalizing and reopening are done on the Generate schedule page.

#### A.5.5 Schedule publishing

| | |
|---|---|
| **Route** | `/publishing` |
| **Who** | Registrar (and School Admin) |
| **Purpose** | Release the finalized schedule to students and faculty. |
| **Endpoints** | `GET /api/scheduling/schedule`<br>`GET /api/publishing/{semesterId}/preview`<br>`POST /api/publishing/{semesterId}/publish`<br>`GET /api/publishing/schedule` |

**Description.** Shows draft vs. published counts for the semester. **Publish** is only possible once the Academic Head has finalized the schedule. Before running, a preview states how many classes will be published and how many faculty and students will be emailed; the Registrar then confirms. Publishing notifies every assigned faculty member and confirmed student. The published schedule can then be viewed **By class**, **By week** or **By day**, with day and cohort filters.

#### A.5.6 Faculty load

| | |
|---|---|
| **Route** | `/faculty-load` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Allocate subjects to faculty before scheduling. |
| **Endpoints** | `GET /api/faculty-load`<br>`GET /api/faculty-load/{facultyProfileId}/subjects`<br>`PUT /api/faculty-load/{facultyProfileId}`<br>`GET /api/faculty-load/{facultyProfileId}/preferences`<br>`PUT /api/faculty-load/{facultyProfileId}/preferences` |

**Description.** Lists every faculty member with assigned units against their maximum, a load bar and an over-ceiling warning.

The **Assign Load** modal assigns subjects per class section (for example CS301 → BSCS 3-A). A subject-and-section pair already held by someone else is shown with its holder and can't be taken, and assignments that would exceed the member's ceiling are refused.

The **Preferences** modal records each member's preferred teaching windows, which the engine tries to honor. This allocation is what the scheduling engine places.

### A.6 Academic setup pages

#### A.6.1 School years

| | |
|---|---|
| **Route** | `/school-years` |
| **Who** | School Admin |
| **Purpose** | Maintain academic school years. |
| **Endpoints** | `GET /api/school-years`<br>`POST /api/school-years`<br>`PUT /api/school-years/{id}`<br>`DELETE /api/school-years/{id}`<br>`POST /api/school-years/{id}/active` |

**Description.** Create, edit and delete school years with their key dates, and set which one is **active**. Changes are audited.

#### A.6.2 Semesters

| | |
|---|---|
| **Route** | `/semesters` |
| **Who** | School Admin |
| **Purpose** | Maintain terms and change the active term. |
| **Endpoints** | `GET /api/semesters`<br>`POST /api/semesters`<br>`PUT /api/semesters/{id}`<br>`DELETE /api/semesters/{id}`<br>`POST /api/semesters/{id}/active`<br>`POST /api/semesters/{id}/archive`<br>`POST /api/semesters/{id}/unarchive`<br>`GET /api/school-years`<br>`GET /api/reports/semester-export` |

**Description.** Create, edit and delete semesters within a school year.

- **Activate** a semester. Exactly one is active, and every work queue, enlistment check and reminder follows it.
- **Archive** a finished term to freeze its schedule read-only while keeping its data for reports; **unarchive** reverses it. The active term can't be archived.
- **Export** a semester's whole data set as one workbook.

#### A.6.3 Buildings

| | |
|---|---|
| **Route** | `/buildings` |
| **Who** | School Admin |
| **Purpose** | Maintain campus buildings. |
| **Endpoints** | `GET /api/buildings`<br>`POST /api/buildings`<br>`PUT /api/buildings/{id}`<br>`DELETE /api/buildings/{id}` |

**Description.** Create, edit and delete the buildings that group teaching rooms.

#### A.6.4 Rooms

| | |
|---|---|
| **Route** | `/rooms` |
| **Who** | School Admin |
| **Purpose** | Maintain teaching rooms. |
| **Endpoints** | `GET /api/rooms`<br>`POST /api/rooms`<br>`PUT /api/rooms/{id}`<br>`DELETE /api/rooms/{id}`<br>`GET /api/buildings` |

**Description.** Create, edit and delete rooms with their building, capacity and **room kind**: lecture room, computer laboratory or kitchen laboratory. The scheduler and board use the room kind to decide which classes may go there.

#### A.6.5 Class sections

| | |
|---|---|
| **Route** | `/class-sections` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Maintain student blocks. |
| **Endpoints** | `GET /api/class-sections`<br>`POST /api/class-sections`<br>`PUT /api/class-sections/{id}`<br>`DELETE /api/class-sections/{id}` |

**Description.** Create, edit and delete class sections (course · year level · section, for example BSCS 3-A) per semester. Each is linked to the curriculum it follows, and can be filtered by semester and program.

#### A.6.6 Subjects & curriculum

| | |
|---|---|
| **Route** | `/subjects` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Maintain the subject catalog and program curricula. |
| **Endpoints** | `GET /api/curricula`<br>`POST /api/curricula`<br>`PUT /api/curricula/{id}`<br>`POST /api/curricula/{id}/active`<br>`POST /api/curricula/{id}/archive`<br>`POST /api/curricula/{id}/restore`<br>`GET /api/subjects`<br>`POST /api/subjects`<br>`PUT /api/subjects/{id}`<br>`DELETE /api/subjects/{id}`<br>`POST /api/subjects/{id}/archive`<br>`POST /api/subjects/{id}/restore` |

**Description.**

**Subjects:** create and edit code, title, units, weekly hours, delivery (lecture only / laboratory only / lecture–laboratory), required laboratory kind and prerequisites. Subjects can be archived, restored or deleted.

**Curricula:** create and edit each program's curriculum, placing subjects by year level and term, and set the **active** curriculum per program. Curricula can be archived or restored. The **Archived** modal lists archived items for restoring.

Continuing students stay on the curriculum of the school year they entered.

### A.7 Administration pages

#### A.7.1 Reports & analytics

| | |
|---|---|
| **Route** | `/reports` |
| **Who** | Registrar, Academic Head (and School Admin) |
| **Purpose** | View and export semester reports. |
| **Endpoints** | `GET /api/reports/registration`<br>`GET /api/reports/enlistment`<br>`GET /api/reports/faculty-load`<br>`GET /api/reports/room-utilization`<br>`GET /api/reports/document-completion`<br>(each with `?format=xlsx` to export)<br>`GET /api/dashboard/metrics`<br>`GET /api/reports/semester-export`<br>`GET /api/reports/system-export` (School Admin)<br>`/hubs/reports` |

**Description.** A report switcher with five reports:

1. validated registrations;
2. enlistment results;
3. faculty load summary;
4. room utilization;
5. document completion.

Each report is scoped to a chosen semester, shown as a table, and exportable to Excel in one click. The open report refreshes itself when data changes.

Two bundle exports are also available: the **whole semester** in one workbook, and, for the School Admin, **master data and system parameters** in one workbook.

#### A.7.2 Room utilization

| | |
|---|---|
| **Route** | `/analytics/room-utilization` |
| **Who** | Registrar, Academic Head (and School Admin) |
| **Purpose** | Find under-used rooms. |
| **Endpoints** | `GET /api/analytics/room-utilization`<br>`GET /api/analytics/room-utilization/export`<br>`GET /api/reports/room-grid-schedule`<br>`/hubs/reports` |

**Description.** Ranks every room by hours used against the teaching window and flags **critically underutilized** rooms, to support room reallocation. Downloads include the analysis workbook and the **room grid schedule** (the class scheduling matrix by room). The page refreshes live.

#### A.7.3 Faculty load reports

| | |
|---|---|
| **Route** | `/reports/faculty-load` |
| **Who** | Registrar, Academic Head (and School Admin) |
| **Purpose** | Produce the Confirmation of Faculty Loading and grid schedules. |
| **Endpoints** | `GET /api/reports/faculty-loading`<br>`GET /api/reports/faculty-loading/{facultyProfileId}`<br>`GET /api/reports/faculty-loading/{facultyProfileId}/pdf`<br>`GET /api/reports/faculty-loading/{facultyProfileId}/schedule-grid`<br>`GET /api/reports/faculty-loading/consolidated`<br>`GET /api/reports/faculty-loading/consolidated.pdf`<br>`GET /api/reports/faculty-loading/bulk`<br>`GET /api/reports/grid-schedules`<br>`GET /api/dashboard/metrics`<br>`/hubs/reports` |

**Description.** Lists faculty members for a semester. It reproduces the school's own **Confirmation of Faculty Loading** form:

- per member, as a workbook or PDF;
- consolidated for all members, as a workbook or PDF;
- in bulk.

The forms are signed through the program head set in System parameters. Also available are each member's **grid schedule** workbook and an all-faculty grid-schedules workbook. The page refreshes live.

#### A.7.4 User management

| | |
|---|---|
| **Route** | `/users` |
| **Who** | Academic Head (and School Admin) |
| **Purpose** | Manage login accounts for all roles. |
| **Endpoints** | `GET /api/users`<br>`POST /api/users`<br>`PUT /api/users/{id}`<br>`POST /api/users/{id}/active`<br>`POST /api/users/{id}/password` |

**Description.** Search and filter accounts. Actions:

- **create** an account with any role;
- **edit** name, email and role;
- **deactivate** or **reactivate** an account (the last active School Admin can't be deactivated);
- **reset** a user's password.

Every action is audited.

#### A.7.5 System parameters

| | |
|---|---|
| **Route** | `/parameters` |
| **Who** | School Admin |
| **Purpose** | Maintain institutional rules without code changes. |
| **Endpoints** | `GET /api/parameters`<br>`PUT /api/parameters/settings`<br>`PUT /api/parameters/section-capacity`<br>`POST /api/parameters/time-slots`<br>`PUT /api/parameters/time-slots/{id}`<br>`DELETE /api/parameters/time-slots/{id}`<br>`PUT /api/parameters/faculty/{id}/load-limit`<br>`PUT /api/parameters/program-heads/{programCode}` |

**Description.** Cards on this page:

- **Section seat cap:** default 40. Sections already above a lowered cap are listed, never changed.
- **Enrollment rules:**
  - enlistment open/closed switch, which works within the *Subject enlistment* stage;
  - maximum units per student;
  - minimum section size (advisory; under-filled sections are flagged).
- **Institution:** the name printed on official documents.
- **Faculty-loading memo:**
  - laboratory teaching-credit factor (teaching hours = lecture hours + lab hours × factor);
  - the **Program Head** who signs the form for each program.
- **Scheduling engine budgets:** time and step limits for one generation run.
- **Allowable time slots:** create, edit and delete the periods classes may use. Slots used by a published schedule can't be changed.
- **Faculty ceilings:** each member's maximum units, edited in place.

Every change is audited.

#### A.7.6 Email outbox

| | |
|---|---|
| **Route** | `/outbox` |
| **Who** | School Admin |
| **Purpose** | Check and recover email delivery. |
| **Endpoints** | `GET /api/admin/outbox`<br>`POST /api/admin/outbox/{id}/retry`<br>`POST /api/admin/outbox/retry-failed` |

**Description.** Lists every email the system has queued with its status: queued, delivered or permanently failed. It answers "did that student get the email?". A failed email can be **retried** individually, or **all failures** retried at once after a mail outage.

#### A.7.7 Audit trail

| | |
|---|---|
| **Route** | `/audit` |
| **Who** | School Admin |
| **Purpose** | Review the accountability log. |
| **Endpoints** | `GET /api/audit`<br>`/hubs/reports` |

**Description.** A newest-first log of every security- and data-relevant action (64+ types), filterable by action. Each entry shows who did it, when and what. The types cover:

- logins and account changes;
- SIS and registrations;
- term activations, student numbers, pre-authorization and imports;
- requirements and checklists;
- setup and curriculum changes;
- faculty load;
- schedule generation, overrides, amendments, finalize and publish;
- seat requests and approvals;
- stage changes;
- parameter changes;
- exports;
- email dispatches;
- survey activity.

#### A.7.8 Rating survey (results)

| | |
|---|---|
| **Route** | `/survey-admin` |
| **Who** | Super Admin |
| **Purpose** | Read the ISO/IEC 25010 usability report. |
| **Endpoints** | `GET /api/admin/survey/invitations`<br>`GET /api/admin/survey/results`<br>`GET /api/admin/survey/results/export`<br>`GET /api/admin/survey/collection`<br>`POST /api/admin/survey/collection`<br>`POST /api/admin/survey/invitations/remind` |

**Description.** Shows the overall weighted mean and per-characteristic scores with their verbal interpretation (Usability headlined), the response count against the goal, and respondents' comments. The Super Admin can:

- open or close the **collection window** and set the response goal;
- **remind** people who haven't answered;
- **export** raw responses to CSV (opens correctly in Excel, including Filipino text).

#### A.7.9 Survey recipients

| | |
|---|---|
| **Route** | `/survey-recipients` |
| **Who** | Super Admin |
| **Purpose** | Choose who receives the survey and send it. |
| **Endpoints** | `GET /api/admin/survey/audience`<br>`POST /api/admin/survey/invitations`<br>`POST /api/admin/survey/invitations/remind`<br>`DELETE /api/admin/survey/invitations/{id}`<br>`GET /api/admin/survey/collection` |

**Description.** Lists every account with its invitation status. The Super Admin picks individual users, or whole roles as a shortcut, and sends the survey as a bell notice and/or an emailed single-use link, with an optional personal note. Invited people who haven't answered can be **reminded**; a fresh link replaces the old one. An invitation can be **withdrawn**.

---

## Appendix B: Endpoint reference

Every API endpoint, grouped by module. For each: the method and path, the page(s) that call it, its purpose, and a complete description including who may call it.

> **Roles:** "staff" means Admission Officer, Registrar, Academic Head and School Admin. The School Admin and Super Admin can call every endpoint of the institutional roles. Super-admin-only endpoints are marked.

### B.1 Authentication

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| POST | `/api/auth/register` | Create account | Self-register an account | Public. Validates name, email and password strength; requires terms acceptance (time saved); refuses a duplicate email. Creates the account. |
| POST | `/api/auth/login` | Sign in | Sign in | Public. Checks email and password, with throttling after repeated failures. Returns a session token (JWT) carrying the role, or, for a 2FA account, a challenge token and emails a 6-digit code. Logged as success or failure. |
| POST | `/api/auth/2fa/verify` | Sign in | Complete a 2FA sign-in | Public. Exchanges the challenge token and emailed code for a session token. Attempts are limited and the code expires. |
| POST | `/api/auth/2fa/resend` | Sign in | Resend the 2FA code | Public. Emails a fresh code for an active challenge. |
| POST | `/api/auth/forgot-password` | Forgot password | Request a reset link | Public. Emails a single-use, time-limited reset link. Always answers the same way, so it doesn't reveal whether an email has an account. |
| POST | `/api/auth/reset-password` | Reset password | Set a new password | Public. Accepts a valid reset token and the new password; the token is then spent. |
| GET | `/api/auth/me` | App shell, Confirm email, First-login password change | Current user | Any signed-in user. Returns the user's id, name, email, role, 2FA status and whether a password change is required. |

### B.2 Profile

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| PUT | `/api/profile` | Profile settings | Update own profile | Any signed-in user. Saves the user's own display name. Audited. |
| PUT | `/api/profile/password` | Profile settings, First-login password change | Change own password | Any signed-in user. Requires the current password; clears the "must change password" flag. Audited. |
| POST | `/api/profile/email/request` | Profile settings | Start an email change | Any signed-in user. Emails a confirmation link to the new address; nothing changes until it is confirmed. Audited. |
| POST | `/api/profile/email/confirm` | Confirm email change | Finish an email change | Accepts the confirmation token and switches the account to the new address. Audited. |
| POST | `/api/profile/2fa/start` | Profile settings | Begin enabling 2FA | Any signed-in user. Emails a code to the account address. |
| POST | `/api/profile/2fa/enable` | Profile settings | Enable 2FA | Any signed-in user. Confirms the emailed code and turns 2FA on. Audited. |
| POST | `/api/profile/2fa/disable` | Profile settings | Disable 2FA | Any signed-in user. Requires the account password and turns 2FA off. Audited. |

### B.3 Registration (SIS) and term activation

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| POST | `/api/registration` | Digital SIS | Submit a SIS | Public. Validates and stores the SIS in ALL CAPS with terms acceptance. Issues a registration number, provisions the student login (temporary password emailed), seeds the document checklist and emails a confirmation. Refused in *Preparation* and *Enrollment closed*. |
| GET | `/api/registration` | Registration (SIS) | Registration queue | Registrar. Lists SIS records for the active term with status and duplicate flags; search widens to every term; paged. |
| GET | `/api/registration/{id}` | Registration (SIS) | Open a registration | Registrar. Returns every SIS field, the year level and the document checklist. |
| PUT | `/api/registration/{id}` | Registration (SIS) | Correct / confirm a registration | Registrar. Saves corrections (ALL CAPS), year-level overrides and confirmation. Confirmation emails the student. Audited. |
| GET | `/api/registration/mine` | My SIS | Own SIS | Student. Returns the student's own SIS and whether it is still editable. |
| PUT | `/api/registration/mine` | My SIS | Correct own SIS | Student. Allowed only while *Submitted*. Updates names, birth details, mobile, address and guardian (not program, student type or email). A name change also updates the login account. Audited. |
| GET | `/api/registration/link` | Dashboard, Document requirements, My subjects, Profile settings | Own student record | Student. Returns whether the account is linked to a SIS record, plus official student number, registration number, program and year level. |
| POST | `/api/registration/link` | Document requirements | Link account to SIS | Student. Links the account to a SIS record when the student number, date of birth and account email all match. Audited. |
| GET | `/api/registration/student-number` | Assign student number | Student-number queue | Admission Officer. Lists registrations by `?status=` (to number / numbered / all) with whole-queue tallies. |
| POST | `/api/registration/{id}/student-number` | Assign student number | Assign official number | Admission Officer. Records the official student number; must be unique. Audited. |
| POST | `/api/registration/term-activation/lookup` | Term activation | Activation step 1 | Public. Finds the student by official number (or registration number) and last name, and returns the year level and term they would activate into. Refused while term activation is closed. |
| POST | `/api/registration/term-activation` | Term activation | Activation step 2 | Public. Files the request once the student confirms the year level and term. The term must still be the active one. Audited. |
| GET | `/api/registration/term-activation` | Term activations | Activation queue | Admission Officer. Lists the active term's requests, highlighting a declared year level that disagrees with the record. |
| POST | `/api/registration/term-activation/{id}/validate` | Term activations | Validate a request | Admission Officer. Approves the request, sets the derived or overridden year level, provisions a login if needed and emails confirmation. Audited. |
| GET | `/api/registration/term-activation/control` | Term activation control | Read the switch | Registrar, Academic Head. Returns whether activation is open, the pending count and the last change. |
| PUT | `/api/registration/term-activation/control` | Term activation control | Open / close activation | Registrar, Academic Head. Sets the institution-wide switch. Audited. |

### B.4 Transferee evaluation and academic records

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/transferee-evaluations` | Evaluate transferee | Transferee queue | Registrar, Admission Officer, Academic Head. Lists the active term's transferees with evaluation progress; search widens to every term. |
| GET | `/api/transferee-evaluations/{registrationId}` | Evaluate transferee | Open an evaluation | Same roles. Returns the curriculum sheet with each subject's verdict, credited units and derived year level. |
| PUT | `/api/transferee-evaluations/{registrationId}` | Evaluate transferee | Save an evaluation | Same roles. Saves Credited (with source subject and grade) or To take verdicts; moves the sheet to *In progress*. Audited. |
| POST | `/api/transferee-evaluations/{registrationId}/complete` | Evaluate transferee | Complete an evaluation | Same roles. Requires every subject decided. Sets the year level (derived or overridden), opens enlistment and notifies the student. Audited. |
| POST | `/api/transferee-evaluations/{registrationId}/reopen` | Evaluate transferee | Reopen an evaluation | Same roles. Keeps all decisions and closes enlistment until it is completed again. Audited. |
| GET | `/api/academic-records` | Academic records | Student list | Registrar, Academic Head. Lists students with their history status, and the count of students with no history. |
| GET | `/api/academic-records/{registrationId}` | Academic records | Open a student's record | Same roles. Returns the student's curriculum with each subject's recorded results per term. |
| PUT | `/api/academic-records/{registrationId}` | Academic records | Save a term's results | Same roles. Saves passed / failed / dropped verdicts for one term; retakes are kept as separate attempts. Audited. |
| GET | `/api/academic-records/template` | Academic records | Import template | Same roles. Downloads the .xlsx template. |
| POST | `/api/academic-records/import` | Academic records | Import history | Same roles. Loads results from .xlsx with a row-by-row report. Audited. |

### B.5 Documents and requirements

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/documents` | Document requirements | List checklists | Staff see the active term's checklists (filter complete/incomplete; search widens to every term; paged). A student gets only their own checklist. |
| PUT | `/api/documents/{documentId}` | Document requirements | Update a paper's status | Admission Officer, Registrar, School Admin. Sets one paper's status, recording who verified it and when. Only the forms the requirement accepts are allowed. Audited. |
| POST | `/api/documents/reminders` | Document requirements | Send reminders | Admission Officer, Registrar, School Admin. Emails every incomplete enrollee of the active term the papers they still owe, or one chosen enrollee. |
| GET | `/api/requirements` | Document requirements (Requirements modal) | List requirements | Admission Officer, Registrar, School Admin. Returns the catalog with programs, student types, authorization flag and accepted forms. |
| POST | `/api/requirements` | Requirements modal | Add a requirement | Same roles. Creates a requirement. Audited. |
| PUT | `/api/requirements/{id}` | Requirements modal | Edit a requirement | Same roles. Updates name, description, order, programs, student types and flags. Audited. |
| DELETE | `/api/requirements/{id}` | Requirements modal | Archive a requirement | Same roles. Archives rather than deletes, so existing checklists keep their history. Audited. |

### B.6 Pre-enrollment and pre-authorization

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/pre-enrollment/template` | Pre-enrollment import | Import template | Registrar. Downloads the .xlsx template. |
| POST | `/api/pre-enrollment/import` | Pre-enrollment import | Import students | Registrar. Extract → validate → transform → load with duplicate detection (in-file and against the database). Returns a row-by-row report; valid rows load even when others fail. Audited. |
| GET | `/api/pre-authorization` | Pre-authorization | Clearance queue | Admission Officer, Registrar, School Admin. Lists the active term's students as Ready / Blocked (with reasons) / Cleared, with paper counts and tallies; search widens to every term; paged. |
| POST | `/api/pre-authorization/{registrationId}` | Pre-authorization | Authorize a student | Same roles. Requires a confirmed SIS and the papers flagged as required for authorization; otherwise refused with each missing item named. Idempotent. Audited. |
| POST | `/api/pre-authorization/bulk` | Pre-authorization | Bulk authorize | Same roles. Clears many students in one call under the same rules; blocked students are skipped with reasons and the rest are cleared together. |
| DELETE | `/api/pre-authorization/{registrationId}` | Pre-authorization | Revoke clearance | Same roles. Removes a student's clearance. Audited. |

### B.7 Enlistment

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/enlistment/sections` | Subject enlistment | Browse sections + plan | Student. Returns the student's plan for the term and the published sections for it, with meetings and live seat counts. `?all=` widens the browse only. |
| POST | `/api/enlistment/requests` | Subject enlistment | Request a seat | Student. Checks eligibility, stage and switch, plan, prerequisites/co-requisites, capacity, time overlap, duplicates and unit ceiling; refuses with the reason. Audited. |
| GET | `/api/enlistment/mine` | Subject enlistment, Dashboard | Own requests | Student. The active term's requests with status, approved units and enrolled-for-the-term state. |
| DELETE | `/api/enlistment/requests/{requestId}` | Subject enlistment | Cancel a request | Student. Cancels one's own pending request. |
| GET | `/api/enlistment/approvals` | Enlistment approvals | Approval queue | Registrar, School Admin. The active term's pending requests with student and section details. |
| POST | `/api/enlistment/approvals/{requestId}/approve` | Enlistment approvals | Approve a request | Registrar, School Admin. Re-checks prerequisites and takes a seat safely (database capacity rule as backstop). Emails and notifies the student. Audited. |
| POST | `/api/enlistment/approvals/{requestId}/reject` | Enlistment approvals | Reject a request | Registrar, School Admin. Rejects with a reason; emails and notifies the student. Audited. |
| POST | `/api/enlistment/approvals/{requestId}/drop` | Enlistment approvals | Drop an approved seat | Registrar, School Admin. Releases the seat and decrements the section count. Audited. |
| POST | `/api/enlistment/approvals/bulk-approve` | Enlistment approvals | Bulk approve | Registrar, School Admin. Approves a selection, a student's whole load or the whole queue; each request is checked individually and failures are reported. |
| POST | `/api/enlistment/approvals/sections/{sectionId}/capacity` | Enlistment approvals | Override section capacity | Registrar, Academic Head, School Admin. Raises a section's seat cap, up to the institutional cap and the smallest room it meets in. Notifies staff. Audited. |
| GET | `/api/enlistment/seat-counts/mismatches` | Enlistment approvals (Seat-count check) | Find seat-count mismatches | Staff. Lists sections whose stored count disagrees with their live approved requests. |
| POST | `/api/enlistment/seat-counts/{sectionId}/reconcile` | Enlistment approvals (Seat-count check) | Correct a seat count | Staff. Sets the stored count to the live figure under the same safe-concurrency rules as an approval. |

### B.8 Enrollment cycle

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/enrollment-stage` | App shell (ticker) | Current stage | Any signed-in user. Returns the active term and its stage with display wording. |
| POST | `/api/enrollment-stage` | Stage modal | Set the stage | Academic Head, School Admin. Sets any stage, including stepping back to correct a mistake. Audited. |
| POST | `/api/enrollment-stage/advance` | Stage modal | Advance the stage | Academic Head, School Admin. Moves exactly one stage forward. Audited. |

### B.9 Scheduling

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| POST | `/api/scheduling/generate` | Generate schedule | Run the CSP engine | Academic Head, School Admin. Creates missing sections from the faculty load and validates prerequisite ordering. Solves under the hard rules while optimizing the soft goals, within the time and step budgets, and replaces the semester's timetable. Answers 409 with the counts at stake if a published schedule would be discarded and `replacePublished` wasn't given. Returns the placement summary, optimization report and arrangement number. Audited. |
| GET | `/api/scheduling/schedule` | Generate schedule, Review & overrides, Schedule publishing | Read a semester's schedule | Staff. Returns every placement with status (draft / finalized / published), provenance and amendment flags. |
| POST | `/api/scheduling/{semesterId}/finalize` | Generate schedule | Finalize the draft | Academic Head, School Admin. Locks the draft against regeneration and board edits so it can be published. Audited. |
| POST | `/api/scheduling/{semesterId}/reopen` | Generate schedule | Reopen for edits | Academic Head, School Admin. Unlocks a finalized draft. Audited. |
| GET | `/api/scheduling/soft-constraints` | Generate schedule | Engine settings and factors | Academic Head, School Admin. Returns the soft weights, budgets, class-day start, and what the last run optimized against. |
| PUT | `/api/scheduling/soft-constraints/weights` | Generate schedule | Save generation settings | Academic Head, School Admin. Saves the soft weights and the class-day start time (validated to the half hour; must leave at least one period). Audited. |
| GET | `/api/scheduling/board` | Schedule board | Board data | Academic Head, School Admin. One call returns the semester picker, rooms, faculty, the unplaced pool, placed entries and the hours tracker. |
| POST | `/api/scheduling/board` | Schedule board | Place a class | Academic Head, School Admin. Places a meeting at a day, time and room. Rejects room, faculty or section clashes and wrong room kinds (409 with the reason). Refused when locked. |
| PUT | `/api/scheduling/board/{assignmentId}` | Schedule board | Move / resize a class | Same roles and checks. Editing a published class records an amendment and notifies the faculty member and seated students. Audited. |
| DELETE | `/api/scheduling/board/{assignmentId}` | Schedule board | Remove a class | Same roles. Removing a published class is an amendment and notifies those affected. Audited. |
| GET | `/api/scheduling/my-schedule` | My schedule, Dashboard | Own timetable | Any signed-in user. Faculty get their assigned classes with seat counts; students get the classes they hold approved seats in. |

### B.10 Faculty load

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/faculty-load` | Faculty load | Faculty list | Academic Head, School Admin. Each member's assigned units against their ceiling for a semester. |
| GET | `/api/faculty-load/{facultyProfileId}/subjects` | Faculty load (Assign Load modal) | Assignable subjects | Same roles. Subject × class-section rows for the semester, showing which are already held and by whom. |
| PUT | `/api/faculty-load/{facultyProfileId}` | Faculty load (Assign Load modal) | Save a member's load | Same roles. Saves the assignments; refuses a row held by another member or a total above the ceiling. Audited. |
| GET | `/api/faculty-load/{facultyProfileId}/preferences` | Faculty load (Preferences modal) | Read time preferences | Academic Head, School Admin. The member's preferred teaching windows. |
| PUT | `/api/faculty-load/{facultyProfileId}/preferences` | Faculty load (Preferences modal) | Save time preferences | Same roles. Saves the windows the engine tries to honor. Audited. |

### B.11 Publishing

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/publishing/{semesterId}/preview` | Schedule publishing | Preview a publish | Registrar, School Admin. Dry run: classes to publish and faculty and students to notify, computed exactly as the publish will. |
| POST | `/api/publishing/{semesterId}/publish` | Schedule publishing | Publish | Registrar, School Admin. Refused (409) while any draft row is unfinalized. Marks the schedule published and emails every assigned faculty member and confirmed student. Audited. |
| GET | `/api/publishing/schedule` | Schedule publishing | Published schedule | Any signed-in user. The published-only schedule with day and cohort filters and their picker values. |

### B.12 Academic setup and curriculum

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET / POST | `/api/school-years` | School years, Semesters | List / create school years | School Admin. Lists school years or creates one with its key dates. Audited. |
| PUT / DELETE | `/api/school-years/{id}` | School years | Edit / delete a school year | School Admin. Audited. |
| POST | `/api/school-years/{id}/active` | School years | Set the active school year | School Admin. Makes this the active school year. Audited. |
| GET / POST | `/api/semesters` | Semesters | List / create semesters | School Admin. Filterable by school year. Audited. |
| PUT / DELETE | `/api/semesters/{id}` | Semesters | Edit / delete a semester | School Admin. Audited. |
| POST | `/api/semesters/{id}/active` | Semesters | Activate a term | School Admin. Makes this the only active semester; every queue follows it. Audited. |
| POST | `/api/semesters/{id}/archive` | Semesters | Archive a term | School Admin. Freezes the term's schedule read-only, keeping its data. The active term can't be archived. Audited. |
| POST | `/api/semesters/{id}/unarchive` | Semesters | Unarchive a term | School Admin. Lifts the freeze. Audited. |
| GET / POST | `/api/buildings` | Buildings, Rooms | List / create buildings | School Admin. Audited. |
| PUT / DELETE | `/api/buildings/{id}` | Buildings | Edit / delete a building | School Admin. Audited. |
| GET / POST | `/api/rooms` | Rooms | List / create rooms | School Admin. Name, building, capacity and room kind; filterable by building. Audited. |
| PUT / DELETE | `/api/rooms/{id}` | Rooms | Edit / delete a room | School Admin. Audited. |
| GET / POST | `/api/class-sections` | Class sections | List / create class sections | Academic Head, School Admin. Course · year · section per semester with its curriculum; filterable by semester and program. Audited. |
| PUT / DELETE | `/api/class-sections/{id}` | Class sections | Edit / delete a class section | Same roles. Audited. |
| GET / POST | `/api/subjects` | Subjects & curriculum | List / create subjects | Academic Head, School Admin. Code, title, units, hours, delivery, lab kind and prerequisites; filterable by curriculum. Audited. |
| PUT / DELETE | `/api/subjects/{id}` | Subjects & curriculum | Edit / delete a subject | Same roles. Audited. |
| POST | `/api/subjects/{id}/archive` · `/restore` | Subjects & curriculum, Archived modal | Archive / restore a subject | Same roles. Audited. |
| GET / POST | `/api/curricula` | Subjects & curriculum | List / create curricula | Same roles. Program curricula with subjects by year level and term. Audited. |
| PUT | `/api/curricula/{id}` | Subjects & curriculum | Edit a curriculum | Same roles. Audited. |
| POST | `/api/curricula/{id}/active` | Subjects & curriculum | Set the active curriculum | Same roles. Makes it the active curriculum for its program (continuing students stay on their entry-year curriculum). Audited. |
| POST | `/api/curricula/{id}/archive` · `/restore` | Subjects & curriculum, Archived modal | Archive / restore a curriculum | Same roles. Audited. |

### B.13 Dashboard, reports and analytics

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/dashboard/metrics` | Dashboard, Reports & analytics, Faculty load reports | Dashboard metrics | Staff. Live metrics for the active (or `?semesterId=`) semester: registrations, documents, pre-authorization, enlistment, rooms and faculty load. |
| GET | `/api/dashboard/scheduling-transparency` | Dashboard | Scheduling transparency | Academic Head, Registrar, School Admin. For the active (or `?semesterId=`) semester: every placement with subject, cohort, day and time, room, faculty, provenance (CSP engine or manual override) and status (draft / published / amended). Includes summary counts and the hard rules and soft factors the engine works to. |
| GET | `/api/reports/registration` | Reports & analytics | Validated registrations report | Registrar, Academic Head, School Admin. Semester-scoped; JSON or `?format=xlsx`. |
| GET | `/api/reports/enlistment` | Reports & analytics | Enlistment results report | Same roles. Semester-scoped; JSON or .xlsx. |
| GET | `/api/reports/faculty-load` | Reports & analytics | Faculty load summary | Same roles. Semester-scoped; JSON or .xlsx. |
| GET | `/api/reports/room-utilization` | Reports & analytics | Room utilization report | Same roles. Semester-scoped; JSON or .xlsx. |
| GET | `/api/reports/document-completion` | Reports & analytics | Document completion report | Same roles. Semester-scoped; JSON or .xlsx. |
| GET | `/api/reports/semester-export` | Reports & analytics, Semesters | Whole-semester workbook | Same roles. One workbook with the term's data. Audited. |
| GET | `/api/reports/system-export` | Reports & analytics | Master data workbook | School Admin. Setup master data and system parameters in one workbook. Audited. |
| GET | `/api/reports/faculty-loading` | Faculty load reports | Faculty loading list | Registrar, Academic Head, School Admin. Faculty with their loading for a semester. |
| GET | `/api/reports/faculty-loading/{facultyProfileId}` | Faculty load reports | One member's loading form | Same roles. Confirmation of Faculty Loading workbook for one member. |
| GET | `/api/reports/faculty-loading/{facultyProfileId}/pdf` | Faculty load reports | One member's loading PDF | Same roles. Printable PDF of the form with program-head sign-off. |
| GET | `/api/reports/faculty-loading/{facultyProfileId}/schedule-grid` | Faculty load reports | One member's grid schedule | Same roles. Weekly grid workbook for the member. |
| GET | `/api/reports/faculty-loading/consolidated` | Faculty load reports | Consolidated loading | Same roles. All members on one consolidated form (workbook). |
| GET | `/api/reports/faculty-loading/consolidated.pdf` | Faculty load reports | Consolidated loading PDF | Same roles. Printable PDF of the consolidated form. |
| GET | `/api/reports/faculty-loading/bulk` | Faculty load reports | Bulk loading forms | Same roles. Every member's individual form in one download. |
| GET | `/api/reports/grid-schedules` | Faculty load reports | All grid schedules | Same roles. Workbook with every faculty member's grid schedule. |
| GET | `/api/reports/room-grid-schedule` | Room utilization | Room grid schedule | Same roles. The class scheduling grid/matrix by room (workbook). |
| GET | `/api/analytics/room-utilization` | Room utilization | Room utilization analysis | Registrar, Academic Head, School Admin. Rooms ranked by used hours against the teaching window, with underutilization flags. |
| GET | `/api/analytics/room-utilization/export` | Room utilization | Room utilization workbook | Same roles. .xlsx of the analysis. |
| GET | `/api/prospectus/programs` | Curriculum prospectus | Programs and curricula | Registrar, Academic Head, Admission Officer. Programs, curricula and year levels for the picker. |
| GET | `/api/prospectus/curriculum.pdf` | Curriculum prospectus | Curriculum prospectus PDF | Same roles. `?curriculumId=&yearLevel=`; omit the year level for the whole program. |
| GET | `/api/prospectus/students/{registrationId}/evaluation.pdf` | Evaluate transferee | Transferee evaluation PDF | Staff. Signable credit-evaluation record with credited subjects, remaining load and year level. |
| GET | `/api/prospectus/students/{registrationId}/registration-form.pdf` | Registration (SIS) | Student's COR PDF | Registrar, Academic Head, Admission Officer, School Admin. Certificate of Registration for a student: approved subjects with day, time, room, faculty and total units, enrollment status, and amendment flags. |
| GET | `/api/prospectus/me/curriculum.pdf` | My subjects | Own prospectus PDF | Student. Own curriculum, with credited subjects shaded for an evaluated transferee. |
| GET | `/api/prospectus/me/registration-form.pdf` | My subjects | Own COR PDF | Student. Own Certificate of Registration for the term. |
| WS | `/hubs/reports` | Dashboard, Reports & analytics, Room utilization, Faculty load reports, Audit trail, Notifications bell, App shell | Live refresh | Signed-in users. A real-time (SignalR) channel that signals open pages to refetch when data changes. It never blocks or fails a save. |

### B.14 Administration

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/users` | User management | List users | Academic Head, School Admin. Accounts across all roles, with search and filters. |
| POST | `/api/users` | User management | Create a user | Same roles. Creates an account with a role. Audited. |
| PUT | `/api/users/{id}` | User management | Edit a user | Same roles. Updates name, email and role. Audited. |
| POST | `/api/users/{id}/active` | User management | Deactivate / reactivate | Same roles. The last active School Admin can't be deactivated. Audited. |
| POST | `/api/users/{id}/password` | User management | Reset a password | Same roles. Sets a new password for the user. Audited. |
| GET | `/api/parameters` | System parameters | Read parameters | School Admin. Seat cap, enrollment rules, institution name, faculty-loading settings, program heads, engine budgets, time slots, faculty ceilings, and warnings (sections above the cap, under-filled sections). |
| PUT | `/api/parameters/settings` | System parameters | Save settings | School Admin. Enlistment switch, max units per student, minimum section size, institution name, lab credit factor and engine budgets. Audited. |
| PUT | `/api/parameters/section-capacity` | System parameters | Set the seat cap | School Admin. Sets the institutional seat cap and reports sections already above it (never changes them). Audited. |
| POST | `/api/parameters/time-slots` | System parameters | Add a time slot | School Admin. Adds an allowable period. Audited. |
| PUT / DELETE | `/api/parameters/time-slots/{id}` | System parameters | Edit / remove a time slot | School Admin. Refused for a slot used by a published schedule. Audited. |
| PUT | `/api/parameters/faculty/{id}/load-limit` | System parameters | Set a faculty ceiling | School Admin. Sets a member's maximum units. Audited. |
| PUT | `/api/parameters/program-heads/{programCode}` | System parameters | Set a program head | School Admin. Assigns (or clears) the account that signs the faculty-loading form for a program. |
| GET | `/api/audit` | Audit trail | Read the audit trail | School Admin. Newest-first entries with action filter and paging. |
| GET | `/api/admin/outbox` | Email outbox | List queued emails | School Admin. Every email with status (queued / delivered / failed), filterable. |
| POST | `/api/admin/outbox/{id}/retry` | Email outbox | Retry one email | School Admin. Re-queues one failed email. |
| POST | `/api/admin/outbox/retry-failed` | Email outbox | Retry all failures | School Admin. Re-queues every permanently failed email. |
| GET | `/api/notifications` | Notifications, Notifications bell | List own notices | Any signed-in user. Own notices only, with unread count. |
| POST | `/api/notifications/{id}/read` | Notifications, Notifications bell | Mark one read | Any signed-in user. Own notices only. |
| POST | `/api/notifications/read-all` | Notifications, Notifications bell | Mark all read | Any signed-in user. |
| GET | `/api/nav/badges` | App shell (sidebar) | Work counters | Any signed-in user. Role-scoped counts for the active term: unread notices, pending approvals, registrations, term activations, transferee evaluations, open survey. |

### B.15 Rating survey

| Method | Endpoint | Used by page | Purpose | Description |
|---|---|---|---|---|
| GET | `/api/survey/{token}` | Rating survey (by link) | Load the survey | Public. The bilingual questionnaire for a valid, unanswered invitation link. |
| POST | `/api/survey/{token}` | Rating survey (by link) | Submit answers | Public. Saves the 1–5 ratings and comment once (409 on a second try); unknown questions are ignored. Audited. |
| GET | `/api/survey/mine` | Evaluation survey | Load own survey | Any signed-in user with an open invitation. |
| POST | `/api/survey/mine` | Evaluation survey | Submit own answers | Any signed-in user; once per invitation. Audited. |
| GET | `/api/admin/survey/audience` | Survey recipients | Recipient picker | **Super Admin.** Every account with its invitation status. |
| GET | `/api/admin/survey/invitations` | Rating survey (results) | List invitations | **Super Admin.** All invitations with status and who invited. |
| POST | `/api/admin/survey/invitations` | Survey recipients | Send invitations | **Super Admin.** Invites chosen users and/or roles via bell notice and/or emailed single-use link, with an optional note. Audited. |
| POST | `/api/admin/survey/invitations/remind` | Survey recipients, Rating survey (results) | Remind non-responders | **Super Admin.** Re-sends to those who haven't answered; a fresh link replaces the old one. |
| DELETE | `/api/admin/survey/invitations/{id}` | Survey recipients | Withdraw an invitation | **Super Admin.** Cancels an invitation so its link no longer works. |
| GET / POST | `/api/admin/survey/collection` | Survey recipients, Rating survey (results) | Collection window | **Super Admin.** Reads, or opens/closes, the collection window and sets the response goal. |
| GET | `/api/admin/survey/results` | Rating survey (results) | Usability report | **Super Admin.** Weighted means overall and per characteristic, with verbal interpretation and comments. |
| GET | `/api/admin/survey/results/export` | Rating survey (results) | Export responses | **Super Admin.** Raw responses as CSV (Excel-safe, Filipino text preserved). |
