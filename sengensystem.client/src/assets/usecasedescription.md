# SEN-GEN — Use Case Descriptions (per user role)

Covers every use case from the student's first registration to the last step SEN-GEN handles
(the Certificate of Registration, before the student goes to the cashier). Sources:
`SEN-GEN_Revised_Enrollment_v22.docx`, `RequirementsSpecifications.md`, and the role guards in
the current code (sidebar `nav.js` + server `RequireRole` checks).

Use cases marked **(Proposed)** are not built yet. They come from the gaps listed in
`enlistment-usecases.md`.

---

## 1. Actors

| Actor | Who they are | Main responsibility in SEN-GEN |
|---|---|---|
| **Guest / Applicant** | Prospective or returning student without a signed-in session | Files the SIS or a term activation on the public pages |
| **Student** | Enrollee with a provisioned account | Tracks requirements, enlists in subjects, views their schedule, prints their COR |
| **Admission Officer** | Admission Office staff | Requirements checklist, official student numbers, term-activation validation, pre-authorization |
| **Registrar** | Registrar's Office staff | Confirms SIS, imports pre-enrollment lists, evaluates transferees, keeps academic records, publishes schedules, approves slot requests |
| **Academic Head** | Dean / program head | Curriculum, class sections, faculty load, schedule generation and finalization, enrollment stage, user accounts |
| **Faculty Member** | Teaching staff | Views their teaching schedule and seat counts, receives schedule notices |
| **School Admin** | Institutional system administrator | Academic setup, system parameters, users, audit trail, email outbox. Can reach every function |
| **Super Admin** | Top-level administrator / researcher | Everything the School Admin can do, plus the ISO 25010 evaluation survey and School Admin accounts |
| **Email service** *(system actor)* | Gmail SMTP via the outbox | Delivers credentials, confirmations, reminders, approvals, publication notices |
| **Cashier** *(external)* | Existing cashiering process | Takes tuition after enlistment. Outside SEN-GEN's scope |

## 2. The enrollment lifecycle and the use cases at each step

The active term moves through five stages (FR-CYC):
**Preparation → Registration → Document submission → Subject enlistment → Enrollment closed.**

| # | Step | Use cases |
|---|---|---|
| 0 | **Term preparation** (before students arrive) | SA-01–SA-04, AH-01–AH-04, AH-12, ADM-01 |
| 1 | **Registration**: new students file the SIS, returning students activate | GST-01, GST-02, ALL-01, REG-01, REG-02, ADM-04, ADM-05 |
| 2 | **Document submission** and admission checks | STU-03, STU-04, ADM-02, ADM-03, REG-03, REG-04 |
| 3 | **Scheduling**: build, finalize and publish the timetable | AH-05–AH-08, REG-05 |
| 4 | **Pre-authorization**: clear students for online enlistment | ADM-06 |
| 5 | **Subject enlistment**: choose subjects and sections, get approved | STU-06–STU-10, REG-06–REG-10 |
| 6 | **Enrollment confirmation**: enrolled status and COR | STU-11, REG-11 |
| 7 | **Hand-off to the cashier** (outside SEN-GEN) | — |
| 8 | **During the term**: schedule changes, faculty view, monitoring | AH-09, FAC-01–FAC-03, REG-12, SA-06, SA-07 |
| 9 | **End of term**: record verdicts, roll over, archive | REG-13, SA-08, SA-09 |
| 10 | **System evaluation** (research) | ALL-07, SUP-01, SUP-02 |

---

## 3. All signed-in users (common use cases)

### ALL-01 — Sign in
**Actors:** every role · **Related:** FR-AUTH-05, FR-AUTH-12, FR-AUTH-13
**Description:** A user signs in with email and password to reach their role's functions.
**Preconditions:** An active account exists.
**Main flow:**
1. User opens `/login` and enters email and password.
2. System verifies the password hash and that the account is active.
3. If two-factor authentication is on, system emails a one-time code and the user enters it.
4. System issues a session token carrying the user's role and opens the dashboard.
**Alternative / exception flows:**
- 2a. Wrong credentials: generic "invalid email or password". The failed attempt is audited.
- 2b. Account deactivated: sign-in refused.
- 3a. Wrong or expired code: user retries or requests a new code.
- 4a. **First sign-in with a temporary password** (`MustChangePassword`): user is sent to the forced password change and can't go anywhere else until it's done.
**Postconditions:** User is signed in. `LoginSucceeded` / `LoginFailed` is audited.

### ALL-02 — Recover a forgotten password
**Actors:** every role, Email service · **Related:** FR-AUTH-10
**Description:** A user who forgot their password resets it without staff help.
**Preconditions:** None (public page).
**Main flow:**
1. User opens `/forgot-password` and enters their email.
2. System always shows the same neutral message. If the account exists, it emails a single-use link valid for 60 minutes.
3. User opens the link and enters a new password that meets the policy (8+ characters, letters and digits).
4. System saves the new hash and invalidates the token.
**Alternative / exception flows:**
- 3a. Link expired or already used: user starts again.
- 3b. Password fails the policy: validation message.
**Postconditions:** Password changed, and audited.

### ALL-03 — Manage own profile
**Actors:** every role · **Related:** FR-AUTH-11, FR-AUTH-12, FR-AUTH-14
**Description:** Change name, password, email address (confirmed by link), and two-factor setting.
**Main flow:**
1. User opens `/profile`.
2. User edits their name and saves, **or** changes their password (current + new), **or** requests an email change.
3. For an email change, system emails a confirmation link to the **new** address. The change only takes effect when that link is opened (valid 24 h).
4. User can turn two-factor authentication on or off.
**Alternative / exception flows:**
- 3a. New email already in use: refused.
- 3b. Confirmation link expired: the old email stays.
**Postconditions:** Profile updated and audited. A student also sees a read-only **Student record** card (official student number, program, year level).

