# SEN-GEN — To Fix & To Improve

A running backlog of known limitations, assumptions, and follow-ups, re-derived from an end-to-end
walk of the system's actual workflow (slices, endpoints, domain entities, and client pages present
in the repository) rather than from the requirements document alone.

**How to read this file**

- **Part 1** traces the enrollment workflow stage by stage and states where it breaks or stops short.
  Every finding there is anchored to a file and, where useful, a line.
- **Part 2** is the prioritized checklist — P0 first.
- **Part 3** is the per-area detail, including the cross-cutting engineering items.

Items marked **[decision]** need a product call, not just code. Items marked **[verified]** were
confirmed by reading the code path, not inferred. Items marked **[FIXED]** have been closed — the
finding is kept rather than deleted, because the reasoning is why the code now looks the way it does.

*Last analysed: 29 July 2026. Fix passes: (1) 29 July 2026 — F-01, F-02, F-08, F-09, F-13;
(2) 29 July 2026 — F-20; (3) 5 August 2026 — F-10, F-11; (4) 5 August 2026 — F-06, F-16, the
security batch, and the first automated tests; (5) 5 August 2026 — F-07, F-12, the email outbox;
(6) 6 August 2026 — the shared client `apiFetch`; (7) 6 August 2026 — accessibility and ops;
(8) 6 August 2026 — the CSP engine test suite, CI, and the pigeonhole tightening; (9) 6 August 2026
— the outbox admin view; (10) 6 August 2026 — F-17/18/19; (11) 8 October 2026 — the decisions pass
(F-05, F-14, F-15, institution name, 500 detail), F-03, F-04, the seat reconcile, and every
remaining engineering-time P2/P3 item.*

> **A note on stale findings.** Pass (8) tested the P3 "a 2-hour subject rounds up to a 3h block"
> claim before budgeting to fix it, and found the engine had already been corrected — the finding had
> simply outlived the code. The neighbouring pigeonhole finding, tested the same way, turned out to be
> real and worse than described. Both are now pinned by tests. Re-verify an old finding before
> planning work against it; this file is a record of what *was* true.

> **Note on coverage.** This analysis predates the ISO 25010 survey module, the Super Admin role,
> and the appearance/behaviour settings. Those three slices have not been walked, so they have no
> findings here — `SurveyAdminEndpoints` is mentioned only to explain why its cap was left alone.
> Worth a pass of their own, particularly Super Admin against the note on elevation breadth below.

---

# Part 1 — Workflow analysis

## The intended flow

```
Preparation ─→ Registration ─→ Document submission ─→ Subject enlistment ─→ Closed
                   │                  │                       │
   Academic Head:  │                  │                       │
   curriculum ─→ faculty load ─→ generate (CSP) ─→ finalize ─→ publish (Registrar)
                                                                  │
   Student: SIS submit ─→ account provisioned ─→ Registrar confirms ─→ Admission
            pre-authorizes ─→ (transferee: credit evaluation) ─→ browse published
            ─→ request seat ─→ Registrar approves ─→ seat consumed ─→ My schedule / COR
```

## Stage-by-stage findings

### Stage 0 — Term setup and the enrollment stage itself

**F-01 · The enrollment stage is decorative — nothing enforces it. [verified] [P0] [FIXED]**
`Semester.EnrollmentStage` is written by `Features/EnrollmentCycle/EnrollmentStageEndpoints.cs` and
read by the client ticker, but **no slice anywhere gates on it**. Grep confirms the only consumers
are the endpoint that sets it, the DTO that reports it, and the model configuration. Consequences:

- Advancing the term to *Enlistment* changes nothing — enlistment is actually gated by the unrelated
  `SystemSettings.EnlistmentOpen` switch (`RequestSlotEndpoint.cs:47-54`).
- Setting the stage to *Closed* while `EnlistmentOpen` is still true leaves enlistment wide open. The
  banner tells every student enrollment is closed while the API happily accepts seat requests.
- Conversely, *Preparation* does not stop SIS submissions (`POST /api/registration` is
  `AllowAnonymous`, `RegisterStudentEndpoint.cs:55`).

**Fixed** — `Features/EnrollmentCycle/EnrollmentCyclePolicy.cs` is the gate the mutating slices now
ask. `RequestSlot` refuses unless the term is in the Enlistment stage *and* `EnlistmentOpen` is on;
`RegisterStudent` refuses in `Preparation` and `Closed`. Each refusal names the stage, so the message
and the ticker agree.

Two things worth knowing about the fix:

- Registration's gate is deliberately **wider** than enlistment's — it accepts submissions from the
  Registration stage onward, because late enrollees are ordinary and their papers and enlistment
  follow behind them. Only the two ends are shut.
- It falls **open** when no semester is active, because the SIS is an anonymous public form and a
  setup gap must not show a prospective student a locked door.

While wiring this up: **the enum's numeric order contradicts the workflow order.** `EnrollmentStage`
declares `DocumentSubmission = 2` before `Registration = 3`, but the FR-CYC decision put Registration
first — so the canonical `Order` array disagrees with the enum, and comparing two stages with `<`/`>`
silently gives the wrong answer. The values are a persistence contract and must not be renumbered, so
the order now lives in one place (`EnrollmentCyclePolicy.Order`) with `IndexOf` as the only correct
way to compare. Previously `Order` and `Label` were private to the endpoints file and would have been
copy-pasted into each new caller.

**F-02 · Two competing switches for the same thing. [decision] [FIXED]**
`EnlistmentOpen`, `TermActivationOpen`, and `EnrollmentStage` overlapped without a documented
precedence — a staff member had three places to look when a student said "it won't let me enlist."

**Fixed** — precedence is now decided and documented on `EnrollmentCyclePolicy`: **the stage defines
the period; the parameter switches are a manual pause within it.** Closing either closes enlistment,
and the refusal names which one did it. ~~Still to do: surface that sentence on the Parameters
screen~~ — **done** (pass 11): the Enrollment rules card states the rule beside the switch, including
that opening it outside the Enlistment stage does nothing.

### Stage 1 — Registration (SIS)

**F-03 · No duplicate-person detection, only duplicate email. [P2] [FIXED]**
`RegisterStudentEndpoint.cs:123` rejects a second registration with the same email. Nothing catches
the same *person* registering twice with two mailboxes (same name + birth date + program), which is
the realistic paper-world duplicate. Add a soft duplicate check that flags — not blocks — a likely
match for the Registrar's queue.

**Fixed** — `Features/Registration/LikelyDuplicates.cs`. Same last name + first name + date of birth
under a different record is flagged: a "Possible duplicate" chip in the Registrar's queue and, in
the drawer, the matching records with a jump to each. **Computed on read, never stored**, and never
a block — two people can share a name and a birthday, and refusing a real student on an anonymous
public form is worse than showing the Registrar a pair to compare. Middle name and program are left
out of the key on purpose: a typo'd middle name or a changed program is exactly how the second
attempt differs from the first.

The match relies on names being stored in capitals, which surfaced a real bug in the staff
correction path — see *Found while fixing* in pass 11.

**F-04 · A student cannot correct their own submitted SIS. [P2] [FIXED]**
Once submitted, only staff can edit the registration (`Features/Registration/Manage`). A typo in a
name or address requires a staff visit — the exact friction the system exists to remove. Consider
allowing student-side edits while status is `Pending`, locked on `Confirmed`.

