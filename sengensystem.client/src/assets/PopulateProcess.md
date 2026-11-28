# SEN-GEN — Populating the System for AY 2026–2027, Second Semester

**Classes run Monday, January 11, 2027 to Saturday, June 12, 2027.** This guide lists, in order,
every piece of data SEN-GEN needs before students can enlist for that term, who enters it, and
where in the system it goes. Follow the phases in order: each one depends on the one before it.

> Prepared November 23, 2026. Target dates are recommendations working back from January 11;
> move them to fit the school calendar, but keep the order.

---

## At a glance

| Phase | What gets done | Who | Target dates |
|---|---|---|---|
| 0 | Go live, secure the admin accounts, create the School Admin | Super Admin | Nov 24 – Nov 27, 2026 |
| 1 | System parameters: institution, time slots, seat cap, enrollment rules | School Admin | Nov 30 – Dec 2 |
| 2 | Academic calendar: school year, both semesters, buildings, rooms | School Admin | Nov 30 – Dec 2 |
| 3 | Staff and faculty accounts, Program Heads, load ceilings | School Admin, Academic Head | Dec 1 – Dec 4 |
| 4 | Curricula, subjects, prerequisites, admission requirements | Academic Head, Registrar | Dec 1 – Dec 9 |
| 5 | Continuing students: import, confirm, student numbers, first-semester results | Registrar, Admission Officer | Dec 7 – Jan 6, 2027 |
| 6 | Class sections, faculty load, AI schedule generation, review, publish | Academic Head, Registrar | Dec 7 – Dec 18 (draft); publish by **Jan 4, 2027** |
| 7 | Open registration: SIS for new students and transferees, term activation for returning students | Academic Head, Admission Officer, Registrar | Dec 7, 2026 – Jan 8, 2027 |
| 8 | Enlistment: pre-authorize, open slot selection, approve requests | Admission Officer, Registrar, Academic Head | **Jan 4 – Jan 15, 2027** |
| 9 | Close enrollment, reports, backup | Academic Head, Registrar, Super Admin | Jan 16 – Jan 22, 2027 |

