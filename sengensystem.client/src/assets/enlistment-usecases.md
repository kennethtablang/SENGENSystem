# SEN-GEN — Use cases & situations the system will meet

Source: `SEN-GEN_Revised_Enrollment_v22.docx` (scope, roles, the enrollment stages) and
`RequirementsSpecifications.md`, checked against the code as of 2026-11-23.
Focus: **subject / unit selection (enlistment)**. The other roles' cases are grouped after it.

**Status legend**
- **Handled**: the system already does the right thing (the FR or file is named)
- **Partial**: it works, but a common variant is missing or a rough edge remains
- **Gap**: the situation will happen and nothing handles it yet
- **Bug**: the code contradicts the spec or blocks a valid student (checked in code)
- **Policy**: the code is fine either way. STI has to decide the rule first

---

## 1. Who the student is: what they are offered

The planner (`EnlistmentPlan.cs`) builds the subject list from **program → curriculum → year
level → term**, removes subjects already passed or credited, and adds subjects still owed.
Most cases in this section depend on what kind of student it is.

| ID | Situation | What should happen | Status |
|---|---|---|---|
| S01 | **Regular freshman, 1st sem.** No history | Offered Y1-S1 of their program. Prerequisite gate is open because there is nothing to check | Handled (FR-ENL-13, fall-open rule) |
| S02 | **Regular continuing student**, passed everything | Offered their year/term. Year level advances when the school year turns over | Handled, but only if last term's verdicts were encoded in Academic Records |
| S03 | **Failed a subject** last term | It comes back in the next term it runs, marked "repeat" | Handled (FR-ENL-17) |
| S04 | Failed a **prerequisite** (e.g. CS101), and its dependent (CS102) is in this term's list | The dependent shows as **locked, with the reason**, *before* they click | Partial. CS102 is listed as requestable and only refused on click (409). Show the lock in the plan |
| S05 | **Dropped** (verdict Dropped) last term | Same as failed: owed again | Handled |
| S06 | **Irregular student** whose back subject's only section **clashes** with their own block's class | A way out: another section, a petition, or a Registrar decision on which one gives way | **Gap**. The overlap check refuses every option and nothing tells the student what to do next |
| S07 | **Transferee** | Blocked until the credit evaluation is complete. Credited subjects are removed and "to take" subjects from earlier years are added | Handled (FR-ENL-12, FR-TRAN) |
| S08 | Transferee evaluation **reopened after** the student already holds approved seats | Seats in subjects now marked *credited* are flagged for the Registrar | Gap (verify). Reopening closes the gate but leaves existing seats alone |
| S09 | **Curriculum revised.** A 2nd-year student entered under the 2025 curriculum and the 2026 one is now active | They stay on the curriculum they entered under | **Fixed** (2026-11-23). `StudentCurriculum.ResolveAsync` now picks the curriculum effective for the school year of the student's entry term (`CurriculumSchoolYear`). It falls back to the active curriculum only when no curriculum is linked to that year. Used by the plan, year-level derivation, academic records, and new transferee evaluations |
| S10 | **Shiftee** (changes program within STI, e.g. ITP → HRS) | A credit evaluation like a transferee's: common GE subjects carry over | **Gap**. `StudentType` is only New / Transferee. Editing the program moves them to the new curriculum, where none of their passed subjects match |
| S11 | **Returning after a leave (LOA)** | Term activation. Year level comes from earned units. Curriculum stays pinned | Partial (term activation works). Also hit by S09 if the curriculum changed while they were away |
| S12 | **Repeating a whole year** | Only the subjects not yet passed are offered | Handled |
| S13 | **Prior-term verdicts not yet encoded** when 2nd-sem enlistment opens (grades still being finalized) | They can enlist provisionally. The prerequisite is checked again once the verdict arrives | **Gap**. Any student with *some* history is refused a subject whose prerequisite they are still finishing. This will happen every January |
| S14 | **Co-requisites** (lecture and lab, or two subjects that must be taken together in the same term) | Allowed when the co-requisite is passed **or** requested/approved this term. Dropping one flags the other | **Fixed** (2026-11-23). A same-year, same-term edge (`SubjectPrerequisite.IsCoRequisitePair`, the generator's own definition) is met when the co-requisite is passed **or** the student holds a requested/approved seat in it this term, at request and at approval. The refusal says "taken together with…", not "not passed". Still open: dropping one half doesn't flag the other (P14) |
| S15 | **Graduating student** needs more units than the ceiling | Per-student overload, approved by the Registrar or Academic Head and audited | Gap. `MaxEnlistmentUnitsPerStudent` is global only |
| S16 | **Underload.** Student takes 2 of 7 subjects | The "Enrolled" status shows it as incomplete. STI decides whether a minimum load exists | Partial / Policy |
| S17 | **Advance subject** (take a higher-year subject to catch up) | Petition, then the Registrar adds it to that student's plan | Gap. The plan only goes *down* in year level, never up |
| S18 | **Cross-enrollee / second-courser** | Decide whether it's in scope | Policy (probably out of scope; say so in §6) |
| S19 | **Continuing student, no history on file** (school adopts SEN-GEN mid-program) | Gate falls open. Academic Records shows how many such students there are | Handled. Known risk, documented |
| S20 | **Wrong year level or program** on the record | Student sees "see the Registrar". Staff fix it and the plan recomputes | Partial. No in-app "this looks wrong" report, and see R03 for requests already filed |
| S21 | Subject they owe has **no published section** | Shown as "no class yet" **and** counted as demand for the Academic Head | Partial. The student sees it, but nobody is told there is demand |
| S22 | **No curriculum** exists for the program | Falls open with a notice | Partial. While open, the student can request *any* published section, including another program's |
| S23 | **Lecture + laboratory subject** | One section, both meetings come together | Handled (two CSP meetings, one Section) |

## 2. Picking sections and building the load

| ID | Situation | What should happen | Status |
|---|---|---|---|
| P01 | **Regular student wants their block** (BSCS 1-A) | One click to request every section of their block | Gap (quality of life). Today it's one request per subject |
| P02 | Student **mixes sections across blocks** | Allowed if nothing overlaps, or forbidden for regulars | Handled technically. Policy: are regulars locked to a block? |
| P03 | Section **full when browsing** | Refused, with other sections suggested | Handled (refused). Suggesting alternatives would help |
| P04 | **Oversubscribed.** 60 pending requests for 40 seats (pending requests don't hold seats) | The student knows where they stand, and the extra requests resolve themselves | Partial. Approving #41 fails with "full, reject instead". The other 20 wait for a manual reject. Options: hold a seat on request with an expiry, or auto-reject or waitlist the rest when the section fills |
| P05 | **Waitlist** for a full section | Joins a queue and is offered a seat when someone drops | Gap |
| P06 | **Change section** (A → B, same subject) | One swap request that keeps seat A until B is granted | **Gap**. The duplicate-subject rule forces them to cancel or drop A first, so they can lose both |
| P07 | **Time overlap** between two choices | Refused at request and at approval | Handled (FR-ENL-07) |
| P08 | **A published class is moved after approval** (amendment) and now overlaps another of the student's classes | Amendment notice names the new clash, and the Registrar sees the affected students | Gap. FR-PUB-04 sends a notice, but nothing re-checks overlaps |
| P09 | **Unit ceiling** reached | Refused, naming the ceiling. Pending requests count toward it | Handled (FR-ENL-09) |
| P10 | Ceiling **lowered mid-window** below what students already hold | Policy: existing loads keep their seats, new requests are refused | Policy (that's what happens today) |
| P11 | **Double-click / two tabs** | Only one request is created | Handled (unique live-request index) |
| P12 | **Stale page / hand-made API call** | The server rechecks everything | Handled (FR-ENL-14) |
| P13 | Cancel a pending request / drop an approved seat | Cancel any time. Students can drop during the window, staff any time | Handled |
| P14 | Drops a **co-requisite** but keeps its partner | Warn the student or block the drop | Gap (comes with S14) |
| P15 | **Withdraws from the term** entirely | One "withdraw" that releases every seat, with a reason | Partial. Only drops one at a time |
| P16 | **Enlistment closes mid-session** | Next request is refused and the reason names the switch or stage | Handled (FR-CYC-05) |
| P17 | **Session expires** while choosing | Re-login returns them to the page with the choices intact | Verify on the client |
| P18 | **Opening-minute rush** (hundreds at once) | No lost or duplicate seats, and the page stays responsive | Partial. Concurrency is proven. Load testing is still 🟡 in the NFRs |

## 3. Registrar

| ID | Situation | What should happen | Status |
|---|---|---|---|
| R01 | Approve one request, a student's whole load, or the whole queue | Every request is still checked individually | Handled (FR-ENL-11) |
| R02 | Reject with a reason | Student is notified with the reason | Handled |
| R03 | Something changed **between request and approval**: pre-authorization revoked, SIS un-confirmed, year level or program edited, transferee evaluation reopened, unit ceiling lowered, window closed | Approval reruns **every** request-time check | **Gap**. `TryApproveAsync` rechecks prerequisites, overlap and capacity only. Eligibility, plan membership and the unit ceiling are not rechecked, so a request for a subject no longer in the plan can still be approved. Fix: one shared validator for both legs |
| R04 | **Capacity override** | Never above `SectionCapacityCap` (40) or the room's real capacity | **Fixed** (2026-11-23). Raising the cap is refused past `SectionCapacityCap` or the smallest room the section meets in. Lowering a section that is already over the cap is still allowed |
| R05 | **Walk-in student** with no device or internet | Registrar enlists on their behalf. Same checks, audited as assisted | **Gap**. There is no staff-side "create request" endpoint. The thesis's equity argument needs this fallback |
| R06 | **Late add** after the window (adjustment period) | Staff can add (R05) under the same checks | Gap. Staff can drop after close, but can't add |
| R07 | Seat count disagrees with approved requests | Mismatch shown and corrected deliberately | Handled (FR-ENL-19) |
| R08 | Who is fully enrolled? COR printing | Enrolled status is derived. The COR covers one term | Handled (FR-ENL-18) |
| R09 | **Pending requests left over** when the stage closes or the term rolls over | Expired automatically, and the student is told | Partial. They drop out of the queue (FR-TERM-02) but stay "Requested" forever |
| R10 | **Fairness / priority** (graduating students or irregulars first) | Optional enlistment windows by year level or student type | Policy / Gap |
| R11 | Approval queue becomes the new bottleneck | Optional **auto-approve** when every check passes, so the Registrar only handles exceptions | Policy. Worth asking the Registrar |
| R12 | Encode verdicts for a whole cohort | Bulk .xlsx import | Handled (FR-ENL-15) |

## 4. Academic Head

| ID | Situation | What should happen | Status |
|---|---|---|---|
| A01 | **Demand exceeds supply** mid-enlistment | "Section full" alert, then open a section on the board, then publish | Partial. Alert exists. Check that a new section published mid-window shows up in students' plans immediately |
| A02 | **Dissolve an under-enrolled section** (below `MinSectionEnrollment`) | Move or release the holders in one action and notify them | Gap. Below-minimum sections are only reported. There is no dissolve-and-move flow |
| A03 | **Demand forecast before generating** (how many sections of a subject?) | Count irregulars, repeaters and back subjects, not just the blocks | Gap. The generator plans from cohorts only, so S06 and S21 will keep happening |
| A04 | **Curriculum edit during enlistment** (units, prerequisites, archiving a subject someone requested) | Lock curriculum edits while the stage is open, or flag affected requests | Gap (verify) |
| A05 | **Faculty replaced** on a published class | Amendment notice to the faculty and the students | Verify that FR-PUB-04 covers a faculty change as well as a move |
| A06 | Stage **stepped back** from Enlistment while requests exist | Requests frozen, not lost | Partial. They stay, but nobody is told |

## 5. Admission Officer

| ID | Situation | What should happen | Status |
|---|---|---|---|
| D01 | Pre-authorize once the authorization papers are on file | Server gate names the missing paper | Handled (FR-PRE-02/04) |
| D02 | **Revoke pre-authorization** from a student who already holds seats | Decide: keep, freeze, or release the seats | Gap / Policy (today the seats stay and pending ones can still be approved, see R03) |
| D03 | Override year level on term activation (repeater, back from leave) | Allowed, audited, and the plan recomputes | Handled. Requests already filed: see R03 |
| D04 | Papers arrive after enlistment | Follow-up only, never a gate | Handled |

## 6. Faculty, School Admin, system

| ID | Situation | What should happen | Status |
|---|---|---|---|
| F01 | Faculty sees seat counts per class | Live counts on My schedule | Handled (FR-FAC-05) |
| F02 | Faculty needs the **class list** (names) | Downloadable roster per section | Verify |
| F03 | Class moved after publish | Faculty notified | Handled (FR-PUB-04) |
| X01 | Email provider down | Bell notice is committed, email retried from the outbox | Handled |
| X02 | Student account deactivated while holding seats | Decide what happens to the seats | Gap / Policy |
| X03 | Archive a finished term | Read-only, still reportable | Handled (FR-TERM-07) |

## 7. Before enlistment: what keeps a student out

All of these show the student the exact blocker (`EnlistmentEligibility`):

- Credential email went to a mistyped address. The Registrar corrects the SIS email and re-sends credentials. **Verify a re-send exists.**
- Forgot password: handled (one-time token).
- Account not linked or the claim fails on date of birth: handled, with a clear message.
- SIS not confirmed, not pre-authorized, transferee not evaluated: handled. Each is a named blocker.
- Enlistment stage not open yet: the stage ticker tells everyone.

---

## Fix order (suggested)

1. ~~**S14 co-requisites**~~: fixed.
2. ~~**S09 pin each student's curriculum**~~: fixed.
3. **R03 one validator for request and approval** (gap): approvals can currently skip plan and eligibility checks.
4. ~~**R04 cap the override**~~: fixed.
5. **S13 verdicts not yet encoded**: allow provisional enlistment, or have the Registrar encode verdicts before the 2nd-sem window opens.
6. **R05 / R06 assisted enlistment and late adds** for walk-ins.
7. **P06 section swap**, then **P04 / P05 oversubscription and waitlist**.
8. **S06 / A03** irregular-student clashes and demand forecasting.
9. **S10 shiftees** (reuse the transferee evaluation sheet).
10. **P08** overlap re-check on amendments, **A02** dissolve-and-move.

## Questions to settle with the Registrar / Academic Head

1. Are **regular students locked to their block**, or can they mix sections (P02)?
2. Should requests that pass every check be **auto-approved** (R11)?
3. Are **overloads** (S15) and **advance subjects** (S17) allowed, and who approves them?
4. Is there a **minimum load** (S16)?
5. Do pending requests **hold a seat**, or is it first-approved-first-served (P04)?
6. **Priority windows** for graduating students or irregulars (R10)?
7. What happens to seats when **pre-authorization is revoked** or an account is deactivated (D02, X02)?
8. Are **cross-enrollees / second-coursers** in scope (S18)?
9. When are last term's **verdicts final** relative to the 2nd-sem enlistment window (S13)?