**Fixed** — `Features/Registration/SelfService/MyRegistrationEndpoints.cs` (`GET`/`PUT
/api/registration/mine`) and the student's **My SIS** page. Editable only while `Submitted`; once the
Registrar confirms, it is what they vouched for and corrections go back through staff. The editable
set is the typo-prone personal and contact fields — names, birth details, mobile, address (through
the same PSGC picker as the public form), guardian. **Not** the email (the account's sign-in, with
its own verified change flow) and **not** program or student type (they drive the checklist, the
curriculum, and a transferee's evaluation — an admission decision, not a correction). A name fix is
carried onto the provisioned account, and the change is audited as `RegistrationSelfCorrected`
(78) so the trail shows whose hand changed the record.

### Stage 2 — Documents

**F-05 · There is no document *submission* — only a status flag. [verified] [decision] [P1] [DECIDED]**
`Domain/RegistrationDocument.cs` carries `RequirementCode` + `Status` and nothing else. `IFormFile`
appears exactly once in the entire server (the pre-enrollment .xlsx import,
`Features/PreEnrollment/Import/ImportStudentsEndpoint.cs:30`) — so **no file is ever uploaded for a
requirement**. The student's role is described as "submit enrollment requirements," and the checklist
is described as *digital*, but in practice the student hands paper over a counter and staff tick a
box. Either:
- (a) accept this and rename the student-facing wording from "submit" to "track" (a documentation
  fix, cheap), or
- (b) build actual upload — file store, size/type validation, virus scan or at minimum content-type
  allowlist, retention policy, and a per-document `SubmittedAtUtc`/`VerifiedByUserId`.

This is the single biggest gap between what the requirements imply and what the code does.

**Decided (pass 11): (a), track rather than submit.** Paper is handed over at the counter; the
system records its arrival. Student-facing wording now says so (Help, the nav description), and the
requirements spec carries a scope note saying nothing is uploaded, rather than implying it. Upload
remains a possible future slice — the per-row `UpdatedAtUtc` / `VerifiedByUserId` from F-06 are
already the provenance it would need.

**F-06 · No per-row provenance on a checklist decision. [P1]**
`RegistrationDocument` has no `UpdatedAtUtc` and no `VerifiedByUserId`. FR-DOC-03 calls the checklist
"an auditable record"; today the only trace is the free-text audit entry, which cannot be joined back
to the row. Add both fields — they are two columns and one migration.

**F-07 · Reminder blasts are synchronous, unbounded, and un-deduplicated. [verified] [P1]**
`Features/Documents/Reminders/SendRemindersEndpoint.cs` loads every matching registration and sends
each email **inside the HTTP request**. There is no batch cap, no `LastRemindedAtUtc`, and no
throttle, so: a large term blocks (and can time out) the Registrar's button with no idea how far it
got, and pressing it twice emails the same students twice. Add a per-registration last-reminded
timestamp, a minimum interval, a batch cap, and move the sweep to a background job with a progress
report.

### Stage 3 — Enlistment

**F-08 · A seat can never be released. `EnrolledCount` is only ever incremented. [verified] [P0] [FIXED]**
`ApprovalsEndpoints.cs:370` (`section.EnrolledCount++`) is the **only** mutation of that field in the
whole server. There is no decrement anywhere. Combined with `MyEnlistmentEndpoint.cs:111` — a student
may cancel only a `Requested` row, never an `Approved` one — this means:

- A student who is approved into the wrong section is stuck in it. No drop, no swap.
- A Registrar who mis-approves cannot undo it; the seat is burned for the term.
- A student who withdraws from the school keeps their seats, and the section reads full.
- `Section.EnrolledCount` therefore drifts permanently away from the true count of live approved
  requests, and every figure derived from it (dashboard fill %, "No. of students" on the faculty
  loading report, section-full alerts, capacity-override decisions) inherits the drift.

**Fixed** (migration `AddSeatDrop`) — `SlotRequestStatus.Dropped` plus
`Features/Enlistment/SeatRelease.cs`, which is deliberately the **only** place in the system that
decrements the counter, so a staff release and a student's own drop cannot diverge:

- **Staff**: `POST /api/enlistment/approvals/{id}/drop` (Registrar/School Admin), with an optional
  reason, a bell notice to the student, and its own audit action. This is the undo a rejection could
  never be — rejection only applies to a request still pending.
- **Student**: `DELETE /api/enlistment/requests/{id}` now covers both cases, because they are one
  thing to the student ("I don't want this class"). A pending request is cancelled as before; an
  approved one is dropped and the seat returns. Self-drop is bounded by the enlistment window — once
  the term leaves the enlistment stage the roster is the Registrar's to change. The UI confirms
  first (via the house `confirmAction`) and says plainly that the seat may be taken by someone else.
- **Concurrency** mirrors the approval exactly: the decrement rides `Section.RowVersion` and retries
  on a lost race, with the `CK_Sections_EnrolledCount` CHECK as the backstop, so a drop racing an
  approval cannot corrupt the count in either direction.
- **Existing drift** is surfaced, not hidden: releasing a seat from a section that already reads 0
  enrolled refuses and says the two figures disagree, rather than pushing the counter negative.

New statuses fall out of every existing filter correctly — every read path tests `== Approved` or
`== Requested`, and the filtered unique index already covered only that live pair, so a dropped
subject can be requested again.

Still open: **[decision]** whether to derive `EnrolledCount` from the count of live approved
requests rather than storing it, keeping the column only as the concurrency/CHECK anchor.
~~A reconcile pass for counts that drifted before this path existed~~ — **done** (pass 11):
`Features/Enlistment/SeatCounts`. The approvals page lists every active-term section whose stored
count disagrees with its live approved requests, and the Registrar corrects one deliberately — it is
**surfaced, never silently rewritten**, because a mismatch means something happened that the seat
lifecycle did not record. The correction rides the same `RowVersion` retry (recounting inside the
loop) and refuses when there are more live approvals than seats, since that needs a person to raise
the cap or drop a student. Audited as `SeatCountReconciled` (79) with before/after figures. The live
dev database currently has **no** mismatches.

**F-09 · The approval-time overlap re-check is not scoped to the active term. [verified] [P0] [FIXED]**
`ApprovalsEndpoints.cs:322-326` gathers the student's approved sections with
`r.Status == SlotRequestStatus.Approved` and **no semester filter** — then compares published time
slots. `RequestSlot` was fixed for exactly this (see its comment at lines 95-104), but the approval
leg was missed. A returning student whose previous term's approvals still sit in the table gets a
false *"Approving this would give the student overlapping classes"* against a class that ended last
semester, and the Registrar's only listed remedy is to reject a perfectly valid request.

**Fixed** — the query now carries `&& r.Section!.SemesterId == section.SemesterId`, matching the
scoping `RequestSlot` already had.

**F-10 · Prerequisites are modeled, editable, printed — and never enforced. [verified] [P1] [FIXED]**
`SubjectPrerequisite` is seeded, maintained through `Features/Curriculum/Subjects`, exposed in DTOs,
and printed on the prospectus. It was checked **nowhere** in `Features/Enlistment` —
`EnlistmentPlan.cs` contained no prerequisite logic at all. A first-year student could hold a seat in
a subject whose prerequisite they had not taken.

The deeper reason was **F-11**, and one could not be fixed without the other. Both are now closed
together; see F-11 for the store and the fall-open rule that makes enforcement safe to ship.

**Fixed** — `AcademicHistory.UnmetPrerequisitesAsync` is asked on both legs:
`RequestSlotEndpoint` (so the student learns at the moment they click, not days later from a
Registrar) and `ApprovalsEndpoints.TryApproveAsync` (because a record can change in between, and a
seat must not be granted on a check that is no longer true). Both refusals name the missing subjects
and are audited as `PrerequisiteBlocked` — the one enlistment refusal a student cannot resolve
themselves, so the trail is how the Registrar finds out the rule fired and whether the history
behind it is wrong.

**F-11 · There is no record of subjects a student has completed. [verified] [decision] [P0-for-design] [FIXED]**
The system stored what a student *is enlisting in*, and — for transferees only — what was *credited
from elsewhere* (`TransfereeEvaluationItem`). There was no grade, no pass/fail, no "subject
completed" record for a continuing student. Everything downstream that logically depends on academic
history was therefore standing on nothing:

- **Prerequisites** could not be checked (F-10).
- **Year level** is derived from credited units for a transferee (`YearLevelPolicy.FromCreditedUnits`)
  but for a continuing student it was whatever was last assigned — it did not advance on its own.
- **The enlistment plan** (`EnlistmentPlanner.ResolveAsync`) offered a student *their year level's*
  subjects, not *the subjects they still owe* — a student who failed a subject last term was offered
  this year's list and never the repeat.

**Decided and fixed** (migration `AddStudentSubjectRecord`) — the minimum honest version, not a
grade book. `Domain/StudentSubjectRecord.cs` holds subject + term + verdict
(`Passed`/`Failed`/`Dropped`) with who recorded it and when. There is no grade value, no scale, and
no computation of standing: grading belongs to the separate student-records system, and a
half-built version of it here would be worse than none. `Features/AcademicRecords` is the Registrar's
slice — a paged queue, a per-student sheet read against their curriculum, and an .xlsx import,
because a school adopting SEN-GEN mid-programme has years of this behind it already and typing four
years of it one sheet at a time is not a plan.

Four things are worth knowing about the fix:

- **The gate falls open for a student with nothing on file.** This is the most consequential line in
  the change. With no history recorded *every* prerequisite reads as unmet, so enforcing against an
  empty record would not be strictness — it would be a school-wide outage dressed as a rule, refusing
  every continuing student every subject that has a prerequisite on the first day it shipped. So
  `AcademicHistory.IsEnforceable` turns on there being something to check against, exactly as
  `EnlistmentPlan.IsResolved` already falls open for a missing curriculum. The page says so where the
  backlog count is, rather than leaving it to be discovered.
- **A transferee's credits count as history.** `AcademicHistory` reads the completed evaluation's
  credited items alongside the records. Leaving that out would have been the cruellest available bug:
  a transferee credited for the prerequisite, refused the subject it unlocks, by the very evaluation
  that was supposed to let them in.
- **Retakes are first-class.** The unique index is (student, subject, **term**), so both attempts at
  a failed subject are kept; a subject counts as passed if any attempt passed, and units are counted
  per distinct subject so failing and then passing a 3-unit subject earns 3 units, not 6.
- **Year level now advances on units, not on the calendar.** `YearLevelPolicy.FromEarnedUnits` is the
  general form of the transferee rule — credited-from-elsewhere and passed-here units are the same
  currency, so both go through one calculation. It is consulted on term activation only when the
  school year turns over (re-deriving mid-year could *demote* a student between semesters), and only
  when there is history; otherwise the calendar rule stands. It remains a recommendation the
  Admission Officer can override, the same standing the transferee derivation always had.

Still open: **[decision]** whether a student who owes subjects from two years back should be blocked
from this year's load rather than merely offered the repeat alongside it — the plan currently offers
both and lets the unit ceiling arbitrate.

**F-12 · Bulk approval: N saves and N synchronous emails in one request. [P1]**
`BulkApproveAsync` takes up to 500 requests and calls `TryApproveAsync` per row, each with its own
`SaveChangesAsync` (`ApprovalsEndpoints.cs:362-386`), then loops sending approval emails inline. A
full sweep is 500+ round trips plus 500 SMTP sends before the response returns. The per-request
integrity is right; the transport is not. Batch the saves and hand the emails to a background queue.

**F-13 · A cancellation is audited as a request. [verified] [P2] [FIXED]**
`MyEnlistmentEndpoint.cs:121` recorded a student's cancellation with `AuditAction.SlotRequested`, so
the audit trail could not tell a seat request from a student taking one back — the two read
identically on the audit page.

**Fixed** — `SlotCancelled = 73` and `SlotDropped = 74`, appended to the enum (never renumbered, per
the contract in its own doc comment) and wired to their call sites.

**F-14 · Enlistment has no terminal state. [decision] [P2] [FIXED]**
A student ends up with a set of approved `SlotRequest` rows and nothing that says *"this student's
enrollment for the term is complete."* The cycle's own last stage is *Closed*, but no per-student
completion exists, so the Registrar cannot answer "who is actually enrolled?" except by counting
approvals. Since tuition (stage 4) is out of scope, an explicit `RegistrationStatus.Enlisted` /
`EnrollmentCompleted` marker — set when the student's plan is fully covered by approved seats — is
the honest stand-in and makes the COR meaningful.

**Decided and fixed (pass 11): derived, not stored.** `Features/Enlistment/EnrollmentCompletion.cs`
— a student is *Enrolled* when every subject their plan says they owe this term holds an approved
seat; otherwise *Partial* (naming what is missing) or *Not started*. A stored flag would need
keeping in step with every approval, rejection, drop, re-evaluation and recorded verdict — exactly
the drift F-08 recorded for `EnrolledCount`. It shows on the Registrar's registration queue, the
student's enlistment page and dashboard journey (where "done" now means fully enlisted, not "one
subject approved"), and the COR prints *Enrollment complete* / *incomplete — still to enlist: …*.

Two limits worth stating: it is **not a filter** (the plan cannot run in SQL, so it is computed per
row of the page shown, at most 200 plan resolutions), and an **empty plan reads "No subjects due"**,
not *Enrolled* — nothing left to take is not the same as having enlisted for the term.

### Stage 4 — Scheduling, finalize, publish

**F-15 · Publish does not require finalize. [decision] [P1] [FIXED]**
Carried forward and still true: the Registrar can publish a draft that was never finalized
(`Features/Publishing/PublishSchedule/PublishScheduleEndpoint.cs`). If the intended flow is strictly
Draft → Finalized → Published, add the guard.

**Decided and fixed (pass 11): strictly Draft → Finalized → Published, with a dry run.** Publish
refuses with a 409 while any draft row is unfinalized. `GET /api/publishing/{id}/preview` returns
what one press would do — classes, faculty and students to notify, and the refusal reason if any —
computed by the same recipient code the publish uses, so the number the Registrar agrees to is the
number that happens. The Publish button now loads it and asks "Publish 19 classes and notify 7
faculty members and 15 students? This cannot be undone." This also closes the P2 bulk-confirmation
item for publish, the irreversible one.

**F-16 · No optimistic concurrency on schedule writes. [verified] [P1]**
`RowVersion` exists on exactly one entity — `Domain/Section.cs:45`. `ScheduleAssignment` has none, so
generate / board-edit / finalize / publish can race: regenerate deletes drafts and re-inserts while
another request reads or writes the same rows. Add a concurrency token, or serialize these operations
per semester with an application lock.

**F-17 · Finalized lock has no proactive banner. [FIXED]** Board edits on a finalized schedule were
blocked server-side (clear 409), but the board showed nothing up front — and a 409 arrives *after*
someone has dragged a class across the screen, which is the wrong moment to learn the board was
locked before they started.

**Fixed** — the board GET now returns `isFinalized`, `isArchived`, and a single `lockReason` carrying
the same sentence the server would refuse with, so the banner and the 409 cannot disagree. The
calendar's `editable`/`droppable`/`eventDurationEditable` are switched off when it is set, so the
interaction agrees with the banner rather than contradicting it. Styled calmer than `.alert`: a
finalized schedule is a normal state the Academic Head deliberately put it in, not an error, and
flagging it as one would make a correctly signed-off board look broken.

**F-18 · Fullscreen height doesn't re-fit on resize. [FIXED]** The height read `window.innerHeight`
during render, and a resize does not re-render — so rotating a tablet or moving the window to another
display left the calendar clipped or floating until fullscreen was toggled off and on.

**Fixed** — viewport height is state, updated by a resize listener that is attached *only while
fullscreen*. Outside it the height is a constant, so there is nothing to recompute and no reason to
run a handler on every resize of an ordinary window.

**F-19 · Tooltip parity. [FIXED]** The hover card existed only on the board, leaving the view most
people actually use — their own week — with no way to see a class's room, section, or seat count
without cross-referencing the day list below it.

**Fixed by extraction, not by copying.** The card was ~70 lines inline in `ScheduleBoardPage`;
duplicating it into `SchedulePage` would have created exactly the problem this file complains about
two sections down. `ScheduleTooltip.jsx` now serves both. The two callers carry *different* entry
shapes — the board knows components, delivery, overrides and amendments; My schedule knows seat
counts and nothing about drafts — so every row is conditional on its field being present, and the
card shows what the caller happens to know rather than forcing one merged DTO on both endpoints.

While extracting it, `DAY_NAMES` turned out to be declared in three files. It now lives once in
`calendarUtils.js` alongside the other day/time helpers (with `DAY_ABBR` beside it), which closes
half of the "day/time formatting duplicated" P2 item — the client half. The server still formats
days and 12-hour times independently in the report builders.

### Cross-stage — the queues

**F-20 · Every work queue is capped, not paged, and the client filters only what it received.
[verified] [P0] [FIXED]**
Eight queue endpoints hard-cap at `.Take(500)` (documents checklist, enlistment approvals ×2,
pre-authorization, assign-student-number, registrations, term activations, transferee evaluations);
`ListUsersEndpoint.cs:56` and `SurveyAdminEndpoints.cs:540` cap at `.Take(1000)`. Meanwhile
`features/shell/useTableControls.js` does **all** filtering, sorting, and pagination in the browser
over whatever arrived — its own header comment says so ("the lists are already fetched in full (the
server caps at 500)").

The result is worse than slow, it is wrong: past row 500 a record is not merely on a later page, it
**does not exist as far as the UI is concerned**, and the search box will confidently return "no
results" for a student who is in the database. Nothing warns the user that the list was truncated.
This directly defeats FR-TERM-03 ("a search shall widen past the active term") — the widened search
still only searches the first 500 rows.

**Fixed** — real server-side paging on every queue, built on `Common/Paging/Paging.cs`
(`PageSpec` / `Paged<T>` / `ToPagedAsync`) and `features/shell/useServerTable.js`. The new hook is a
sibling of `useTableControls` with the same surface, so `Pagination` and `SortHeader` were unchanged;
`useTableControls` stays for the lists that genuinely arrive whole (a term's subjects, a student's own
requests).

Ten endpoints and nine pages moved over: users, registrations, audit trail, enlistment approvals,
document checklist, pre-authorization, assign-student-number, term activations, transferee
evaluations.

Three things had to move server-side along with the paging, each of which would otherwise have become
a quiet lie the moment the list stopped arriving whole:

1. **Sorting.** A client-side sort now orders only the page it was handed, so the header would look
   like it worked while ranking 25 rows out of hundreds. Every sortable column has a SQL ordering,
   including the derived ones — `creditedUnits` and evaluation `status` sort through subqueries;
   `clearance` and the pre-auth eligibility filter resolve the gating requirement codes first (a
   handful of rows) so the test itself still runs in SQL. Every ordering ends in a stable
   `ThenBy(Id)`, without which two rows with the same surname can swap places between pages.
2. **Filters that were applied after the fetch** — the checklist's complete/incomplete, the
   pre-authorization eligibility chip, the transferee evaluation status. Each would have filtered one
   page while the total counted the rest.
3. **Summary counts.** `pendingCount`, `completeCount`, `authorizedCount`, `eligibleCount`,
   `completedCount` were all computed from the fetched rows and would have silently become
   "…on this page" — and these are the numbers staff judge the backlog by, and what the sidebar
   badges are checked against. They are now SQL counts over the whole queue, taken *before* the view
   chip narrows it, so switching from "All" to "Incomplete" no longer changes what the board reports
   the term's outstanding work to be.

The audit trail's action dropdown moved too: it was derived from the fetched rows, so paging would
have reduced it to "the actions visible right now" and made filtering to anything else impossible.
The server returns the distinct set.

`SurveyAdminEndpoints` keeps its `.Take(1000)` deliberately — it is the ISO 25010 research
instrument, sized at 45 respondents (§8), so paging it would be scaffolding for a list that cannot
grow.

---

# Part 2 — Prioritized checklist

## Done — 29 July 2026

Six findings closed, including four of the five P0s. Each is kept in Part 1 with its reasoning and a
**Fixed** note, because the reasoning is why the code now looks the way it does.

| # | What it was | Where the fix lives |
|---|---|---|
| **F-08** | A seat could never be released — `EnrolledCount` only ever incremented, so a mis-approval was permanent | `Features/Enlistment/SeatRelease.cs`, migration `AddSeatDrop`, `SlotRequestStatus.Dropped`, staff `POST /approvals/{id}/drop`, student drop on `DELETE /enlistment/requests/{id}` |
| **F-09** | The approval-leg overlap check ignored the term, so a returning student got a false clash with last semester's class | `ApprovalsEndpoints.TryApproveAsync` — semester filter on the approved-sections query |
| **F-01** | `EnrollmentStage` was decorative; the ticker and the API could disagree outright | `Features/EnrollmentCycle/EnrollmentCyclePolicy.cs`, wired into `RequestSlot` and `RegisterStudent` |
| **F-02** | No documented precedence between the stage and the parameter switches | Decided and documented on `EnrollmentCyclePolicy`: the stage defines the period, the switches pause within it |
| **F-13** | A cancellation was audited as a request, so the trail could not tell them apart | `AuditAction.SlotCancelled = 73`, `SlotDropped = 74` |
| **F-20** | Queues capped at 500/1000 while the browser filtered only what it received — records past the cap were invisible to search | `Common/Paging/Paging.cs`, `features/shell/useServerTable.js`, 10 endpoints + 9 pages |

**Verified against the live database**, not just compiled: paging returns non-overlapping pages under
a descending sort; the audit trail's 989 rows are now searchable to the last one (the oldest entry
was previously unreachable behind a 200-row cap); the summary counts stay whole-queue across every
view chip; and a seat was dropped end-to-end — `EnrolledCount` 1 → 0, audited as `SlotDropped`, a
second drop correctly refused — then restored so the demo data is unchanged.