### ALL-04 — Read notifications
**Actors:** every role · **Related:** FR-NOTIF-03, FR-NOTIF-04
**Main flow:**
1. The top-bar bell shows the unread count (refreshed every 60 s).
2. User opens the dropdown or `/notifications`, reads a notice, and follows its link.
3. User marks one or all notices as read.
**Postconditions:** Notices marked read. Sidebar badges show outstanding work for the role.

### ALL-05 — View the dashboard and enrollment stage
**Actors:** every role · **Related:** FR-DASH, FR-CYC-02
**Description:** The dashboard is different for each role. Staff see semester metrics, a student sees their enrollment journey, a faculty member sees their week. The enrollment-stage ticker is shown to everyone.

### ALL-06 — Set preferences and read help
**Actors:** every role
**Description:** `/settings` holds device preferences (theme, density, motion, 12/24-hour time, sidebar). `/help` explains the enrollment journey, the roles, and an FAQ.

### ALL-07 — Answer the evaluation survey
**Actors:** invited users (any role) · **Related:** FR-EVAL-01–03
**Preconditions:** The Super Admin invited this user (SUP-01).
**Main flow:**
1. User opens the single-use link from the email (no sign-in needed) or the sidebar entry.
2. User rates each ISO 25010 item on a 5-point scale, in English or Filipino, and adds comments.
3. User submits.
**Alternative / exception flows:**
- 1a. Link already used: "already answered".
**Postconditions:** Response stored. The invitation is used up.

### ALL-08 — Sign out
**Actors:** every role · Ends the session.

---

## 4. Guest / Applicant (public pages, no account yet)

### GST-01 — Submit the digital Student Information Sheet (SIS)
**Actors:** Guest (new student or transferee), Email service · **Related:** FR-SIS-01–07, FR-SIS-14, FR-AUTH-02, FR-AUTH-13, FR-DOC-01
**Description:** A new enrollee or transferee registers by filling in the digital SIS. This replaces the paper form.
**Preconditions:** The active term is in **Registration** or **Document submission** stage.
**Main flow:**
1. Applicant opens `/register-sis`.
2. Applicant selects student type (New / Transferee) and program (e.g. HRA, HRS, ITP).
3. Applicant fills in personal, family/guardian and previous-school details. Every text field is converted to ALL CAPS as they type.
4. Applicant picks the permanent address with the cascading picker (province → city/municipality → barangay) and types house/street and zip code.
5. Applicant opens the terms and conditions, scrolls to the end and agrees.
6. Applicant submits.
7. System validates every field (required fields, email and date formats, completeness).
8. System saves the registration with the agreement timestamp, issues a registration number, sets year level 1, and seeds the document checklist for the applicant's program and student type.
9. System provisions a Student account with a temporary password (`SenGen-NNNNNN`) and emails the login details and a registration confirmation.
**Alternative / exception flows:**
- 1a. Stage is Preparation or Enrollment closed: the form refuses submissions and says why.
- 5a. Terms not accepted: submit stays disabled.
- 7a. Validation fails: each field shows its prompt, and nothing is saved.
- 7b. Email already registered: "This email is already registered to the system."
- 8a. Same name and birth date as another registration: saved, but **flagged** to the Registrar as a possible duplicate.
- 9a. Email delivery fails: the message waits in the outbox and is retried (SA-07).
**Postconditions:** Registration status *Submitted*. Student account exists with `MustChangePassword`. `AccountRegistered` audited.

### GST-02 — Request term activation (returning student)
**Actors:** Guest (returning student) · **Related:** FR-SIS-10–12
**Description:** A student from a previous term asks to be activated for the current term.
**Preconditions:** The term-activation window is open (REG-14 / AH-11). The student has a prior registration.
**Main flow:**
1. Student opens `/term-activation` and enters their **official student number** and **last name**.
2. System finds the record (falls back to the registration number if no official number was ever issued).
3. System shows the term and the **year level** the student will enter: derived from units earned, or from the school-year change if there is no history.
4. Student confirms both and submits.
**Alternative / exception flows:**
- 1a. Window closed: refused with a plain message.
- 2a. No match: "record not found". The student checks their details or goes to the Admission Office.
- 2b. Already activated or a request is already pending for this term: told so.
**Postconditions:** Term activation *Pending*, waiting for ADM-05.

---

## 5. Student

### STU-01 — Change the temporary password at first sign-in
**Actor:** Student · **Related:** FR-AUTH-13
**Preconditions:** Account provisioned by GST-01 or ADM-05.
**Main flow:** 1. Student signs in with the emailed temporary password. 2. System forces the password change screen. 3. Student sets a new password that meets the policy. 4. System clears `MustChangePassword` and opens the dashboard.
**Postconditions:** Account ready to use.

### STU-02 — Link the account to a student record
**Actor:** Student · **Related:** FR-ENL-05
**Description:** Used when an account isn't already tied to its SIS record (older or imported records).
**Main flow:** 1. Student opens the claim card under Document requirements. 2. Enters their student number and date of birth. Their account email must match the SIS email. 3. System checks all three facts and links the record.
**Alternative / exception flows:** 3a. Any fact mismatches: refused without saying which one. 3b. Record already linked to another account: refused.
**Postconditions:** Account linked. `StudentAccountLinked` audited.