The offices are expected to be closed for the holidays around **Dec 24, 2026 – Jan 3, 2027**
(Christmas, Rizal Day on Dec 30, New Year's Day). Finish Phases 1–4 and the schedule draft before
the break so January is only publishing, enlistment, and approvals.

---

## Who does what

| Role | Responsible for | Signs in at |
|---|---|---|
| **Super Admin** | Everything a School Admin can do, plus the evaluation survey. Created automatically on first start | `/login` |
| **School Admin** | System parameters, academic calendar, buildings and rooms, user accounts, email outbox, audit trail | `/login` |
| **Academic Head** | Curricula and subjects, class sections, faculty load, schedule generation and review, enrollment stage | `/login` |
| **Registrar** | Confirming SIS registrations, imports, academic records, transferee evaluation, publishing schedules, enlistment approvals | `/login` |
| **Admission Officer** | Document checklists, official student numbers, term activations, pre-authorization | `/login` |
| **Faculty** | Checking their published schedule | `/login` |
| **Students** | SIS (`/register-sis`) or term activation (`/term-activation`), then enlistment | Public forms, then `/login` with the emailed credentials |

Menu names below are written as **Group → Page**, exactly as they appear in the left sidebar.

---

## Phase 0 — Go live and secure the accounts (Super Admin)

Target: **Nov 24 – Nov 27, 2026**. Detailed steps are in `deploy/deploymentplan.md` §6–§7.

1. Deploy the site and confirm the smoke tests in `deploy/deploymentplan.md` §6 Step 11 pass.
2. Sign in as the Super Admin with the seeded email and password. Change the password if a
   default was used.
3. Turn on two-factor authentication on **Profile settings** (a one-time code is emailed at each sign-in).
4. Create the School Admin account: **Administration → User management → New user**, role
   *School Admin*, with the real person's lowercase email and a strong temporary password.
   (The School Admin was deliberately not seeded; see `deploy/deploymentplan.md` §3.4.)
5. Send a test email (for example a password reset to your own account) and check
   **Administration → Email outbox** shows it as *Delivered*. Without a working Gmail app
   password, no account or enrollment emails go out.
6. Take the first database backup (`deploy/deploymentplan.md` §8) before entering any school data.

**Note on starter data.** A new database already contains three sample curricula (**ITP**,
**HRA**, **HRS**) with a few example subjects each, so the system is not empty on first start.
These are placeholders: Phase 4 replaces them with the real curricula.

---

## Phase 1 — System parameters (School Admin)

Target: **Nov 30 – Dec 2, 2026**. Page: **Administration → System parameters**.

1. **Institution** — set the institution name exactly as it should appear on emails, PDFs, and
   reports (for example *STI College Alaminos*).
2. **Allowable time slots** — define the teaching grid the AI scheduling feature may use, day by
   day (Monday–Saturday as the school holds classes) with start and end times. Delete or disable
   slots the school never uses (for example lunch hours). The engine only places classes inside
   these slots.
3. **Section seat cap** — the maximum number of students per section (default 40). Rooms smaller
   than this will hold fewer.
4. **Enrollment rules**
   - **Max units per student** — the institutional unit ceiling per term. `0` means no ceiling;
     only section capacity applies.
   - **Minimum section size** — sections below this are flagged in reports, never blocked
     (default 15).
   - Switch the **Enlistment open / closed** toggle to *closed* for now (it starts open); Phase 8
     turns it back on.
5. **Scheduling engine budgets** — leave the defaults (20 seconds, 2,000 thousand steps) unless a
   generation run in Phase 6 reports it ran out of time.
6. **Faculty-loading memo** — fill in the signatories and wording used on the faculty loading
   confirmation PDF. Its **Program Head** per program is picked in Phase 3, after the staff
   accounts exist.
7. **Faculty unit-load ceilings** are also set in Phase 3.

**Check:** the page saves without errors, and the time-slot list shows every teaching day.

---

## Phase 2 — Academic calendar, buildings, and rooms (School Admin)

Target: **Nov 30 – Dec 2, 2026**.

### 2.1 School year

**Academic setup → School years → New school year**

| Field | Value |
|---|---|
| Name | `AY 2026-2027` |
| Start date | The first day of the first semester (the school's actual date, for example August 2026) |
| End date | **June 12, 2027** |

If an *AY 2026-2027* entry already exists, edit its dates instead of creating a second one.
Set it as the **active** school year.

### 2.2 Semesters

**Academic setup → Semesters → New semester**. Create both terms of the school year:

| School year | Semester | Start date | End date | Active? |
|---|---|---|---|---|
| AY 2026-2027 | First Semester | The real first-semester start (Aug 2026) | The real first-semester end (Dec 2026) | No |
| AY 2026-2027 | **Second Semester** | **January 11, 2027** | **June 12, 2027** | **Yes** |

The name is generated automatically (*AY 2026-2027 — Second Semester*).

- The **First Semester** record is needed even though it ran outside SEN-GEN: Phase 5 records
  first-semester results against it, which the system uses for prerequisites and year levels.
- Set the **Second Semester** as **active**. Everything students see (registration, enlistment,
  schedules) follows the active semester. Only one semester is active at a time.
- A new semester starts in the **Preparation** stage, so students cannot register yet. That is
  intended until Phase 7.

### 2.3 Buildings

**Academic setup → Buildings → New building** — one entry per campus building (name and short
code, for example *Main Building*, `MB`). Every room must belong to a building.

### 2.4 Rooms

**Academic setup → Rooms → New room** — every room the school can teach in.

| Field | What to enter |
|---|---|
| Name | As students know it, for example `Room 301` |
| Building | The building from 2.3 |
| Capacity | Real seat count |
| Kind | *Lecture room*, *Computer laboratory*, or *Kitchen laboratory* |

- The campus has **one Computer Laboratory and one Kitchen Laboratory**. Set their kind correctly:
  the AI scheduling feature puts laboratory meetings only in a lab of the right kind, and never
  puts a pure lecture in a lab.
- A *Kitchen Laboratory* room may already exist from the starter data; edit it rather than adding
  a duplicate.

**Check:** every room has a building, a capacity, and the right kind.

---

## Phase 3 — Staff and faculty accounts (School Admin, Academic Head)

Target: **Dec 1 – Dec 4, 2026**. Page: **Administration → User management → New user**.

1. Collect the staff list: full name, real lowercase email, and role for each person.
2. Create one account per person with the matching role: *Academic Head*, *Registrar*,
   *Admission Officer*, or *Faculty member*. Give each a temporary password and tell them to
   change it at first sign-in.
3. For every faculty member, record the **employee ID** and **program**.
4. **Administration → System parameters → Faculty unit-load ceilings** — set each faculty
   member's maximum teaching load (default 24 units).
5. **Administration → System parameters → Faculty-loading memo** — pick the Program Head of each
   program (ITP, HRA, HRS, and any other program you add in Phase 4). They sign the faculty
   loading confirmation.
6. Ask every staff member to sign in once, change their password, and turn on two-factor
   authentication.

> **Known gap — fix before Phase 3.** Faculty members created through User management do not yet
> get the faculty profile the scheduling side needs. Without it, a new faculty member does not
> appear in **Faculty load**, in the unit-load ceilings, or in schedule generation. Only the demo
> faculty had profiles, and those are never seeded in production. This needs a small code change
> (create the profile when a *Faculty member* account is created or a user is switched to that
> role) and a redeploy before faculty accounts are entered.

**Check:** **Scheduling → Faculty load** lists every faculty member with their ceiling.

---

## Phase 4 — Curricula, subjects, and admission requirements (Academic Head, Registrar)

Target: **Dec 1 – Dec 9, 2026**.

### 4.1 Curricula (Academic Head)

**Academic setup → Subjects & curriculum**

1. For each program the school offers (ITP, HRA, HRS, …), open its curriculum in the left rail, or
   create one with **New curriculum** (program code, program name).
2. Tick **AY 2026-2027** under the effectivity school years. A program can have only one curriculum
   per school year.
3. Make sure the curriculum used this year is the **active** one for its program.

### 4.2 Subjects (Academic Head)

Inside each curriculum, add every subject of every year level and term, using the official
prospectus. Replace or delete the starter sample subjects.

| Field | What to enter |
|---|---|
| Code | Unique within the curriculum, for example `ITP103` |
| Title | Official subject title |
| Units | Credit units |
| Year level | 1, 2, or 3 (ITP and HRS are 2-year programs; HRA is 3 years) |
| Term | *First Semester* or *Second Semester* — the curriculum term, not a date |
| Delivery | *Lecture only*, *Laboratory only*, or *Lecture + laboratory* |
| Lecture hours / Lab hours | Weekly contact hours of each part. A lecture + laboratory subject becomes **two separate meetings** in the schedule |
| Lab room kind | *Computer laboratory* or *Kitchen laboratory*, for subjects with a lab part |
| Prerequisites | Tick each subject that must be passed first |

- Enter **all** terms, not only the Second Semester. Students' prospectus, year levels, and
  transferee evaluations read the whole curriculum.
- A prerequisite must sit in an earlier term. A same-term link is treated as a co-requisite, and
  both subjects must then be offered to the same section.

**Check:** **Enrollment → Curriculum prospectus** downloads a correct PDF for each program and
year level.

### 4.3 Admission requirements (Registrar or Admission Officer)

**Enrollment → Document requirements → Manage requirements**

1. Review the requirement catalog (for example Form 137, PSA birth certificate, good moral
   certificate, health papers).
2. Set which programs each requirement applies to. ITP students are exempt from the health papers.
3. Archive any requirement the school no longer collects.

Missing documents do not block enlistment; they are tracked so the Admission Office can follow up.

---

## Phase 5 — Continuing students and first-semester results (Registrar, Admission Officer)

Target: **Dec 7, 2026 – Jan 6, 2027**. This is the largest data-entry phase. Students who were
enrolled in the first semester were not in SEN-GEN, so their records must be brought in once.

### 5.1 Import the continuing-student list (Registrar)

**Enrollment → Pre-enrollment import**

1. Click **Download template** and fill one row per continuing student. Columns: `StudentType`,
   `Program`, `LastName`, `FirstName`, `MiddleName`, `DateOfBirth`, `Gender`, `CivilStatus`,
   `Email`, `MobileNumber`, `AddressLine`, `Barangay`, `CityMunicipality`, `Province`, `ZipCode`,
   `LastSchoolLevel`, `SchoolName`, `YearGradeLastAttended`, `LastTerm`, `GuardianRelationship`,
   `GuardianName`, `GuardianMobile`.
2. Use each student's **real, personal email**. Their login and every notice go there, and an
   email can belong to only one student.
3. Upload the file. Valid rows load; invalid rows and duplicates (same email, or same name and
   birth date) are listed with the reason. Fix those rows and re-upload them.

### 5.2 Confirm the registrations (Registrar)

**Enrollment → Registration (SIS)** — open each imported record, check it, and **Confirm** it.
Only confirmed registrations can be pre-authorized and enlist.

### 5.3 Record official student numbers (Admission Officer)

**Enrollment → Assign student number** — enter the official student number issued by the
school's student system for each confirmed student. Returning students use this number (or their
SEN-GEN registration number) to activate the term.

### 5.4 Record first-semester results (Registrar or Academic Head)

**Enrollment → Academic records** — as soon as first-semester grades are final.

1. Click **Download template (.xlsx)** and fill one row per student per subject: `StudentNumber`,
   `SubjectCode`, `Term` (write the exact name **AY 2026-2027 — First Semester**), `Verdict`
   (*Passed*, *Failed*, or *Dropped*), and optional `Remarks`.
2. Upload it. Re-importing the same student, subject, and term updates the row instead of failing.
3. Include earlier terms too for second- and third-year students, so prerequisites from their
   first year are satisfied.

Only *Passed* earns credit and satisfies a prerequisite. A student with no recorded result for a
prerequisite cannot enlist in the subject that needs it.

**Check:** a sample of students shows the right subjects on **Enlistment → My subjects** once they
sign in (Phase 7).

---

## Phase 6 — Sections, faculty load, and the class schedule (Academic Head, Registrar)

Target: draft by **Dec 18, 2026**; publish by **Jan 4, 2027**.

### 6.1 Class sections (Academic Head)

**Academic setup → Class sections → New section** — one block per program, year level, and
section for the **Second Semester**, for example *ITP 1-A*, *ITP 2-A*, *HRA 1-A*, *HRA 1-B*.

| Field | Value |
|---|---|
| Semester / term | AY 2026-2027 — Second Semester |
| Course | Program code |
| Curriculum | The AY 2026-2027 curriculum from Phase 4 |
| Year level | 1, 2, or 3 |
| Section | A, B, … |

Size the number of sections from the expected head count divided by the seat cap from Phase 1.

### 6.2 Faculty load (Academic Head)

**Scheduling → Faculty load** — select *AY 2026-2027 — Second Semester*.

1. For each faculty member, open the assignment window and tick the subjects and sections they
   will teach. The running total must stay within their unit ceiling.
2. Every second-semester subject of every section needs a faculty member. Generation refuses to
   run while any section is missing one.
3. Open **Preferences** for each faculty member and record their preferred teaching times. The AI
   scheduling feature tries to honour them.

### 6.3 Generate the schedule (Academic Head)

**Scheduling → Generate schedule**

1. Check the semester is *AY 2026-2027 — Second Semester*.
2. Review the soft-constraint weights (faculty preference, idle gaps, room fit). The defaults are
   a good start.
3. Click **Generate**. The engine places every meeting into a time slot and a room of the right
   kind with no clashes for rooms, faculty, or sections.
4. If it stops with a message, fix the cause and run it again. Common causes:
   - a section without a faculty allocation (6.2);
   - no time slots after the configured class start time (Phase 1);
   - a prerequisite placed in the same or a later term (Phase 4.2);
   - not enough lab hours: only one Computer Lab and one Kitchen Lab exist, so too many lab
     meetings cannot fit.

### 6.4 Review, adjust, and finalize (Academic Head)

1. **Scheduling → Review & overrides** — inspect the result by section, faculty, and room, and
   apply manual changes where needed. Every override is checked for conflicts.
2. **Scheduling → Schedule board** — drag-and-drop adjustments on a weekly calendar, with each
   meeting's plotted hours against the required hours.
3. When it is right, go back to **Scheduling → Generate schedule** and click **Finalize**.
   Finalizing locks the draft against regeneration and edits until it is reopened, and shows
   *Finalized · ready to publish*.

### 6.5 Publish (Registrar)

**Scheduling → Schedule publishing** — publish *AY 2026-2027 — Second Semester* by **Jan 4, 2027**.
Faculty and confirmed students are notified and the schedule becomes official. A change after
publishing is an amendment: the affected faculty and enlisted students are notified again.

**Check:** faculty see their classes on **Scheduling → My schedule**;
**Administration → Faculty load reports** downloads each faculty member's load workbook.

---

## Phase 7 — Open registration (Academic Head, Admission Officer, Registrar)

Target: **Dec 7, 2026 – Jan 8, 2027**.

1. **Move the term to Registration (Academic Head).** Click **Enrollment stage** in the top bar
   and advance *AY 2026-2027 — Second Semester* from *Preparation* to *Registration*. The stages
   run Preparation → Registration → Document submission → Subject enlistment → Enrollment closed.
2. **Open term activation (Registrar or Academic Head).** **Enrollment → Term activation
   control → Open activation**.
3. **Announce the public links** through the school's usual channels:
   - New students and transferees: `https://<site>/register-sis` (digital Student Information
     Sheet). Each submission creates the student's login and emails a temporary password.
   - Returning students (everyone imported in Phase 5): `https://<site>/term-activation`, with
     their student number and last name.
4. **Validate term activations (Admission Officer).** **Enrollment → Term activations** — approve
   each request. Check the year level shown: it stays the same within a school year, so a student
   in year 2 for the first semester stays in year 2. Correct it where needed. Approval creates the
   student's login (if they have none) and emails it.
5. **Confirm new SIS submissions (Registrar).** **Enrollment → Registration (SIS)** — review and
   confirm, then assign official student numbers (**Enrollment → Assign student number**).
6. **Evaluate transferees (Registrar, Admission Officer, or Academic Head).** **Enrollment →
   Evaluate transferee** — rule on every subject (*Credited*, with the source subject and grade, or
   *To take*) and complete the evaluation. It sets the transferee's year level. Until it is
   complete, the transferee cannot enlist.
7. **Track documents (Admission Officer).** When submissions slow down, advance the stage to
   *Document submission* and record what each student hands in on **Enrollment → Document
   requirements**. Send reminders from the same page.

---

## Phase 8 — Enlistment (Admission Officer, Registrar, Academic Head)

Target: **Jan 4 – Jan 15, 2027** (one week before classes plus the first week for adds and drops).

1. **Pre-authorize (Admission Officer or Registrar).** **Enrollment → Pre-authorization** —
   authorize every confirmed student who may select subjects. A student needs a confirmed SIS
   (and, for a transferee, a completed evaluation).
2. **Open enlistment (Academic Head, School Admin).**
   - Advance the **Enrollment stage** to *Subject enlistment*.
   - Switch the toggle to **Enlistment open** in **Administration → System parameters →
     Enrollment rules**.
   Both are needed; students see which one is closed if either is.
3. **Students enlist.** On **Enlistment → Subject enlistment**, students see published sections
   with live seat counts and request seats. The system checks prerequisites, unit ceilings, and
   time conflicts.
4. **Approve requests (Registrar).** **Enlistment → Enlistment approvals** — approve or decline
   each request, adding a note when declining. Approval takes the seat and emails the student.
   Students then download their Certificate of Registration from **Enlistment → My subjects** and
   settle fees at the cashier as usual.
5. **Watch the numbers daily.** **Administration → Reports & analytics** (enlistment, sections
   below the minimum size, faculty load) and **Administration → Room utilization**.

---

## Phase 9 — Close enrollment (Academic Head, Registrar, Super Admin)

Target: **Jan 16 – Jan 22, 2027**, after the add/drop period.

1. Advance the **Enrollment stage** to *Enrollment closed*. Registration and enlistment stop.
2. Close term activation (**Enrollment → Term activation control**).
3. Download the final reports: **Reports & analytics**, **Faculty load reports** (bulk download),
   and **Room utilization**.
4. Take a full database backup labelled with the term, for example
   `sengen_2026-2027_2ndSem_enrollment.bak` (`deploy/maintenanceplan.md` §6).

At the end of the term (after **June 12, 2027**), record second-semester results in **Academic
records**. They drive prerequisites and year levels for AY 2027–2028.

---

## Go-live checklist

- [ ] Admin accounts secured with two-factor authentication; School Admin created (Phase 0)
- [ ] Test email delivered; backup taken (Phase 0)
- [ ] Institution name, time slots, seat cap, enrollment rules saved; enlistment toggle closed (Phase 1)
- [ ] AY 2026-2027 with both semesters; Second Semester active, Jan 11 – Jun 12, 2027 (Phase 2)
- [ ] All buildings and rooms, with the Computer Lab and Kitchen Lab set to the right kind (Phase 2)
- [ ] Faculty-profile gap fixed and redeployed (Phase 3)
- [ ] All staff accounts; load ceilings and Program Heads set (Phase 3)
- [ ] Real curricula and subjects for every program; sample subjects removed (Phase 4)
- [ ] Admission requirements per program reviewed (Phase 4)
- [ ] Continuing students imported, confirmed, numbered; first-semester results recorded (Phase 5)
- [ ] Sections and faculty load complete; schedule generated, finalized, and published by Jan 4 (Phase 6)
- [ ] Stage at Registration; term activation open; public links announced (Phase 7)
- [ ] Pre-authorization done; stage at Subject enlistment; enlistment toggle open (Phase 8)

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| A student says registration is closed | The term is still in *Preparation* or already *Closed* | Advance the **Enrollment stage** |
| "Term activation is currently closed" | Activation switched off | **Term activation control → Open activation** |
| "We couldn't find a matching student record" | Student number or last name does not match the imported record | Check the record on **Registration (SIS)**; the last name must match exactly |
| A student cannot see any sections | Not pre-authorized, enlistment not open, schedule not published, or a transferee not yet evaluated | Check Phases 6.5, 7.6, 8.1, and 8.2 |
| A subject is missing from a student's choices | A prerequisite has no *Passed* result | Record the result in **Academic records** |
| A faculty member is not listed in Faculty load | No faculty profile (see the Phase 3 gap) | Apply the fix; until then they cannot be assigned |
| Generation stops with a message | See Phase 6.3 | Fix the cause it names and run again |
| No emails arrive | Gmail app password missing or wrong | **Email outbox** shows the failure; see `deploy/deploymentplan.md` §11 |