**Two collateral corrections made while fixing the above**, both recorded here because neither was in
the original analysis:

- **The `EnrollmentStage` enum's numeric order contradicts the workflow order** (`DocumentSubmission = 2`
  precedes `Registration = 3`, but FR-CYC puts Registration first). The values are a persistence
  contract and cannot be renumbered, so comparing stages with `<`/`>` silently gives the wrong answer.
  The canonical order now lives once on `EnrollmentCyclePolicy.Order`, with `IndexOf` as the only
  correct comparison.
- **The seeded demo term contradicted its own stage** — 16 registrations, 17 published schedule rows
  and 19 live slot requests, labelled `Preparation`. Harmless while nothing read the stage; the moment
  F-01 made it real it would have locked the seeded students out of the flow the seed exists to show.
  `DbInitializer` now seeds `EnrollmentStage.Enlistment`, and the existing local database row was
  corrected to match.

## Done — 5 August 2026

The keystone pair. F-11 was the last P0 that needed a scope decision rather than engineering time;
deciding it unblocked F-10 in the same pass, because a prerequisite check with nothing to check
against is not a rule, it is a lockout.

| # | What it was | Where the fix lives |
|---|---|---|
| **F-11** | No record of what a student had completed, so prerequisites, repeats, and year-level advance all rested on nothing | `Domain/StudentSubjectRecord.cs` + `SubjectVerdict`, migration `AddStudentSubjectRecord`, `Features/AcademicRecords/*`, `/academic-records` page |
| **F-10** | Prerequisites modeled, editable, printed — and enforced nowhere | `AcademicHistory.UnmetPrerequisitesAsync`, wired into `RequestSlot` and `ApprovalsEndpoints.TryApproveAsync`, audited as `PrerequisiteBlocked` |