### STU-03 — View and correct own SIS
**Actor:** Student · **Related:** FR-SIS-13
**Main flow:** 1. Student opens `/my-registration`. 2. While the SIS is not yet confirmed, they correct their names, birth details, mobile number, address or guardian, and save.
**Alternative / exception flows:** 2a. Already confirmed by the Registrar: the form is read-only. 2b. Program, student type and email are never student-editable. They are corrected by the Registrar (REG-01).
**Postconditions:** SIS updated and audited.

### STU-04 — Track document requirements
**Actor:** Student · **Related:** FR-DOC-03
**Main flow:** 1. Student opens `/documents`. 2. System lists the papers their program and student type require, with each one's status (pending, original, photocopy, certificate of grades) and which ones are needed for pre-authorization.
**Postconditions:** None (read-only). The student brings any missing papers to the Admission Office.

### STU-05 — Follow the enrollment journey
**Actor:** Student · **Related:** FR-CYC-02, FR-ENL-18
**Description:** The dashboard shows the student's own steps: SIS submitted → confirmed → documents → transferee evaluation (transferees only) → pre-authorization → enlistment → enrolled. Each step shows its state and what is still blocking it.

### STU-06 — View my subjects for the term
**Actor:** Student · **Related:** FR-ENL-13, FR-RPT-06
**Main flow:** 1. Student opens `/my-subjects`. 2. System shows the subjects their curriculum, year level and term require, including back subjects and repeats, minus anything passed or credited. 3. Student can download the curriculum prospectus as a PDF.

### STU-07 — Browse published sections
**Actor:** Student · **Related:** FR-ENL-01, FR-ENL-02, FR-ENL-13
**Preconditions:** Schedule published for the active term.
**Main flow:**
1. Student opens `/enlistment`.
2. System shows the **plan checklist**: every subject owed, its units, how many published classes it has, and the student's request status for it.
3. Under each subject, system shows its sections with day/time, room, faculty and **live seats left**.
4. Student can turn on "Show every section" to see the wider catalog (view only).
**Alternative / exception flows:**
- 2a. Not yet eligible: the page lists each blocker (SIS not confirmed, not pre-authorized, transferee not evaluated, account not linked).
- 2b. No curriculum for the program: the page shows a notice and lists all published sections.
- 3a. A subject has no published section yet: shown as "no class yet".
**Postconditions:** None.

### STU-08 — Request a seat in a section (slot selection)
**Actor:** Student · **Related:** FR-ENL-03–09, FR-ENL-14, FR-ENL-16, FR-CYC-05
**Description:** The student asks for a seat in one section of a subject they owe. The Registrar approves it later.
**Preconditions:** Signed in, linked, SIS confirmed, pre-authorized, transferee evaluation completed (if a transferee). Stage is *Subject enlistment* and the enlistment switch is on.
**Main flow:**
1. Student clicks **Request seat** on a section.
2. System rechecks eligibility and the enlistment window.
3. System checks that the subject is in the student's plan.
4. System checks that every prerequisite has been passed (when the student has academic history on file).
5. System checks that the section is published, the student has no other live request for the same subject, the unit ceiling isn't exceeded, the section isn't full, and nothing overlaps their other requested or approved classes.
6. System saves the request as *Requested*, notifies every Registrar, and refreshes live counts.
7. Student sees "Seat requested — awaiting Registrar approval".
**Alternative / exception flows:**
- 2a. Not eligible / window closed: refused, naming the blocker or the switch.
- 3a. Subject not in plan: "CS101 is not one of your subjects this term…"
- 4a. Prerequisite not passed: refused, naming the missing subjects (`PrerequisiteBlocked` audited).
- 4b. **Co-requisite** (same year level and term): met if passed **or** the student already holds a requested/approved seat in it this term. Otherwise refused with "taken together with X — request a seat in X first".
- 5a. Already requested this section or subject: refused, naming the existing request.
- 5b. Over the unit ceiling: refused, naming the ceiling.
- 5c. Section full: "pick another section".
- 5d. Time overlap: refused, naming the clashing class and time.
- 6a. Double-click or second tab: only one request is created.
**Postconditions:** Request *Requested*. `SlotRequested` audited.

### STU-09 — Cancel a pending request
**Actor:** Student · **Related:** FR-ENL-04
**Main flow:** 1. Student opens their requests and cancels one that is still *Requested*. 2. System marks it *Cancelled*.
**Alternative / exception flows:** 1a. Already decided: "nothing to withdraw".
**Postconditions:** Request *Cancelled*. Audited. Allowed in any stage.

### STU-10 — Drop an approved seat
**Actor:** Student · **Related:** FR-ENL-04
**Preconditions:** The enlistment window is open.
**Main flow:** 1. Student drops an approved class and can give a reason. 2. System marks it *Dropped* and gives the seat back.
**Alternative / exception flows:** 1a. Window closed: student is told to ask the Registrar (REG-08).
**Postconditions:** Seat released. `SlotDropped` audited. The class leaves My schedule.

### STU-11 — Confirm enrollment and download the Certificate of Registration (COR)
**Actor:** Student · **Related:** FR-ENL-18, FR-RPT-06
**Main flow:** 1. System shows **Enrolled** once every subject in the plan has an approved seat. 2. Student downloads the COR PDF for the term (subjects, sections, schedule, enrolled state, amendment flags). 3. Student proceeds to the cashier (outside SEN-GEN).
**Alternative / exception flows:** 1a. Plan incomplete: status reads *not yet enrolled* and lists what's missing. 1b. Empty plan: *no subjects due*.
**Postconditions:** End of SEN-GEN's part of enrollment.