**Scope decision on F-11**: verdicts, not grades. No grade value, no scale, no computed standing —
grading stays with the separate student-records system, and the honest minimum here is the verdict a
Registrar can defend from the paper record in front of them.

**Verified against the live database**, then restored so the demo data is unchanged: the queue pages
and its counts are whole-queue; recording a verdict flips `IsEnforceable` and moves earned units and
the derived year level; a `None` verdict clears one term's row without touching another attempt at
the same subject; and the full retake lifecycle behaves — a subject failed in one term and passed in
the next keeps **both** attempts, counts its units **once**, drops out of "still owed", and
disappears from the student's enlistment plan, while a subject failed and not yet retaken comes back
into that plan flagged as a repeat.

**Not exercised live**: the two prerequisite refusals firing end-to-end. They compile and share the
helper whose passed/owed sets were verified above, but this database has no published section for any
subject carrying a prerequisite, and manufacturing one would have meant creating a faculty load, a
cohort, a board placement, and a publish in a working dev database. Worth covering by a test rather
than by more fixture-building — it is on the list under *Testing*.

## Done — 5 August 2026, second pass

| # | What it was | Where the fix lives |
|---|---|---|
| **F-06** | A checklist decision left no trace on the row — only free-text audit that could not be joined back to it | `RegistrationDocument.UpdatedAtUtc` / `VerifiedByUserId`, migration `AddChecklistProvenanceAndScheduleConcurrency`, surfaced per row on the board |
| **F-16** | `RowVersion` existed on `Section` alone, so generate / board-edit / finalize / publish raced silently | `ScheduleAssignment.RowVersion` + `Features/Scheduling/ScheduleConcurrency.cs`, wired into all six write points |
| — | No login rate-limiting or lockout; failed sign-ins were audited in detail and otherwise unimpeded | `LoginThrottle` (per-account, migration `AddLoginLockout`) + an IP fixed-window limiter on login, 2FA verify/resend, and forgot/reset-password |
| — | No security response headers at all | `Common/Web/SecurityHeaders.cs` — nosniff, CSP incl. `frame-ancestors 'none'`, X-Frame-Options, Referrer-Policy, Permissions-Policy |
| — | No React error boundary; a render exception blanked the whole SPA | `features/shell/ErrorBoundary.jsx`, wrapping the router and auth provider in `main.jsx` |
| — | **No automated tests anywhere** | `SENGENSystem.Server.Tests` — 37 tests, green |

**On the concurrency fix**: it returns a **409 rather than retrying**, which is the opposite of what
`SeatRelease` does, deliberately. Retrying is right for the seat counter because the intent — one
seat — survives the race intact. It is wrong here: the rows the user was looking at are not the rows
now in the database, so re-applying their decision would apply it to something else. Reload, look,
decide again.

**On the two brute-force layers**: they catch different attacks and neither is sufficient alone. The
IP limiter stops one source working through many accounts; the per-account lockout stops many
sources working on one. The lockout is temporary (15 min) because a permanent one hands anyone who
knows a staff email a denial-of-service against that person.

**The test project** closes the gap the F-10/F-11 pass had to leave open. 37 tests cover
`AcademicHistory` (the fall-open rule, a transferee's credit satisfying the prerequisite it unlocks,
failed-then-passed counting units once), the planner's repeat / already-passed handling, the
year-level ladder, and the lockout. **The suite was mutation-checked, not just run**: forcing
`IsEnforceable` to `true` fails 1 test and dropping the already-passed exclusion fails 2, so the
assertions bite rather than merely passing.

It is deliberately **unit** coverage of policy. The things that need a real database — the
`CK_Sections_EnrolledCount` check, the filtered unique index, the rowversion tokens themselves —
are *not* asserted, because the in-memory provider reports success without enforcing any of them,
which is worse than no test. `TestDb` says so in its own doc comment. Integration tests against SQL
Server are the next step, and the F-10 refusal at both call sites still wants one.

## Done — 5 August 2026, third pass

The bulk-email problem, closed at all three sites at once by giving the system somewhere to put mail
that is not "an SMTP call in the middle of a request".

| # | What it was | Where the fix lives |
|---|---|---|
| **F-07** | Reminder sweep: synchronous, unbounded, no memory — pressing twice emailed everyone twice | `ReminderPolicy` (20h quiet period, 200-row cap), `StudentRegistration.LastRemindedAtUtc`, sweep moved to the outbox |
| **F-12** | Bulk approve: N inline SMTP sends for up to 500 requests | Confirmations queued on the outbox; single and bulk legs now share one path |
| — | Email failures were invisible — a success was audited, a failure swallowed by design | `Domain/OutboxEmail` + `OutboxDispatcher`: status, attempt count, last error, exponential backoff, `Failed` as a terminal state rather than a deletion |
| — | Publish announced to the whole institution synchronously (see the note under P1) | Same outbox, keyed per semester + recipient |

**Why an outbox rather than a queue.** A row is written in the same transaction as the change that
justifies it, so an email can never be sent for a write that rolled back, and never lost for one
that committed. A `SendAsync` in the middle of a handler cannot give either guarantee — it has
already sent by the time the handler decides whether to commit.

**`IEmailSender` is not replaced.** A password reset or a 2FA code still goes out inline, because
the user is sitting there waiting for it and a queue would add latency to the one case where latency
*is* the experience. The outbox is for the bulk paths, where nobody is waiting and volume is the
harm.

**What F-12 deliberately did not do.** The finding says "batch the saves *and* hand the emails to a
background queue". Only the second half is done. The per-request `SaveChangesAsync` inside
`TryApproveAsync` is not incidental chattiness — it is the optimistic-concurrency retry F-08
established for the seat counter, and `RevertApproval` depends on each row committing independently
so one full section cannot poison the batch. Batching would make a single lost race fail the whole
run and would break the "approved 47 of 50, here is why three didn't" contract the endpoint
promises. The transport was the problem; the integrity was not. **This half of F-12 is closed as
won't-fix, not as done.**

**The dispatcher is in-process**, which is a real trade worth naming: a restart mid-batch leaves
those rows `Pending` and they go out on the next start — the safe direction — but it will not scale
past one machine. A job runner is the upgrade path if that ever matters.

Tests: 51 now (up from 37), covering the outbox's commit-coupling and dedupe and the reminder quiet
period. Mutation-checked again — removing the unsaved-batch dedupe check fails 1 test.

## Done — 6 August 2026

One extraction closing one P1 and three P2s, because they were all the same finding seen from
different angles: there was no shared client, so there was nowhere for a cross-cutting decision to
live.

| What it was | Where the fix lives |
|---|---|
| **No global 401/expiry handling** — only `scheduling/api.js` recognised a 401; everywhere else a lapsed session produced a generic message on a page that would never work again | `features/shell/apiClient.js` — clears the token and redirects once, preserving `?next=` |
| **`parseError` copy-pasted into 21 api modules**, each with its own fetch and auth wiring | All 22 modules (+ 4 pages and 2 shell helpers) now call `apiFetch` |
| **ProblemDetails read by one module** — the global 500 handler returns `detail` and a `reference` trace id, and 20 modules threw both away | Folded into the shared `parseError`; every page gets the lead now |
| — | `features/shell/token.js` — token storage split out to break the `apiClient ⇄ auth/api` import cycle |

**Two calls deliberately stay off the shared client**, and both would be bugs if migrated:

- `fetchCurrentUser` runs on app start to decide whether a session exists. A 401 there is the
  ordinary "not signed in" answer, not an error — routing it through the global handler would make
  the app redirect to login as a *consequence* of checking whether it needed to.
- `useNavBadges` polls every 60s in the background. Its 401 would yank the user to the login screen
  mid-sentence, caused by a request they never made. The next thing they actually click does the
  redirect, with their own action as the cause.

`/api/auth/*` is also exempt from the redirect: login answers a wrong password with a 401, and
bouncing on that would replace "Invalid email or password" with a page reload.

**Verified**: export surfaces diffed against `HEAD` for all 22 modules — identical except the two
intended additions (`importPreEnrollment`, `fetchPreEnrollmentTemplate`, moved out of
`PreEnrollmentPage` where they had escaped the shared client) and `auth/api`'s token functions,
which are now re-exported from `shell/token.js`. Lint is clean; the 4 remaining errors are the same
pre-existing ones in five files this pass never touched.

**A note on how this was done.** The first attempt used a codemod, which mis-parsed destructured
parameters, deleted shared helpers out from under call sites it had not converted, and broke six
modules at once. It was caught by lint, reverted from git, and the work redone by hand. The lesson
worth keeping: the build does **not** type-check this code, so `npm run build` passing means very
little here — `eslint` is the actual safety net for a refactor of this shape.

## Done — 6 August 2026, second pass

| What it was | Where the fix lives |
|---|---|
| `.alert` banners were plain `<div>`s, so a screen reader never announced an error or a success | 73 banners annotated across 52 files — `role="alert"` for errors, `role="status"` for successes; the dynamic ones branch their role with their class |
| Sortable headers carried their state only in an arrow glyph | `aria-sort` on the `<th>` plus an `.sr-only` description of what the next click does — fixed once in `SortHeader`, so every table in the app has it |
| Paging swapped the table's contents with no announcement | `aria-live="polite"` on the pager's range readout |
| Modals were only *visually* modal — Tab walked out into the page behind the overlay | `features/shell/useModalFocus.js`, wired into all six dialogs; traps Tab and restores focus to whatever opened it |
| No `/health` endpoint | `/health` (readiness, includes a DB probe) and `/health/live` (liveness, no checks) |
| `DateTime.Now` in 8 report sites read the **server's** locale, not the school's | `Common/InstitutionClock.cs` — store UTC, convert once at the display edge |

**On the time-zone fix**, since it is the one with a real bug behind it: on a developer machine
`DateTime.Now` looks correct, so nothing shows. On a UTC-configured host — the default for a
container or cloud VM — every printed report was stamped eight hours behind the office that
generated it, and the room grid highlighted the wrong day as "today". `InstitutionClock` also treats
`DateTimeKind.Unspecified` (what EF hands back) as UTC, because reading those as local would
double-apply the offset — the subtlest form of the same bug.

**Three things turned out not to be broken**, and are recorded as verified rather than fixed:
icon-only buttons all already had accessible names (`aria-label` on every `modal-close`,
`aria-pressed` on fullscreen, and no svg-only buttons anywhere); and three static informational
banners inside modals were deliberately **left** without a live region, because they render with the
dialog rather than in response to an action — a role there would make them interrupt on every open.

Tests: 56 (up from 51), including five for the clock. Mutation-checked — collapsing
`InstitutionClock.Now` back to `DateTime.UtcNow` fails 1.

## Done — 6 August 2026, third pass

**The CSP engine now has tests** — the largest untested surface in the repository, and the one this
file has called the highest-value target since the first analysis. 20 tests across three files
(`SENGENSystem.Server.Tests/Scheduling/`), suite total 76.

| Area | What is covered |
|---|---|
| **Hard constraints (NFR-1)** | H1 room, H2 faculty, and H6 cohort double-booking; H3a capacity; H3b room-kind suitability; a lecture-laboratory subject placed as two meetings that cannot collide; a full grid packed with demand equal to supply |
| **Weekly-hours coverage** | A 3-hour laboratory occupies one contiguous block of *exactly* 180 minutes — and is refused rather than scheduled across a lunch break |
| **Reproducibility (FR-SCHED-08)** | Same seed → identical timetable *and* identical step count, stable across repeated runs; every seed still valid; and — the other half of the contract — different seeds genuinely explore different arrangements |
| **Diagnostics (FR-SCHED-07)** | Empty pools named individually; an overloaded member named with the numbers and the sections; a missing room *kind* distinguished from a room that is merely too small; both pigeonhole cases (faculty and cohort); a section allocated to a deleted member |

**These assert the contract, not a timetable.** The engine breaks ties with a seeded shuffle and
orders candidates by soft cost, so pinning exact placements would turn every heuristic improvement
into a red suite — the fastest way to teach people to ignore their tests.
`ScheduleProblemBuilder.AssertHardConstraints` checks the produced schedule against the guarantee
instead: every meeting placed exactly once, every block exactly its required length, and no
overlapping pair sharing a room, a member, or a cohort.

**Two pre-flight assertions are about cost, not correctness.** The overload and pigeonhole tests
assert `Steps == 0`, because both conditions are decided from the inputs alone — if either ever
slipped into the search, an impossible problem would burn the full 20-second budget before saying so.

**Mutation-checked, three ways**, since a suite that passes first time proves nothing on its own:
deleting the cohort-clash check fails 1; letting a block span a break in the day fails 1; and making
the tie-break ignore the seed fails exactly the reproducibility-variety test written for it. All
three reverted, and the engine verified byte-identical to `HEAD` afterwards.

## Done — 8 October 2026

The decisions pass, then everything left at P2/P3 that was engineering time rather than a call.
Started by re-verifying every open item against the code, per the note on stale findings at the
top — one was partly stale (the per-faculty grid already had colours, just from a third palette).

**Decisions taken**: F-05 → *track*, not upload · F-15 → finalize required, plus a dry-run
confirmation · F-14 → derived marker · institution name → configurable · 500 detail → trace id only.
**Declined for now**: shortening the JWT lifetime, moving secrets out of `appsettings.json`.

| # | What it was | Where the fix lives |
|---|---|---|
| **F-15** | Publish accepted a never-finalized draft and announced it to everyone | `PublishScheduleEndpoint` (409 guard + `GET …/preview`), `PublishingPage` confirmation |
| **F-14** | No per-student "enrolled" state | `Features/Enlistment/EnrollmentCompletion.cs`, `EnrollmentChip.jsx`, COR line |
| **F-05** | "Submit documents" implied an upload that does not exist | Wording in Help/nav; scope note in the spec |
| **F-03** | Same person, second mailbox, undetected | `Features/Registration/LikelyDuplicates.cs` |
| **F-04** | Students could not fix their own SIS | `Features/Registration/SelfService/*`, `/my-registration` |
| **F-08 follow-up** | Drifted seat counts had no reconcile | `Features/Enlistment/SeatCounts/*`, `SeatCountCheck.jsx` |
| **F-02 follow-up** | Precedence documented only in code | Parameters → Enrollment rules card |
| — | Institution printed as a hardcoded "STI" | `SystemSettings.InstitutionName` (migration `AddInstitutionName`), Parameters card, memo + 3 PDFs |
| — | 500 responses carried the exception type and message | `Program.cs` handler + `GenerateScheduleEndpoint`: trace id only; the message stays in the log and audit trail |
| — | No way to reproduce a past arrangement | "Reproduce #" field on Generate |
| — | Subject palette in two places, kept in sync by hand — *and* a third report-only palette | `Common/Reporting/SubjectPalette.cs` mirrors `calendarUtils.subjectHue`; `SubjectPaletteTests` pins it to hues computed by running the client function |
| — | Day/time formatting re-implemented in 12 server files | `Common/Formatting/ClockText.cs` (the server's half; the client's was done in pass 10) |
| — | Per-faculty grid lacked the class-program grid's treatment | Board palette fill + border, bold code, LEC/LAB, 12-hour time, subject legend |
| — | Main bundle > 500 kB | 47 pages lazy-loaded; main chunk 330 kB, FullCalendar only with the board |
| — | Downloads could hang forever | `apiDownload` timeout (120 s) + cancel; every download now goes through it |
| — | Sparse operational logging | `Common/Web/RequestLogging.cs` — one line per `/api` request; 5xx Error, > 3 s Warning |

**Found while fixing** — none of these were in the analysis:

- **The COR certified classes from every term.** `RegistrationFormAsync` gathered approved sections
  with no semester filter, so a returning student's form listed last semester's classes under this
  semester's heading — the F-09 shape again, on a printed, signed document. Now scoped to the term.
- **A staff correction broke the SIS case convention.** `UpdateRegistrationEndpoint` wrote names in
  Proper Case and the email in lower case while everything else stores CAPS, so a corrected record
  fell out of the "one SIS per email" check (which compares capitals) and out of the F-03 match.
  All three SIS writers now share `SisText.Caps`.
- **The capacity override audited twice on a lost race.** It recorded inside its retry loop, so
  the losing attempt's entry committed alongside the winner's. `AuditLog.DiscardUnsaved()` now
  drops staged entries before a retry; the new reconcile uses it too.
- **The report tint hash could crash.** `ScheduleGridKit.TintIndex` took `Math.Abs` of a wrapping
  `int`, which throws `OverflowException` when the hash lands on `int.MinValue`. Removed along with
  the third palette.
- **`apiDownload` revoked its object URL after 0 ms** — the exact pitfall `saveBlob` documents and
  guards against. It now uses `saveBlob`.