### STU-12 — View my class schedule
**Actor:** Student · **Related:** FR-PUB-02
**Main flow:** Student opens `/schedule` and sees a weekly calendar and a day-by-day list of approved classes, with room, faculty and amendment marks.

### STU-13 — Receive enrollment notices
**Actor:** Student, Email service · **Related:** FR-NOTIF-01
**Description:** Email and bell notices for account credentials, SIS confirmation, document reminders, transferee evaluation completed, seat approved / rejected / released, schedule published, class amended.

### Student — proposed
- **STU-P1 Swap section (Proposed):** request section B of a subject while keeping the seat in A until B is approved.
- **STU-P2 Join a waitlist (Proposed):** queue for a full section and be offered the seat when one frees up.
- **STU-P3 Petition a subject (Proposed):** ask for an advance subject, an overload, or a class for a subject with no published section.
- **STU-P4 Withdraw from the term (Proposed):** release every seat at once, with a reason.

---

## 6. Admission Officer

### ADM-01 — Maintain the admission requirements catalog
**Actors:** Admission Officer (also Registrar, School Admin) · **Related:** FR-DOC-06–09
**Main flow:**
1. Officer opens **Manage requirements** on the Document Requirements page.
2. Officer adds, renames or reorders a requirement, choosing which programs and student types (new / transferee) it applies to, whether it is required for pre-authorization, and whether it accepts a certificate of grades.
3. Officer archives a requirement that's no longer needed.
**Alternative / exception flows:** 3a. Archiving is soft: existing checklists keep their history.
**Postconditions:** New checklists use the updated catalog.

### ADM-02 — Record document submissions
**Actors:** Admission Officer (also Registrar) · **Related:** FR-DOC-02, FR-DOC-03
**Main flow:** 1. Officer opens `/documents` and filters complete / incomplete. 2. Opens an enrollee and sets each paper's status. 3. Saves.
**Alternative / exception flows:** 2a. A status the requirement doesn't accept (e.g. a photocopy where only a certificate of grades is allowed) is refused.
**Postconditions:** Checklist updated. `DocumentChecklistUpdated` audited. Completion rate updates on the dashboard.

### ADM-03 — Send document reminders
**Actors:** Admission Officer (also Registrar), Email service · **Related:** FR-DOC-05, FR-TERM-05
**Main flow:** 1. Officer sends reminders to every incomplete checklist of the active term, or to one enrollee. 2. System emails each person the exact papers they're missing.
**Postconditions:** Emails queued in the outbox and audited.

### ADM-04 — Assign the official student number
**Actor:** Admission Officer · **Related:** FR-SIS-08
**Main flow:** 1. Officer opens `/assign-student-number`, finds the registration, and enters the number issued by the separate student-records system. 2. System saves it.
**Alternative / exception flows:** 1a. Number already used by another student: refused.
**Postconditions:** The number appears on the student's record card and is used for term activation.

### ADM-05 — Validate a term activation
**Actor:** Admission Officer · **Related:** FR-SIS-09–11
**Preconditions:** A pending request from GST-02.
**Main flow:**
1. Officer opens `/term-activations` (badge-counted).
2. Officer reviews the student, the term, and the derived year level.
3. Officer approves it, overriding the year level if needed (repeater, back from leave), or rejects it.
4. System updates the student's year level for the term, provisions a login if the student never had one, and emails the decision.
**Postconditions:** Student active for the term. `YearLevelAssigned` audited if the year level changed.

### ADM-06 — Pre-authorize a student for online enlistment
**Actors:** Admission Officer (also Registrar) · **Related:** FR-PRE-02, FR-PRE-04
**Preconditions:** SIS confirmed by the Registrar. Papers flagged "required for authorization" are on file.
**Main flow:** 1. Officer opens `/pre-authorization` and sees each student's Ready / Blocked state. 2. Officer clears a Ready student. 3. The student's enlistment gate opens.
**Alternative / exception flows:**
- 2a. Blocked: the server refuses and names the missing paper or the unconfirmed SIS.
- 2b. **Revoke:** the officer can withdraw an authorization (audited).
**Postconditions:** `IsPreAuthorized` set. `StudentPreAuthorized` audited.

### ADM-07 — Evaluate transferee credits
Shared with the Registrar. See **REG-04**.

### ADM-08 — Download the curriculum prospectus
**Actors:** Admission Officer, Registrar, Academic Head · **Related:** FR-RPT-06
**Description:** Print a year level's subject list for a program, for walk-in advising.

---

## 7. Registrar

### REG-01 — Review, correct and confirm SIS registrations
**Actor:** Registrar · **Related:** FR-SIS-04, FR-SIS-05, FR-SIS-14
**Main flow:**
1. Registrar opens `/registrations` (queue follows the active term; search reaches every term).
2. Opens a registration and checks it against the documents, including the possible-duplicate flag.
3. Corrects any field, including program, student type and email.
4. Confirms the registration.
5. System emails the confirmation to the student.
**Alternative / exception flows:**
- 2a. True duplicate: Registrar rejects it or keeps one record.
- 3a. Program changed: the student's checklist and subject plan follow the new program.
**Postconditions:** Status *Confirmed*. The SIS becomes read-only to the student.