- **The seat-count query worked in memory and failed against SQL Server** (a `WHERE` over a
  positional record's constructor). Caught by the live run, not the tests — the in-memory caveat in
  `TestDb` is not theoretical.

**Verified**: 127 tests green (up from 76), with the new ones mutation-checked — perturbing the
palette hash fails all six pinned hues, counting a pending request as a seat fails 1, and switching
the publish guard off fails 1. Lint clean (the same 8 pre-existing warnings). A scratch build on
:5299 against the dev database exercised every new and changed endpoint read-only, and every report
whose code changed was downloaded and opened (valid `PK`/`%PDF`, legend present, institution
printed).

**Worth knowing about the demo data**: the confirmed seeded students hold approved seats in BSCS and
BSIT sections that are not in their own (ITP) plan, and several have plans that resolve to zero
subjects due — so on this database the Enrolled marker reads "No subjects due" or "Not enlisted"
for everyone. That is the marker telling the truth about seed data assembled before plans existed.

## P0 — correctness / data integrity · 5 of 6 done

The one remaining P0 is not engineering time — it needs real secret values.

- [x] **F-08** Seats can never be released — `EnrolledCount` only increments; no drop/revoke path
- [x] **F-09** Approval-time overlap check is not term-scoped → false conflicts for returning students
- [x] **F-01** `EnrollmentStage` gates nothing; the banner and the API can disagree
- [x] **F-20** Queues cap at 500/1000 with client-side search → records silently invisible
- [x] **F-11** No record of completed subjects — prerequisites, year-level advance, and repeats all rest on it **[decided: verdicts, not grades]**
- [ ] Secrets committed to `appsettings.json` (JWT key, seed admin password, SMTP identity)

## P1 — workflow gaps & hardening · 12 of 13 done

**Everything remaining at P1 is a decision.** No engineering-time item is left at this priority.

- [x] **F-05** No actual document upload — **decided: track, not upload**; wording and spec now say so
- [x] **F-06** No `UpdatedAtUtc` / `VerifiedByUserId` on a checklist row
- [x] **F-07** Reminder blast is synchronous, uncapped, and re-sendable without limit
- [x] **F-10** Prerequisites modeled and printed but never enforced
- [x] **F-12** Bulk approve: N inline SMTP sends per request (the "batch the saves" half is won't-fix — see above)
- [x] **F-15** Publish doesn't require finalization — **decided: required**, with a dry-run confirmation
- [x] **F-16** No optimistic concurrency on schedule writes (only `Section` has `RowVersion`)
- [x] No login rate-limiting / lockout (brute force)
- [x] No global 401/expiry handling on the client
- [x] No security response headers (nosniff, frame-ancestors/CSP, Referrer-Policy)
- [x] No top-level React error boundary
- [x] No automated tests at all — 37 now, unit-level; the CSP engine and DB-level integration remain
- [ ] JWT in `localStorage` + 8-hour lifetime **[decision]** — considered in pass 11 and left as is

> **These were not theoretical.** During the previous pass a single `POST /publishing/{id}/publish`
> against the dev database published 19 draft rows and sent **15 real emails** through the live
> Gmail account inside one request — four of them to real mailboxes. That is the failure mode both
> findings describe, observed.
>
> The outbox now bounds all three bulk paths including publish, so the request returns immediately
> and every recipient is visible in one table with a status. It does **not** make an accidental
> publish recoverable: the rows commit and the mail goes out on the next dispatcher cycle. The
> remaining gap is a **dry-run or confirmation step on the bulk paths** — "this will publish 19
> classes and notify 15 people, continue?" — which is what would have made that mistake harmless.
> Added to P2 below; it is a UX decision about how those buttons behave, not a bug.

## P2 — quality, consistency, UX · 21 of 21 done

- [x] **F-03** No duplicate-person detection on SIS submission — flagged, never blocked
- [x] **F-04** Student cannot correct their own pending SIS
- [x] **F-13** Cancellation audited as `SlotRequested`
- [x] **F-02** No documented precedence between the stage and the parameter switches
- [x] Surface the stage-vs-switch precedence on the Parameters screen (follow-on from F-02)
- [x] **Dry-run / impact confirmation on the bulk paths** (publish now previews before it runs) — publish and the reminder sweep should state
      what they are about to do ("19 classes, 15 people") and require a confirmation before doing it.
      The document sweep already confirms; publish does not, and publish is the irreversible one **[decision]**
- [x] An admin view over the outbox — a failed notice is now recorded but nothing surfaces it
- [x] **F-14** No terminal "enrolled" state per student — **decided: derived marker**
- [x] **F-17** No proactive "Finalized — reopen to edit" banner
- [x] **F-18** Fullscreen height doesn't re-fit on window resize
- [x] **F-19** Hover tooltip missing on the My schedule view
- [x] `parseError` duplicated across 20 api modules → one shared `apiFetch`
- [x] ProblemDetails `detail`/`reference` parsed only in `scheduling/api.js`
- [x] Time/day formatting duplicated — client in `calendarUtils.js` (pass 10), server in
      `Common/Formatting/ClockText.cs` (pass 11): one source of truth per side
- [x] Subject-colour palette duplicated — client is the source of truth; the server mirror is pinned
      to it by `SubjectPaletteTests`
- [x] `.alert` banners lack `role="alert"` / `aria-live`
- [x] Verify icon-button labels + modal focus trapping/restore (labels were already fine; focus was not)
- [x] `DateTime.Now` vs `DateTime.UtcNow` mixed (8 sites, 3 report files)
- [x] Client bundle > 500 kB — route-level code splitting; main chunk now 330 kB
- [x] No `/health` endpoint (pass 7); sparse operational logging (pass 11, request logging)
- [x] No timeout/cancel on client downloads

## P3 — scheduling engine & reports · 7 of 12 done

- [x] Time-grid granularity — **the finding was stale**; the engine already places exact-length blocks
- [x] No UI to re-enter a seed and reproduce a past arrangement
- [ ] Determinism per-seed vs. absolute **[decision]** — now *tested* in both directions (same seed → same timetable; different seeds → different ones), so the current answer is locked in and provable. Whether it is the *wanted* answer is still a product call.
- [x] Pigeonhole pre-check counted meetings only — now counts teaching minutes as well
- [x] Raw exception detail exposed to the Academic Head on a 500 — **decided: trace id only**
- [ ] Program Head role not modeled (memo reuses Academic Head for THRU and FROM)
- [ ] STI lab-credit factor not modeled (Teaching vs Contact hrs)
- [x] Institution/branch name hardcoded "STI" — **decided: configurable** (System parameters)
- [ ] "Class No." is a proxy (`SectionCode`)
- [x] Per-faculty schedule grid lacks the instructor detail + colour-coding (it had colour, from a
      third palette; now the board's, plus the class grid's block layout and legend)
- [ ] In-memory aggregation in the **report** endpoints (the list endpoints were fixed with F-20)
- [x] No CI pipeline

---

# Part 3 — Detail by area

## Scheduling engine (CSP)

- ~~**Time-grid granularity.**~~ — **the finding was stale, and is now disproved by test.**
  `BuildContiguousBlocks` anchors a block of *exactly* the required length at a period start rather
  than consuming whole periods, so a 2-hour subject gets 120 minutes, not 180. Verified across 60,
  120, 150 and 180-minute meetings, plus the two properties that make exact-length blocks safe: they
  still start on a period boundary (no 09:07 classes), and two odd-length meetings in one room still
  cannot overlap. Reverting the engine to the old whole-period behaviour fails 5 of those tests.

  The lesson for this file: a finding written against one implementation can outlive it silently.
  Worth re-testing an old P3 before budgeting to fix it.
- ~~**Reproduce a specific arrangement.**~~ — **done** (pass 11): an optional "Reproduce #" field
  beside Generate passes the seed through. It reproduces the arrangement only over the same inputs
  (rooms, load, time slots), and its tooltip says so.
- **[decision] Determinism vs. variety.** Output is deterministic *per seed*, not absolute. If the
  spec requires one fixed timetable for identical inputs, revisit.
- ~~**Pigeonhole pre-check messaging.**~~ — **fixed, and it was a real gap rather than a wording
  one.** The check counted meetings against slots, so two 3-hour laboratories against three 90-minute
  periods passed it ("2 meetings ≤ 3 slots") despite needing 6 hours of a week that offers 4.5. That
  reached the search and failed there — correct, but at the cost of the whole time budget. It now
  also compares **teaching minutes against the timetable's total minutes**, for faculty and cohorts
  alike, and the message quotes both figures so the Academic Head knows how much to move.

  Both tests are deliberately *necessary* conditions, never sufficient: they under-detect rather than
  over-detect, because a pre-check that rejected a feasible timetable would be far worse than one
  that occasionally lets an impossible one reach the search. A test pins the exactly-fitting case for
  that reason — changing the comparison from `>` to `>=` fails it.
- ~~**No tests.**~~ — covered since pass 8; see *Testing* below.

## Schedule generation — error handling

- ~~**[decision] Exception detail exposure.**~~ — **decided: trace id only** (pass 11). Both the
  global handler and the generation endpoint now return a fixed sentence plus the trace id; the
  exception's type and message stay in the server log (and, for generation, the staff-only audit
  trail), found by that id.

## Enlistment (see F-08 – F-14)

The enlistment slice is the most-developed part of the system and also where the remaining
correctness work is concentrated. The per-request checks are genuinely careful — eligibility,
plan membership, prerequisites, duplicate subject, unit ceiling, capacity under optimistic
concurrency with a DB CHECK backstop, and time overlap. What was missing was the **reverse
direction** (F-08) and **term scoping on the approval leg** (F-09); both are closed. The **academic
history** the plan and prerequisites logically require (F-10/F-11) is closed too. What remains is the
**transport** of the bulk paths (F-12).

Two invariants now hold here and are worth protecting. One place takes a seat
(`ApprovalsEndpoints.TryApproveAsync`) and one place gives it back (`SeatRelease.ReleaseAsync`).
And one place answers "what has this student earned?" — `AcademicHistory` — which the prerequisite
gate, the enlistment plan, the year-level derivation, and the Registrar's own sheet all read, so the
sheet can never show a student as having met something the gate then refuses them for.

The seat lifecycle now reads in both directions, and the asymmetry that made F-08 possible is gone:
one place takes a seat (`ApprovalsEndpoints.TryApproveAsync`) and one place gives it back
(`SeatRelease.ReleaseAsync`), under the same concurrency discipline. Any future path that needs to
move a seat should go through one of those two rather than touching `EnrolledCount` directly — that
is the invariant worth protecting here.

## Documents (see F-05 – F-07)

The checklist is catalog-driven, program- and student-type-scoped, and correctly filtered through
`DocumentChecklist.Applicable`. The gap is not the model — it is that nothing is ever *submitted*
through the system, and that a decision on a row leaves no structured trace.

## Reports — Confirmation of Faculty Loading

- **Program Head not modeled.** No Program Head role exists, so the memo's **THRU** and **FROM** both
  use the active Academic Head (Noted = School Admin). Add a Program Head role/field if the form
  needs a distinct signatory.
- **STI lab-credit factor missing.** The official form shows Teaching hrs < Contact hrs for labs
  (per-subject crediting). We store only Units + Hours, so the report shows Units (credited load) +
  Contact hours (meeting duration). Add a per-subject teaching-credit factor for an exact match.
  `Features/Reports/FacultyLoading/FacultyLoadingPdfModels.cs`.
- ~~**[decision] Institution/branch name hardcoded.**~~ — **configurable** (pass 11):
  `SystemSettings.InstitutionName`, default "STI College Alaminos", set on System parameters and
  printed in capitals on the memo, prospectus, evaluation sheet, and COR. **Not yet** on the email
  footers — those are static builders with no settings access, and threading it through ~17 call
  sites was left for when a second branch actually exists.
- **Class No. proxy.** "Class No." maps to `Section.SectionCode` (no real STI class number exists in
  the data).
- **"No. of students" inherits the `EnrolledCount` drift** described in F-08 — it uses
  `Section.EnrolledCount`, falling back to `Capacity` when enrollment is 0
  (`FacultyLoadingPdfModels.cs:167`). Confirm this matches how the registrar reads the figure, and
  fix F-08 before trusting it.

## Reports — grid schedules

- ~~**Individual (per-faculty) schedule grid**~~ — **done** (pass 11). The finding was half stale:
  the grid was already colour-coded, but from `ScheduleGridKit.BlockTints` — a third palette keyed
  on subject code, so a subject printed a different colour than it showed on the board (the room
  grid had the same problem). Both now use `SubjectPalette`; the faculty grid also gets the class
  grid's block layout (bold code, title, section, room · LEC/LAB, 12-hour time) and a subject legend.

## Testing

- **`SENGENSystem.Server.Tests` now exists** — xUnit, 37 tests, green, registered in the solution.
  It covers `AcademicHistory`, `EnlistmentPlanner`, `YearLevelPolicy`, and `LoginThrottle`: the
  policy layer, in memory. `InternalsVisibleTo` lets the test assembly at those types rather than
  widening them to public for a test runner's sake.

  Two limits are deliberate and should not be mistaken for coverage. The in-memory provider does not
  enforce the `CK_Sections_EnrolledCount` check, the filtered unique index, or the rowversion tokens,
  so **nothing that depends on the database is asserted** — a test there would report success while
  proving nothing. And the F-10 prerequisite refusal is covered at the helper, not at the two
  endpoints that call it.
- **A note on test honesty.** One test in this suite originally re-implemented the retry endpoint's
  steps inline instead of calling it — which would have passed even if the endpoint stopped doing
  them. `OutboxEndpoints.Requeue` was made `internal` so the test calls the real thing; deleting the
  attempt-counter reset now fails it, where before it would not have. Worth watching for: a test that
  restates the implementation rather than exercising it is worse than no test, because it reads like
  coverage.
- ~~**The CSP engine is the largest untested surface**~~ — **covered** (20 tests,
  `SENGENSystem.Server.Tests/Scheduling/`): hard constraints never violated, full weekly-hours block
  coverage including the no-block-across-a-break rule, seeded reproducibility in both directions
  (same seed → same timetable; different seeds → genuinely different ones), and every infeasibility
  diagnostic. Mutation-checked three ways.
- **Still to cover.** In rough order of value:
  - the enlistment gates (each blocker independently, the capacity race, the overlap rule — including
    the F-09 cross-term case as a regression test, and the F-10 prerequisite gate on **both** legs,
    which is the one part of that fix live verification could not reach),
  - `AcademicHistory` itself: the fall-open rule when nothing is on file, a transferee's credited
    subject satisfying a prerequisite, and a subject failed then passed counting its units once,
  - report generation smoke tests (PDF/XLSX builders return non-empty, valid files).
  `Features/Scheduling/Engine/*`, `Features/Enlistment/*`, `Features/Reports/*`.
- ~~No CI pipeline to run build + tests on push~~ — **added** (`.github/workflows/ci.yml`), now that
  there are tests for it to run. Two jobs: server (restore, build, `dotnet test`) and client (`npm
  ci`, lint, build).

  Two details worth keeping. **Lint runs before the build**, because in this repository the build
  catches almost nothing on the client — Vite transpiles without type-checking, so `npm run build`
  passes over undefined identifiers and unreachable code that `eslint` catches. **The server job
  passes `-p:BuildClient=false`**: the server csproj references the client `.esproj` (which is what
  makes F5 start both halves), and without the flag a server-only job would need the Visual Studio
  JavaScript SDK on the runner and would run `npm install` just to execute xUnit. Verified locally
  — with the flag the build mentions neither esproj nor npm; without it, the default local
  behaviour is untouched.

  The four pre-existing eslint **errors were fixed** as part of this, since a pipeline that is red
  on its first run is a pipeline people learn to ignore: `confirm.jsx` was splitting Fast Refresh by
  exporting components alongside plain functions (the dialog moved to `ConfirmDialog.jsx`), and
  `GenerateSchedulePage` was setting state synchronously inside an effect (now the "adjust state
  when a prop changes" pattern that `useServerTable` already uses). Eight warnings remain and do not
  fail the build.

## Client architecture (duplication)

- ~~**`parseError` is copy-pasted into 20 `api.js` modules**~~ — **fixed.** `features/shell/apiClient.js`
  owns auth headers, the error shape, 401 handling, and the authenticated-download helper; all 22
  modules, four pages, and two shell helpers go through it. Token storage moved to
  `features/shell/token.js` to break the resulting import cycle — which also means the open
  "JWT in localStorage" decision is now a change to one file rather than twenty-two.
- ~~**ProblemDetails parsing is inconsistent.**~~ — **fixed.** `detail` and `reference` are read by
  every module now, not just `scheduling/api.js`, so an unexpected 500 shows its exception summary
  and trace id wherever it happens rather than a bare "Something went wrong".
- **Time/day formatting duplicated — client half fixed.** `DAY_NAMES` was declared in three client
  files and is now exported once from `calendarUtils.js`, with `DAY_ABBR` beside it. **Still open on
  the server:** the confirmation report and the grid workbook re-implement 12-hour and
  day-abbreviation formatting of their own. That half cannot be deduplicated *with* the client — it
  is a different language — so the realistic goal is one formatter per side, each documented as the
  source of truth, rather than one shared implementation.
- ~~**Subject-colour palette duplicated.**~~ — **one source of truth** (pass 11): the client's
  `SUBJECT_HUES` / `subjectHue`. The server cannot import it, so `Common/Reporting/SubjectPalette.cs`
  mirrors it and `SubjectPaletteTests` pins the mirror to hues produced by *running the client
  function* — a cross-language contract test rather than a comment asking people to keep two files
  in step.
- ~~**Bundle size.**~~ — **done** (pass 11): every page except login, the forced password change,
  and the shell is `React.lazy`; the shell keeps its sidebar up with a fallback while a page chunk
  loads. Main chunk 330 kB; FullCalendar (230 kB) now arrives only with the board and calendar pages.

## Auth & security

- **[decision] JWT stored in `localStorage`** (now `features/shell/token.js`). Readable by any
  injected script — an XSS bug becomes full token theft. Consider an httpOnly, SameSite cookie, or
  short-lived access tokens with refresh. Newly cheap to act on: the storage is confined to that one
  module and every network call goes through `apiClient`, so changing the scheme is a two-file edit
  rather than a sweep. The CSP added this pass also narrows the XSS route it depends on.
- ~~**No global 401/expiry handling.**~~ — **fixed** in `apiClient.apiFetch`: a 401 on an
  authenticated call clears the token and redirects to login once, preserving where the user was in
  `?next=`. Three exemptions are deliberate and documented at their call sites — `/api/auth/*` (login
  answers a bad password with a 401), `fetchCurrentUser` (its job is to find out whether a session
  exists), and `useNavBadges` (a background poll must not yank the user mid-sentence).
- ~~**No rate limiting / lockout on login.**~~ — fixed in pass 4 (`LoginThrottle` + IP limiter);
  this entry had been left open here by mistake.
- **Long-lived tokens.** `Jwt.ExpiryMinutes` is 480 (8h); combined with `localStorage` a stolen token
  stays valid a long time. Shorten access-token lifetime (+ refresh) once storage is hardened.
- **Elevation breadth.** `SchoolAdminClaimsTransformation` grants a School Admin every role's claims.
  It is deliberate and documented, but it means a single compromised School Admin account is total
  system access, and per-endpoint role checks give no defence in depth. Worth an explicit note in the
  security section of the paper.

## Configuration & secrets

- **Secrets committed to `appsettings.json`** (and `.gitignore` does not exclude it): the JWT signing
  `Key` (still the literal `"CHANGE-THIS-DEV-ONLY-SIGNING-KEY-…"`), the seed **admin email +
  password** (`admin@stialaminos.local` / `Admin@Sengen2026`), and the SMTP account identity. Before
  any real deployment: move these to user-secrets / environment variables / a secrets manager,
  generate a fresh 32+ char JWT key, and force a change of the seeded admin password on first login.
- **Per-environment config.** `Email.ClientBaseUrl` (`https://localhost:51683`) and the LocalDB
  connection string are baked in; production values must come from environment config.

## Data integrity / concurrency

- **No optimistic concurrency on schedule writes** (F-16). `RowVersion` exists only on
  `Domain/Section.cs:45`.
- ~~**`EnrolledCount` is a denormalized counter with no reconciliation**~~ — **on-demand reconcile
  added** (pass 11, see F-08): mismatches are surfaced on the approvals page and corrected one at a
  time, deliberately, never silently.

## Correctness — time zones

- ~~**`DateTime.Now` vs `DateTime.UtcNow` are mixed**~~ — **fixed.** All 8 sites go through
  `Common/InstitutionClock.cs`; there is no `DateTime.Now` left in the server. The rule is now
  stated in one place: **store UTC, convert once at the display edge**, and that edge is this class.

  Worth recording what the bug actually was, because it is invisible in development: `DateTime.Now`
  reads the *server's* locale, so on a UTC-configured host — the default for a container or cloud VM
  — every printed report was stamped eight hours behind the office that generated it, and the room
  grid highlighted the wrong day as "today". The class also treats `DateTimeKind.Unspecified` (what
  EF returns) as UTC, since reading those as local would double-apply the offset.

## Performance / scalability

- ~~**Queues cap instead of paging** (F-20)~~ — **fixed.** Beyond the correctness win, each queue now
  transfers one page (25 rows by default, 200 max) rather than up to 500 fully-hydrated rows, and the
  grouping/counting it used to do in memory happens in SQL.
- ~~**Bulk operations run inline** (F-07, F-12)~~ — **fixed.** Reminders, bulk approval, and schedule
  publishing all queue their mail on `OutboxEmail` and return; `OutboxDispatcher` delivers it in
  bounded batches of 25 with exponential backoff. The reminder sweep is additionally capped at 200
  rows per press and refuses to chase the same enrollee inside 20 hours.
- **In-memory aggregation — reports only, now.** The *list* endpoints were fixed with F-20 (their
  filters, sorts, and summary counts run in SQL). The **report** paths still `ToListAsync()` and then
  group/filter in memory (`BuildRowsAsync`, grid workbook, soft-constraints). Correct and fine at
  institutional scale, but push grouping/sums into SQL if volumes grow.
  `Features/Reports/FacultyLoading/FacultyLoadingReportsEndpoints.cs`,
  `Features/Scheduling/SoftConstraints/*`.
- ~~**No request timeout/cancellation on client downloads.**~~ — **done** (pass 11): `apiDownload`
  is now the one download path (five local copies folded into it), with a 120 s timeout that covers
  the body as well as the headers and an optional cancel signal. The faculty-load reports page — home
  of the slow bulk .zip — shows **Cancel download** while one runs.

## Accessibility (audit needed)

- ~~Error/success banners are plain `<div>`s~~ — **fixed.** 73 banners across 52 files carry a live
  region now: `role="alert"` for errors (interrupts), `role="status"` for successes (polite). Three
  static notices inside modals were deliberately left alone — they render with the dialog rather than
  in response to an action, so a role there would make them interrupt on every open.
- ~~Verify icon-only buttons~~ — **verified, nothing to fix.** Every `modal-close` already had an
  `aria-label`, fullscreen had `aria-pressed`, and there are no svg-only buttons in the app.
- ~~Modals trap focus and restore it on close~~ — **fixed.** `features/shell/useModalFocus.js`, in all
  six dialogs. They were only *visually* modal before: Tab walked straight out into the page behind
  the overlay, which is still interactive to a keyboard and now invisible. `aria-modal` alone does
  not stop that — it hides the background from assistive tech but moves nothing and blocks no keys.
- ~~Sortable-header semantics~~ — **fixed once for every table** in `SortHeader`: `aria-sort` on the
  `<th>`, the arrow kept `aria-hidden`, and an `.sr-only` phrase naming what the next click will do.
  The pager's range readout is now `aria-live="polite"`, so paging is announced at all.

## Web hardening (HTTP headers)

- ~~**No security response headers.**~~ — fixed in pass 4 (`Common/Web/SecurityHeaders.cs`); this
  entry had been left open here by mistake.
- **No CORS policy configured.** Fine while the SPA is served same-origin by the same host; it
  becomes a blocking gap the moment the client is deployed separately. Note it as a deployment
  precondition.

## Observability / ops

- ~~**No health/readiness endpoint.**~~ — **fixed.** Two probes, because liveness and readiness answer
  different questions and conflating them causes restart loops: `/health/live` runs no checks (a
  database outage is not a reason to kill the process), `/health` includes a DB probe so an
  orchestrator stops routing traffic while the database is unreachable without recycling the
  container. Both anonymous — a probe needing a token cannot run before the app is ready.
- ~~**Sparse application logging.**~~ — **done** (pass 11): `Common/Web/RequestLogging.cs` writes
  one structured line per `/api` request (method, path, status, duration, user, trace id). 5xx is
  Error — the exception handler has already logged the stack under the same trace id — and anything
  over 3 s is Warning whatever its status. 4xx stays Information on purpose: a wrong password or a
  full section is the system working. Query strings are never logged (search terms and tokens travel
  there), and static assets, the SignalR hub, and the health probes are skipped.
- ~~**Email delivery is fire-and-forget with no record of failure.**~~ — **fixed for the bulk paths.**
  `OutboxEmail` carries status, attempt count, and last error, and `Failed` is a terminal state
  rather than a deleted row, precisely so "we tried five times over half an hour and that address
  never accepted it" is an answer someone can give. Permanent failures are logged at warning.
  ~~**Still open:** nothing *surfaces* the failures~~ — **fixed.** `/outbox` (School Admin and Super
  Admin) is a paged view of every queued, delivered, and permanently-failed notice, with the last
  error shown against the row: "did that student get the email?" is now answerable, and so is *why
  not*. Failures get their own banner with the count rather than being reachable only by filtering,
  since a failure nobody looks for is a failure nobody finds. Retry is offered per row and in bulk —
  the bulk path confirms first, because an address that failed five times is often one that will
  never work, and retrying it is a burst of mail at a dead mailbox.

  Two refusals are deliberate: an email already **Sent** cannot be retried (that would deliver it
  twice, and an approval notice arriving twice reads as the system malfunctioning), and a **Pending**
  one cannot either, because it is already going to be attempted.

  **Still open:** single interactive mail (password reset, 2FA code) goes inline via `IEmailSender`
  and remains best-effort — the right trade for latency, since the user is waiting on it, but it
  leaves that narrow path unrecorded.

## Client resilience

- ~~**No React error boundary.**~~ — fixed in pass 4 (`features/shell/ErrorBoundary.jsx`); this
  entry had been left open here by mistake.

## Documentation consistency

- The requirements specification marks nearly everything ✅. Several of those were ✅ *as built* but
  rested on the gaps above. Three are now genuinely closed and the spec can say so: **FR-ENL-04** has
  a reverse transition (F-08), **FR-TERM-03**'s widened search reaches every row rather than the
  first 500 (F-20), and **FR-ENL-01/06** now enforces the prerequisites it prints and offers the
  repeats a student owes (F-10/F-11). ~~**FR-DOC-01/02** is the one still overstated~~ — resolved
  by a scope note rather than a downgrade (pass 11): as worded they are met (a checklist the
  Admission Officer maintains), and what overstated things was the section's "submission" implying an
  upload. The note says plainly that papers are handed over in person and nothing is uploaded. The
  spec also gained FR-CYC-05 (stage enforced), FR-PUB-05, FR-SIS-13/14, FR-ENL-18/19, FR-SCHED-17. A spec that
  overstates completeness is a worse problem than an incomplete system, because it removes the reason
  to go back.
- The spec should gain the **F-11 scope boundary** in the same breath as the feature: SEN-GEN records
  a per-term *verdict* (passed / failed / dropped), not a grade. It does not compute standing, does
  not hold a grade scale, and takes the verdict as given by the Registrar from the separate
  student-records system. Stated positively, that is a design decision; left unstated, the first
  reader to look for grades will read it as a gap.
- **FR-SIS-09** should note that a returning student's year level now advances on units earned where
  academic history exists, and on the school year turning over where it does not.
- The spec should also gain a line on the **enrollment stage now being enforced** (F-01): FR-CYC-01
  described it as a property of the term, and it is now the gate that decides whether the SIS and
  slot selection accept anything at all.

## Operational

- **Server restart required** after backend changes (migrations apply automatically at startup via
  `MigrateAsync`). Not a code fix — a deployment note.