### REG-02 — Import a pre-enrollment list (.xlsx)
**Actor:** Registrar · **Related:** FR-PRE-01, FR-PRE-03
**Main flow:**
1. Registrar downloads the template from `/pre-enrollment`, fills it in, and uploads it.
2. System extracts and validates each row (required fields, formats, enum values), transforms it (names, student numbers, checklists), and loads it with duplicate detection (by email, and by name + birth date, within the file and against the database).
3. System shows a row-by-row report.
**Alternative / exception flows:** 2a. Invalid or duplicate rows are listed with the reason. Valid rows are still loaded. Re-importing the same file creates nothing new.
**Postconditions:** Imported students go through the normal confirm → documents → pre-authorization steps. `PreEnrollmentImported` audited.

### REG-03 — Pre-authorize a student
Same as **ADM-06**. The Registrar holds the same function.

### REG-04 — Evaluate a transferee's credits
**Actors:** Registrar, Admission Officer, Academic Head · **Related:** FR-TRAN-01–03, FR-ENL-12
**Preconditions:** Registration of type Transferee.
**Main flow:**
1. Evaluator opens `/evaluate-transferee` and picks a transferee.
2. System shows the full curriculum (pinned to the version in force), term block by term block.
3. For each subject, evaluator marks **Credited** (entering the previous school's subject and grade) or **To take**. "Credit all" / "All to take" speed up whole blocks.
4. Evaluator saves progress (*In progress*) as often as needed.
5. When every subject has a decision, evaluator clicks **Complete**.
6. System derives the year level from credited units. The evaluator can override it.
7. System opens the transferee's enlistment gate, notifies the student, and offers the sheet as a printable PDF to sign.
**Alternative / exception flows:**
- 5a. Undecided subjects: Complete is blocked.
- 7a. **Reopen** for a correction: decisions are kept and the enlistment gate closes again until it's completed.
**Postconditions:** Evaluation *Completed*. Credited subjects drop out of the student's plan and "to take" subjects are added. Audited.

### REG-05 — Publish the schedule
**Actors:** Registrar (also School Admin), Email service · **Related:** FR-PUB-01–05
**Preconditions:** The Academic Head has finalized the draft (AH-07).
**Main flow:**
1. Registrar opens `/publishing`.
2. Registrar reviews the **preview**: number of classes, faculty and students to be notified.
3. Registrar confirms.
4. System marks the term's draft rows as published and emails every assigned faculty member and confirmed student.
5. Registrar can view and print the schedule **by class, by week, or by day**.
**Alternative / exception flows:** 1a. Draft not finalized: publishing is refused (409). 4a. Some emails fail: they are retried from the outbox. Publication still stands.
**Postconditions:** Students can browse sections. `SchedulePublished` audited.

### REG-06 — Review and decide slot requests
**Actor:** Registrar (also School Admin) · **Related:** FR-ENL-04, FR-ENL-11, FR-ENL-16
**Main flow:**
1. Registrar opens `/approvals` (badge-counted, active term only) and filters or sorts by seats left, student or subject.
2. Registrar approves a request, a student's whole load, a selection, or the entire pending queue.
3. For each request, system rechecks prerequisites, time overlap and seats. It takes the seat under concurrency control and notifies the student.
4. Registrar rejects a request with a reason. The student is notified by email and bell.
**Alternative / exception flows:**
- 3a. Section full: that request is skipped with "reject or raise the cap". The rest of a bulk run continues.
- 3b. Prerequisite not passed / overlap: skipped with the reason.
- 3c. Two approvals race for the last seat: only one wins.
- 3d. Section becomes full: staff are alerted to open another section or raise the cap.
**Postconditions:** Requests *Approved* or *Rejected*. Audited. Live counts updated.

### REG-07 — Override a section's capacity
**Actors:** Registrar, Academic Head, School Admin · **Related:** FR-ENL-10
**Main flow:** 1. On a full section, staff raise the seat cap and give a reason. 2. System saves the new cap and the pending request can be approved.
**Alternative / exception flows:** 1a. New cap below the approved seats already taken: refused. 1b. New cap above the institutional seat cap (`SectionCapacityCap`, default 40): refused, and staff are told to open another section. 1c. New cap above the smallest room the section meets in: refused, naming the room.
**Postconditions:** `SectionCapacityOverridden` audited. Affected staff notified.

### REG-08 — Release a student's seat (staff drop)
**Actor:** Registrar · **Related:** FR-ENL-04
**Main flow:** 1. Registrar drops an approved seat, at any stage, with a reason. 2. System returns the seat and notifies the student.
**Postconditions:** Request *Dropped*. Audited.

### REG-09 — Check and correct seat counts
**Actor:** Registrar · **Related:** FR-ENL-19
**Main flow:** 1. On the approvals page, system lists every section whose stored count disagrees with its approved seats. 2. Registrar reviews and applies the correction.
**Postconditions:** Count corrected under concurrency control. Audited.

### REG-10 — Assisted enlistment for walk-ins and late adds (Proposed)
**Actor:** Registrar
**Description:** Registrar files a seat request on behalf of a student with no device, or after the window closes. Same checks as STU-08, audited as assisted.

### REG-11 — Monitor enrollment status and print a student's COR
**Actor:** Registrar · **Related:** FR-ENL-18, FR-RPT-06
**Main flow:** 1. Registrar sees each student's enrolled / not-enrolled state. 2. Downloads any student's COR for the term.

### REG-12 — Produce reports
**Actors:** Registrar, Academic Head · **Related:** FR-RPT-01–05
**Main flow:** 1. Registrar opens `/reports` and picks the term. 2. Views validated registrations, enlistment results, document completion, room utilization, and faculty load (live-refreshing). 3. Exports any of them to .xlsx, or the whole semester bundle.

### REG-13 — Record academic verdicts
**Actors:** Registrar (Academic Head can view) · **Related:** FR-ENL-15, FR-ENL-17
**Description:** After grades are final, the Registrar records each subject's outcome. This drives the next term's plan, prerequisites, and year level.
**Main flow:**
1. Registrar opens `/academic-records` and picks a student.
2. System shows the student's curriculum as a sheet.
3. Registrar marks each subject taken this term **Passed / Failed / Dropped** and saves.
4. Or: Registrar imports verdicts for many students from .xlsx.
**Alternative / exception flows:** 3a. A retake keeps both attempts. A subject counts as passed if any attempt passed.
**Postconditions:** Failed or dropped subjects come back in a later plan. Passed ones never do. The page reports how many students still have no history on file.

### REG-14 — Open or close the term-activation window
**Actors:** Registrar, Academic Head (also School Admin, Super Admin) · **Related:** FR-SIS-12
**Main flow:** 1. Staff open `/term-activation-control` and see pending requests and when the switch was last changed. 2. Staff open or close the window.
**Postconditions:** GST-02 is accepted or refused accordingly. Audited. Pending requests stay workable.

---

## 8. Academic Head

### AH-01 — Maintain curricula and subjects
**Actors:** Academic Head (also School Admin) · **Related:** FR-SCHED-04, FR-SCHED-09/10
**Main flow:**
1. Academic Head opens `/subjects` and picks or creates a curriculum: program code and name, the school years it is effective for, and whether it's active.
2. Adds or edits subjects: code, title, units, year level (1–3), term (1st/2nd sem), delivery (lecture / laboratory / lecture + laboratory), lecture and lab hours, the lab room kind, and prerequisites.
**Alternative / exception flows:**
- 1a. Two curricula of one program covering the same school year: refused.
- 2a. Duplicate code within the curriculum: refused.
- 2b. Deleting a subject used by a section, or a curriculum that still has subjects: refused (409).
**Postconditions:** Curriculum feeds the scheduler, the enlistment plans, and the prospectus. Audited.

### AH-02 — Maintain class sections (blocks)
**Actor:** Academic Head · **Related:** FR-FAC-01
**Main flow:** Create the term's student blocks: course, year level and section letter (e.g. BSCS 1-A).

### AH-03 — Allocate faculty load
**Actors:** Academic Head (also School Admin) · **Related:** FR-FAC-01, FR-FAC-03, FR-FAC-04
**Main flow:**
1. Academic Head opens `/faculty-load` and picks the term.
2. Opens **Assign load** for a faculty member, filters subjects by course, year and type, and ticks subject × block pairs.
3. The footer shows the running units against the faculty member's ceiling.
4. Saves.
**Alternative / exception flows:** 3a. Over the ceiling: Save is disabled and the server refuses. 2a. A pair already held by another faculty member is shown muted with its holder.
**Postconditions:** Allocation saved and audited. The faculty member is notified of the change.

### AH-04 — Record faculty time preferences
**Actor:** Academic Head · **Related:** FR-SCHED-03
**Main flow:** Set each faculty member's preferred teaching windows. The scheduler tries to honor them as a soft constraint.

### AH-05 — Generate the class schedule (CSP engine)
**Actors:** Academic Head (also School Admin) · **Related:** FR-SCHED-01–17
**Preconditions:** Rooms, subjects, sections or faculty load, and time slots exist for the term.
**Main flow:**
1. Academic Head opens `/scheduling/generate`.
2. Optionally tunes the soft weights (preference, idle gap, room fit), the search budget, and the teaching-day start time.
3. Optionally enters an **arrangement number** to reproduce a past result.
4. Runs generation.
5. System checks the prerequisite and co-requisite structure, then places every meeting with no room, faculty or block overlap, the right room kind, room capacity and load ceilings. It shows the result, its score and the arrangement number.
**Alternative / exception flows:**
- 5a. Structure invalid (e.g. a co-requisite not offered to a block): refused (422) with the reason.
- 5b. No feasible schedule within the budget: reports what couldn't be placed.
- 4a. A published schedule already exists: system asks for confirmation and states what will be discarded.
**Postconditions:** Draft timetable replaces the term's previous draft. `ScheduleGenerated` audited.

### AH-06 — Review and adjust the schedule manually
**Actor:** Academic Head · **Related:** FR-FAC-02, FR-SCHED-14
**Main flow:**
1. Academic Head opens `/scheduling/review` or the **Schedule board**.
2. Drags allocated subjects from the pool onto the weekly calendar, moves or resizes blocks, and picks a room (in the All-rooms view, a prompt shows which rooms are free).
3. The hours tracker shows plotted vs required hours for each meeting.
**Alternative / exception flows:** 2a. Room, faculty or block conflict, or wrong room kind: refused with the reason.
**Postconditions:** Placements saved as manual overrides (draft). Audited.

### AH-07 — Finalize or reopen the schedule
**Actors:** Academic Head (also School Admin) · **Related:** FR-SCHED-11
**Main flow:** 1. Academic Head finalizes a reviewed draft, which locks it against regeneration and board edits. 2. Reopens it if changes are needed before publishing.
**Postconditions:** A finalized draft can be published by the Registrar (REG-05).

### AH-08 — Control the enrollment stage
**Actors:** Academic Head (also School Admin) · **Related:** FR-CYC-01–05
**Main flow:** 1. Academic Head opens the stage control from the ticker. 2. Advances the term one stage, or sets an earlier stage to correct a mistake.
**Postconditions:** Every user sees the new stage. The SIS and enlistment accept or refuse according to the stage. `EnrollmentStageChanged` audited.

### AH-09 — Amend a published class
**Actor:** Academic Head · **Related:** FR-PUB-04
**Main flow:** 1. On the board, Academic Head moves or changes a published class and confirms the warning that people will be notified. 2. System flags the class as amended and notifies the faculty member and every student with an approved seat ("moved from … to …").
**Postconditions:** Class shown with an amended mark, on the COR too. `ScheduleAmended` audited.

### AH-10 — Monitor faculty load and scheduling transparency
**Actor:** Academic Head · **Related:** FR-FAC-04, FR-DASH-03
**Description:** Dashboard load bars flag each member as Balanced / Above average / Below average / Overloaded / Unassigned. The scheduling-transparency panel shows which constraints and preferences shaped each placement.

### AH-11 — Open or close the term-activation window
Same as **REG-14**.

### AH-12 — Produce faculty loading and room reports
**Actors:** Academic Head, Registrar · **Related:** FR-RPT-03, FR-RPT-04
**Main flow:** 1. Academic Head opens `/reports/faculty-load`, searches by name or employee ID. 2. Downloads the per-faculty Confirmation of Faculty Loading, the consolidated loading, grid schedules per block, room utilization, or the bulk zip.

### AH-13 — Manage user accounts
**Actors:** Academic Head, School Admin, Super Admin · **Related:** FR-AUTH-07
**Main flow:** 1. Opens `/users` and filters by role, status or name. 2. Creates an account (name, email, role, initial password), edits it, resets its password, or deactivates / reactivates it.
**Alternative / exception flows:**
- 2a. Academic Head can't create or assign the School Admin role.
- 2b. Nobody can deactivate themselves or the last active School Admin.
- 2c. Duplicate email: refused.
**Postconditions:** Audited.

### AH-14 — Evaluate transferees and view academic records
Shared use cases. See **REG-04** and **REG-13**.

### Academic Head — proposed
- **AH-P1 Dissolve an under-enrolled section (Proposed):** move or release its students in one action, with notices.
- **AH-P2 Forecast section demand (Proposed):** count irregular, repeating and back-subject students before generating.

---

## 9. Faculty Member

### FAC-01 — View my teaching schedule
**Actor:** Faculty Member · **Related:** FR-FAC-05
**Main flow:** 1. Faculty opens `/schedule`. 2. Sees their weekly calendar and day list: subject, block, room, time, seats taken / capacity, total hours, and a "Preview — not yet published" chip while still a draft.

### FAC-02 — Receive schedule and load notices
**Actors:** Faculty Member, Email service · **Related:** FR-NOTIF-01, FR-PUB-03/04
**Description:** Notified when their load allocation changes, when the schedule is published, and when one of their classes is amended.

### FAC-03 — View the dashboard week
**Actor:** Faculty Member · **Description:** The dashboard shows this week's classes and live seat counts.

---

## 10. School Admin

The School Admin can reach **every** function of the roles above. The use cases below are the ones only admins hold.

### SA-01 — Manage school years
**Related:** Academic setup · **Main flow:** Create, edit or delete a school year (e.g. AY 2026-2027), with its key dates, and set the single active year.
**Exception:** Deleting a year that has semesters or is referenced by a curriculum: refused.

### SA-02 — Manage semesters
**Related:** FR-TERM-01 · **Main flow:** Create a school year's First or Second Semester (at most one of each). Its name is generated.
**Exception:** Deleting a semester that sections, registrations or activations use: refused.

### SA-03 — Manage buildings and rooms
**Related:** FR-SCHED-05, FR-SCHED-09 · **Main flow:** Add buildings, then rooms with capacity and kind (lecture room, computer lab, kitchen lab).
**Exception:** Deleting a building with rooms, or a room used in the schedule: refused.

### SA-04 — Maintain system parameters
**Related:** FR-PARAM-01–03, FR-ENL-08/09
**Main flow:** 1. Admin opens `/parameters`. 2. Sets the section seat cap (default 40), the enlistment switch, the per-student unit ceiling, the minimum section enrollment, engine time and step budgets, soft weights, teaching-day start, lab teaching-credit factor, and institution name. 3. Saves.
**Alternative / exception flows:** 2a. Lowering a value below existing records: conflicts are **reported**, never silently rewritten.
**Postconditions:** Audited. The values don't reset at term rollover.

### SA-05 — Manage user accounts
Same as **AH-13**. The School Admin can assign every role except Super Admin.

### SA-06 — Review the audit trail
**Related:** FR-AUD-01 · **Main flow:** Open `/audit` and filter the log by action, user or date. It covers registrations, checklist changes, slot actions, schedule generation, overrides, publication, user management, and notifications.

### SA-07 — Monitor the email outbox
**Related:** FR-NOTIF-02 · **Main flow:** 1. Open `/outbox` and see every queued, delivered and failed message. 2. After a mail outage, retry the failed ones.

### SA-08 — Roll over to the next term
**Related:** FR-TERM-01–06
**Main flow:**
1. Admin creates the next semester (SA-02).
2. Activates it. The previous term is deactivated in the same step.
3. Every work queue now follows the new term. Returning students file term activations (GST-02), and the Academic Head prepares the new schedule (AH-03, AH-05).
**Postconditions:** Last term's data stays searchable. Institutional parameters are unchanged.

### SA-09 — Archive a finished term and export data
**Related:** FR-TERM-07, FR-RPT-04
**Main flow:** 1. Archive a non-active term, which freezes its schedule read-only. 2. Export the semester data bundle and the system parameters / master-data bundle.
**Exception:** Archiving the active term: refused. Activate the next term first.

---

## 11. Super Admin

The Super Admin holds every School Admin use case, plus the following.

### SUP-01 — Choose survey recipients and send the evaluation survey
**Related:** FR-EVAL-01, FR-EVAL-04
**Main flow:** 1. Opens `/survey-recipients` and selects users by role (e.g. 30 students, 10 faculty, 5 staff). 2. Sends the survey. 3. System emails each person a unique single-use link.
**Postconditions:** Invited users see the survey in their sidebar (ALL-07).

### SUP-02 — View the ISO 25010 survey results
**Related:** FR-EVAL-03
**Main flow:** Opens `/survey-admin` to see the response rate, the weighted mean per ISO 25010 characteristic and overall (with the Excellent / Very Good / Good / Fair / Poor interpretation), and respondents' comments.

### SUP-03 — Manage School Admin accounts
**Related:** FR-AUTH-07 · **Description:** Only the Super Admin can create or assign the School Admin role.

---

## 12. Use case index

| ID | Use case | Primary actor |
|---|---|---|
| ALL-01 | Sign in | All |
| ALL-02 | Recover a forgotten password | All |
| ALL-03 | Manage own profile | All |
| ALL-04 | Read notifications | All |
| ALL-05 | View the dashboard and enrollment stage | All |
| ALL-06 | Set preferences and read help | All |
| ALL-07 | Answer the evaluation survey | Invited users |
| ALL-08 | Sign out | All |
| GST-01 | Submit the digital SIS | Guest |
| GST-02 | Request term activation | Guest (returning) |
| STU-01 | Change the temporary password | Student |
| STU-02 | Link the account to a student record | Student |
| STU-03 | View and correct own SIS | Student |
| STU-04 | Track document requirements | Student |
| STU-05 | Follow the enrollment journey | Student |
| STU-06 | View my subjects for the term | Student |
| STU-07 | Browse published sections | Student |
| STU-08 | Request a seat in a section | Student |
| STU-09 | Cancel a pending request | Student |
| STU-10 | Drop an approved seat | Student |
| STU-11 | Confirm enrollment and download the COR | Student |
| STU-12 | View my class schedule | Student |
| STU-13 | Receive enrollment notices | Student |
| STU-P1–P4 | Swap section / waitlist / petition / withdraw *(Proposed)* | Student |
| ADM-01 | Maintain the requirements catalog | Admission Officer |
| ADM-02 | Record document submissions | Admission Officer |
| ADM-03 | Send document reminders | Admission Officer |
| ADM-04 | Assign the official student number | Admission Officer |
| ADM-05 | Validate a term activation | Admission Officer |
| ADM-06 | Pre-authorize a student | Admission Officer |
| ADM-07 | Evaluate transferee credits | Admission Officer (shared) |
| ADM-08 | Download the curriculum prospectus | Admission Officer |
| REG-01 | Review, correct and confirm SIS | Registrar |
| REG-02 | Import a pre-enrollment list | Registrar |
| REG-03 | Pre-authorize a student | Registrar (shared) |
| REG-04 | Evaluate a transferee's credits | Registrar |
| REG-05 | Publish the schedule | Registrar |
| REG-06 | Review and decide slot requests | Registrar |
| REG-07 | Override a section's capacity | Registrar |
| REG-08 | Release a student's seat | Registrar |
| REG-09 | Check and correct seat counts | Registrar |
| REG-10 | Assisted enlistment / late adds *(Proposed)* | Registrar |
| REG-11 | Monitor enrollment status and print a COR | Registrar |
| REG-12 | Produce reports | Registrar |
| REG-13 | Record academic verdicts | Registrar |
| REG-14 | Open or close term activation | Registrar |
| AH-01 | Maintain curricula and subjects | Academic Head |
| AH-02 | Maintain class sections | Academic Head |
| AH-03 | Allocate faculty load | Academic Head |
| AH-04 | Record faculty time preferences | Academic Head |
| AH-05 | Generate the class schedule | Academic Head |
| AH-06 | Review and adjust the schedule manually | Academic Head |
| AH-07 | Finalize or reopen the schedule | Academic Head |
| AH-08 | Control the enrollment stage | Academic Head |
| AH-09 | Amend a published class | Academic Head |
| AH-10 | Monitor faculty load and transparency | Academic Head |
| AH-11 | Open or close term activation | Academic Head (shared) |
| AH-12 | Produce faculty loading and room reports | Academic Head |
| AH-13 | Manage user accounts | Academic Head |
| AH-14 | Evaluate transferees / view academic records | Academic Head (shared) |
| AH-P1–P2 | Dissolve section / forecast demand *(Proposed)* | Academic Head |
| FAC-01 | View my teaching schedule | Faculty Member |
| FAC-02 | Receive schedule and load notices | Faculty Member |
| FAC-03 | View the dashboard week | Faculty Member |
| SA-01 | Manage school years | School Admin |
| SA-02 | Manage semesters | School Admin |
| SA-03 | Manage buildings and rooms | School Admin |
| SA-04 | Maintain system parameters | School Admin |
| SA-05 | Manage user accounts | School Admin |
| SA-06 | Review the audit trail | School Admin |
| SA-07 | Monitor the email outbox | School Admin |
| SA-08 | Roll over to the next term | School Admin |
| SA-09 | Archive a term and export data | School Admin |
| SUP-01 | Send the evaluation survey | Super Admin |
| SUP-02 | View the survey results | Super Admin |
| SUP-03 | Manage School Admin accounts | Super Admin |
