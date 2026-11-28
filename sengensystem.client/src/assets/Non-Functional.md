# SEN-GEN — Non-Functional Requirements

**How well the system must work: quality, performance, security and operations, measured against ISO/IEC 25010.**

| | |
|---|---|
| **System** | SEN-GEN: Student Enrollment and Constraint Satisfaction Problem–Based Generative Class Scheduling Engine for STI Alaminos |
| **Companion documents** | `Functions.md` (what the system does) · `RequirementsSpecifications.md` §4 (the formal NFR list this file expands) |
| **Quality model** | ISO/IEC 25010 product quality characteristics |
| **Derived from** | The server's middleware, policies and engine (`SENGENSystem.Server`), the client (`sengensystem.client`), the test suite (`SENGENSystem.Server.Tests`), and the deployment and maintenance plans (`deploy/`) |
| **Date** | 27 November 2026 |

**Status legend:** ✅ Met and in the code · 🟡 Partly met, with the gap named · ⬜ Not yet done

---

## Contents

1. [Purpose and evaluation target](#1-purpose-and-evaluation-target)
2. [Summary by quality characteristic](#2-summary-by-quality-characteristic)
3. [NFR-1 Functional suitability](#3-nfr-1-functional-suitability)
4. [NFR-2 Performance efficiency](#4-nfr-2-performance-efficiency)
5. [NFR-3 Usability (interaction capability)](#5-nfr-3-usability-interaction-capability)
6. [NFR-4 Reliability](#6-nfr-4-reliability)
7. [NFR-5 Maintainability](#7-nfr-5-maintainability)
8. [NFR-6 Portability (flexibility)](#8-nfr-6-portability-flexibility)
9. [NFR-7 Security and data privacy](#9-nfr-7-security-and-data-privacy)
10. [NFR-8 Compatibility](#10-nfr-8-compatibility)
11. [Operational requirements](#11-operational-requirements)
12. [Design constraints](#12-design-constraints)
13. [Verification: tests and evaluation](#13-verification-tests-and-evaluation)
14. [Open items](#14-open-items)

---

## 1. Purpose and evaluation target

Non-functional requirements describe **how well** SEN-GEN must do the things listed in `Functions.md`: how fast, how safely, how reliably, and how easily. They are grouped by the ISO/IEC 25010 product quality characteristics, which are also the dimensions of the study's evaluation survey.

### 1.1 Evaluation target

| Item | Value |
|---|---|
| Respondents | 45, purposively sampled: 30 students, 10 faculty, 5 administrative staff |
| Instrument | ISO/IEC 25010 questionnaire on a 5-point Likert scale, in English and Filipino, content-validated by 3 IT experts |
| Where it is taken | Inside SEN-GEN itself (the built-in rating survey, Super Admin only) |
| Scoring | Weighted mean per characteristic and overall |
| **Success benchmark** | **Overall weighted mean ≥ 4.00 ("Very Good")** |

**Interpretation scale**

| Weighted mean | Interpretation |
|---|---|
| 4.50 – 5.00 | Excellent |
| 3.50 – 4.49 | Very Good |
| 2.50 – 3.49 | Good |
| 1.50 – 2.49 | Fair |
| 1.00 – 1.49 | Poor |

### 1.2 Characteristics in the survey

The built-in survey (`Features/Survey/SurveyContent.cs`) asks about eight characteristics:

1. Functional Suitability
2. Performance Efficiency
3. Compatibility
4. Usability
5. Reliability
6. Security
7. Maintainability
8. Portability

> **Note on ISO/IEC 25010:2023 naming.** The 2023 revision renames *Usability* to **Interaction capability** and *Portability* to **Flexibility**, and adds **Safety**. This document keeps the familiar names, which are also the ones the survey uses, and gives the 2023 name in brackets. Safety (harm to people, property or the environment) doesn't apply to an enrollment and scheduling system beyond the data-protection duties in NFR-7.

---

## 2. Summary by quality characteristic

| ID | Characteristic | What it means for SEN-GEN | Status |
|---|---|---|---|
| NFR-1 | Functional suitability | Schedules never break a hard rule; seat counts, plans and confirmations are correct | ✅ |
| NFR-2 | Performance efficiency | A schedule generates in seconds; pages stay responsive during enrollment rush | ✅ / 🟡 load test pending |
| NFR-3 | Usability | Each role sees only its work, learns it without training, on phone or desktop | ✅ / 🟡 formal accessibility audit pending |
| NFR-4 | Reliability | No lost or duplicated data; failures are contained and recoverable | ✅ / 🟡 production uptime not yet observed |
| NFR-5 | Maintainability | Features can be changed in isolation; school policy is data, not code | ✅ |
| NFR-6 | Portability | Runs on standard Windows/IIS hosting and any modern browser | ✅ |
| NFR-7 | Security & privacy | RA 10173 compliance, least-privilege access, brute-force and takeover resistance | ✅ |
| NFR-8 | Compatibility | Works alongside the school's existing records system, Excel, PDF and email | ✅ |

---

## 3. NFR-1 Functional suitability

*Does the system do the right thing, completely and correctly?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-1.1 | **Functional correctness** of generated schedules | **0 hard-constraint violations** (100% satisfaction) | `SearchState.IsConsistent` rejects any placement that would cause room double-booking, faculty double-booking, a cohort overlap, a room too small, or the wrong room kind. Faculty ceilings are checked on the allocation before the search. The manual board runs the same checks on every drop, move and resize. Verified by `CspSchedulerHardConstraintTests`. | ✅ |
| NFR-1.2 | Correct seat counts | Never more than the section capacity (max 40) | Optimistic concurrency on `Section.RowVersion`, plus a database CHECK constraint `EnrolledCount <= Capacity`. A two-thread race for the last seat lets exactly one approval win. A seat-count check finds and corrects drift. Verified by `DatabaseConstraintTests` and `ApprovalTests`. | ✅ |
| NFR-1.3 | Correct enlistment rules | A student is offered and allowed only the subjects they owe | One planner (`EnlistmentPlanner`) answers both the page and the request, and the server re-checks prerequisites at request and at approval. Verified by `EnlistmentPlannerTests`, `AcademicHistoryTests` and `EnrollmentCompletionTests`. | ✅ |
| NFR-1.4 | Correct derived values | Year level and "enrolled for the term" are never stale | Derived on read from one rule each (`YearLevelPolicy`, `EnrollmentCompletion`) rather than stored. Verified by `YearLevelPolicyTests`. | ✅ |
| NFR-1.5 | **Functional completeness** | Stages 1–3 of enrollment plus scheduling are fully digital | Document checklist, SIS, enlistment, CSP scheduling, publishing, reports. See `Functions.md`. | ✅ |
| NFR-1.6 | **Functional appropriateness** | Each role reaches its task in few steps | Role-filtered sidebar, badges pointing at outstanding work, bulk actions (bulk approve, bulk pre-authorize, reminders to all). | ✅ |
| NFR-1.7 | Faithful official documents | Printouts match the school's own instruments | The Confirmation of Faculty Loading, scheduling grid, prospectus, evaluation sheet and COR reproduce the paper forms. Verified by `ReportFidelityTests` and `ReportSmokeTests`. | ✅ |

---

## 4. NFR-2 Performance efficiency

*Is it fast enough, and does it use resources sensibly?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-2.1 | **Schedule generation time** | Well under **30 s** for STI Alaminos-scale data (dozens of sections) | Backtracking with most-constrained-variable ordering. Bounded by a wall-clock budget (default **20 s**) and a search-step budget (default **2,000k steps**), both editable in System parameters. | ✅ |
| NFR-2.2 | Reproducible runs | Same inputs + arrangement number → same timetable | Deterministic engine. Verified by `CspSchedulerReproducibilityTests`. | ✅ |
| NFR-2.3 | Responsive lists | Queues stay fast as records grow | Server-side filtering, sorting and paging (`useServerTable`) on the large queues, read-only queries (`AsNoTracking`), and 34 database indexes declared in `AppDbContext`. | ✅ |
| NFR-2.4 | Bulk email doesn't block the user | An enrollment-sized reminder or publish returns immediately | Bulk mail is written to an **outbox** in the same transaction and sent by a background dispatcher in batches of 25. Only one-off interactive mail (reset link, 2FA code) is sent inline. | ✅ |
| NFR-2.5 | Live updates without polling storms | Report pages refresh on change, not on a timer | SignalR `/hubs/reports` pushes a light "changed" signal; pages refetch with a 500 ms debounce. The broadcaster is fire-and-forget. | ✅ |
| NFR-2.6 | Consistent counts under concurrent enlistment | Exact seat counts with many students at once | Transactional seat consumption (see NFR-1.2). | ✅ |
| NFR-2.7 | **Responsiveness under peak load** | Acceptable response with many concurrent users on enlistment day | The design supports it (paging, background mail, row-version concurrency, rate limits sized for a campus lab behind one address), but **no formal load test has been run yet**. | 🟡 |
| NFR-2.8 | Light client | Fast first load on campus connections | Single bundled Vite build with no CDN dependencies; the PSGC address dataset is bundled, so the SIS makes no external lookups. | ✅ |

---

## 5. NFR-3 Usability (interaction capability)

*Can each user achieve their goal easily, without training?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-3.1 | **Role-differentiated interface** | A user sees only the functions of their role | `features/shell/nav.js` filters the sidebar and the top-bar search by role; the server enforces the same boundaries (NFR-7.2). | ✅ |
| NFR-3.2 | **Learnability** | Functions can be found and understood without training | Every sidebar item carries a one-line description; top-bar search over every page the role may open; `/help` "how SEN-GEN works" guide; the enrollment-stage ticker tells everyone where the term stands. | ✅ |
| NFR-3.3 | **Operability**: consistent controls | A table learned once is every table learned | Shared table controls: click a header to sort ascending / descending / clear, type to filter, choose a page size. Applied across about 25 screens. Grouped views (timetables, curricula, class-section cards) filter and sort but deliberately don't page, so a timetable is never cut in half. | ✅ |
| NFR-3.4 | **User error protection** | Mistakes are prevented or reversible | Inline field validation with plain messages; confirmation dialogs before heavy or outward-facing actions (publish, regenerate over a published schedule, editing a published class); archive instead of delete for requirements, subjects and curricula; Reopen for finalized schedules and evaluations; server refusals name the exact reason. | ✅ |
| NFR-3.5 | Low cognitive load for students | A student knows what to do next | Dashboard journey stepper; enlistment opens with the student's own plan, not the whole catalog; "Show every section" is optional. | ✅ |
| NFR-3.6 | **Mobile-responsive** | Usable on a phone browser | Responsive shell with an off-canvas sidebar; media queries in 16 feature stylesheets; layouts avoid horizontal page scroll. | ✅ |
| NFR-3.7 | User-adjustable display | Users can suit the UI to themselves | `/settings`: light / dark / system theme, comfortable or compact density, text size, accent color, collapsed sidebar. | ✅ |
| NFR-3.8 | Language | Respondents can answer in their own language | The evaluation survey is fully bilingual (English and Filipino). | ✅ |
| NFR-3.9 | **Inclusivity / accessibility** | Usable with keyboard and screen readers (WCAG 2.1 AA as the reference) | Semantic headings and tables, labelled controls, about 190 ARIA attributes (labels, pressed states, roles for alerts and status), focusable buttons rather than clickable divs. **No formal WCAG audit has been run yet.** | 🟡 |
| NFR-3.10 | Helpful errors | An error tells the user what happened and how to report it | Every API error returns a JSON ProblemDetails body with a readable message; unexpected server errors carry a **reference** (trace id) to quote. The client's `ErrorBoundary` contains a crashed screen instead of blanking the app. | ✅ |

---

## 6. NFR-4 Reliability

*Does it keep working, and keep data intact when things go wrong?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-4.1 | **Data integrity** | No partial writes | EF Core unit-of-work: each action and its audit entry commit in one transaction. | ✅ |
| NFR-4.2 | Database-level safeguards | Invalid states are impossible, not just discouraged | CHECK constraints (a section's seat count ≤ its capacity; the institutional seat cap stays within its allowed range), 22 unique indexes (for example one official student number per student, one login account per SIS record, one program head per program), row-version concurrency on sections. Verified by `DatabaseConstraintTests`. | ✅ |
| NFR-4.3 | **Fault tolerance**: email | A mail outage never loses data or rolls back an action | Mail is sent after the action commits; bulk mail goes through the outbox with **retry and exponential backoff** (1, 2, 4, 8 min; up to 5 attempts), then is marked permanently failed and can be retried from `/outbox`. Verified by `OutboxDispatchTests`, `EmailOutboxTests` and `OutboxPathTests`. | ✅ |
| NFR-4.4 | Fault tolerance: live refresh | A broken live channel never fails a save | `ReportsBroadcaster` is fire-and-forget and never throws. | ✅ |
| NFR-4.5 | Contained failures | An unexpected error affects one request, not the app | A global exception handler returns a 500 ProblemDetails with a trace id and logs the full error server-side. | ✅ |
| NFR-4.6 | Safe imports | An .xlsx import never corrupts or duplicates records | Row-by-row validation; duplicates detected by email and by name + birthdate (in-file and against the database); re-importing the same file loads nothing twice. | ✅ |
| NFR-4.7 | Safe concurrent edits | Two staff can't overwrite each other's seat decisions | Optimistic concurrency on approvals and seat-count corrections; schedule edits are guarded (`ScheduleConcurrency`). | ✅ |
| NFR-4.8 | **Recoverability** | Data can be restored after a failure or bad deploy | Database backup before every deploy, weekly, daily during enrollment week and at term end, kept off the server; a restore test before go-live; rollback procedure in `deploy/deploymentplan.md` §8–9. | ✅ (procedure) |
| NFR-4.9 | Health monitoring | Outages are detected before users report them | `/health/live` (process up) and `/health` (readiness, including the database), both anonymous, for an uptime monitor every 5 minutes. | ✅ |
| NFR-4.10 | **Availability** during enrollment | System reachable throughout the enrollment period | Hosted on IIS (SmarterASP.NET) with the monitor above and no deploys during enrollment week. **Uptime under real enrollment traffic hasn't been observed yet.** | 🟡 |

---

## 7. NFR-5 Maintainability

*Can it be understood, changed, tested and extended safely?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-5.1 | **Modularity** | A feature can be built, tested and changed in isolation | Vertical Slice Architecture: one slice per feature under `SENGENSystem.Server/Features/*`, mirrored by `sengensystem.client/src/features/*`. Built feature by feature (FDD). | ✅ |
| NFR-5.2 | **Reusability** | Shared rules live in one place | Single-source policies: `PasswordPolicy`, `YearLevelPolicy`, `EnrollmentCyclePolicy`, `EnlistmentEligibility`, `DocumentChecklist`, `AcademicHistory`, `SisText` (ALL-CAPS normaliser); shared client table controls, confirm dialog and API client. | ✅ |
| NFR-5.3 | **Modifiability**: policy is data | A school policy change is a screen edit, not a redeploy | Requirements catalog, time-slot grid, seat cap, unit ceilings, faculty load limits, engine weights and budgets, class-day start, institution name, lab credit factor and program heads are all editable rows. | ✅ |
| NFR-5.4 | Evolvable schema | Every schema change is tracked and repeatable | **43 EF Core code-first migrations**, applied automatically on start; a SQL script is available for manual migration (`deploy/make-migrate-sql.ps1`). | ✅ |
| NFR-5.5 | **Testability** | Engine and endpoints are covered by automated tests | About **117 test methods** across 35 test files: engine (hard constraints, diagnostics, block granularity, pigeonhole, reproducibility), domain rules, security, outbox, and integration tests against a real SQL Server database (`SengenAppFactory`, `SqlServerTestDatabase`). | ✅ |
| NFR-5.6 | **Analysability** | Problems can be traced quickly | Structured request log line per API call (method, path, status, duration, user, trace id); the audit trail for domain events (64+ action types, stored as stable string names); Swagger/OpenAPI with JWT in development. | ✅ |
| NFR-5.7 | Readable code | New maintainers can follow the reasoning | Each slice opens with a comment stating its requirement ID and why it works the way it does. | ✅ |
| NFR-5.8 | Lint and build checks | Client code stays consistent | `npm run lint` (ESLint) and `npm run build` (Vite); `dotnet build` for the server. | ✅ |

---

## 8. NFR-6 Portability (flexibility)

*Can it run in different environments and adapt to them?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-6.1 | **Installability** | Deployable by school IT staff from a written procedure | `deploy/publish-smarterasp.ps1` builds a single `publish.zip`; Visual Studio publish profiles (Web Deploy, FTP, folder); a step-by-step deployment plan with a go-live checklist and smoke tests. | ✅ |
| NFR-6.2 | Cross-platform runtime | Not tied to one server OS | ASP.NET Core (.NET 10) runs on Windows and Linux; currently targeted at Windows/IIS shared hosting with Microsoft SQL Server. | ✅ |
| NFR-6.3 | **Adaptability**: clients | Works on desktop and mobile browsers | Single-page React app; responsive layout (NFR-3.6); no plug-ins. | ✅ |
| NFR-6.4 | Adaptability: configuration | Environment specifics live in configuration, not code | `appsettings.Production.json` (connection string, JWT, SMTP, seed accounts), with an example file committed and the real one kept out of git. | ✅ |
| NFR-6.5 | No external runtime dependencies | Works without third-party APIs or CDNs | Fonts, icons and the PSGC dataset are bundled; the only outbound service is SMTP email. | ✅ |
| NFR-6.6 | **Replaceability** | Can replace the paper SIS, spreadsheet checklist and manual timetable | All three are reproduced digitally, and the printed outputs match the school's existing forms (NFR-1.7). | ✅ |

---

## 9. NFR-7 Security and data privacy

*Is personal data protected, and can only the right people do the right things?*

### 9.1 Requirements

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-7.1 | **RA 10173 (Data Privacy Act of 2012)** compliance | Personal data handled lawfully, with consent and accountability | Terms and privacy consent captured with timestamps on account and SIS; full terms and privacy pages; least-privilege access; audit trail; no financial data stored. | ✅ |
| NFR-7.2 | **Confidentiality**: access control | Every endpoint checks the role; no cross-role access | JWT bearer authentication and a `RequireRole` policy on every endpoint (or an intentional `AllowAnonymous` for the public SIS, login and survey flows). A student's "own" endpoints resolve from the signed-in account, so there is no id to tamper with. Verified by `AuthorizationSweepTests`, `EndpointGateTests` and `AccountAuthorityTests`. | ✅ |
| NFR-7.3 | Password storage | Never stored in plain text | Salted hashes via ASP.NET Core Identity `PasswordHasher`. | ✅ |
| NFR-7.4 | Password strength | Weak passwords refused | At least **8 characters, with letters and digits**, applied to registration, change, reset and admin reset (`PasswordPolicy`). | ✅ |
| NFR-7.5 | **Brute-force resistance** | Guessing is impractical | Per-account lockout after **5 failed attempts for 15 minutes** (`LoginThrottle`), plus a per-IP limit of **20 requests a minute** on sign-in, 2FA verify/resend and forgot/reset password. Verified by `LoginThrottleTests`. | ✅ |
| NFR-7.6 | Abuse resistance on public forms | Public endpoints can't be used to flood mail | Per-IP limit of **30 a minute** (returns 429) on account registration, the public SIS, term-activation lookup and request, email-change requests and survey submission. | ✅ |
| NFR-7.7 | **Authenticity**: two-factor | Optional second factor for any account | Emailed 6-digit code, valid **10 minutes**, at most **5 attempts**; disabling requires the password. | ✅ |
| NFR-7.8 | Account-takeover resistance | Public flows can't be used to hijack an account | One-time credentials (reset link, email confirmation, 2FA code, survey link) are stored **hashed**, single-use and expiring. Forgot-password answers identically for known and unknown emails (no enumeration). Linking an account to a SIS needs three matching facts, with no auto-link at sign-up. SIS-provisioned passwords are temporary, and a server-side gate blocks everything but the password change until it is replaced. | ✅ |
| NFR-7.9 | Session tokens | Tokens are verifiable and expire | JWT signed with a symmetric key, validating issuer, audience, lifetime and signature; lifetime **480 minutes (8 h)**. Production refuses to start with a missing or weak key (`JwtKeyGuardTests`). | ✅ |
| NFR-7.10 | Transport security | All traffic encrypted | HTTPS redirection; HTTPS required on the host (deployment plan §3.2). | ✅ |
| NFR-7.11 | Browser hardening | The SPA resists clickjacking, MIME sniffing and injected scripts | Security headers on every response: Content-Security-Policy with no external script, style, font or frame sources and `frame-ancestors 'none'`, plus `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, and a `Permissions-Policy` that turns off camera, microphone, geolocation and payment. | ✅ |
| NFR-7.12 | No information leakage | Errors don't reveal internals | Exception details stay in the server log; users see only a message and a reference. | ✅ |
| NFR-7.13 | Export safety | Exported files can't carry attacks | CSV export defuses cells that would run as spreadsheet formulas. | ✅ |
| NFR-7.14 | **Accountability / non-repudiation** | Every data-relevant action is attributable | The audit trail records actor, role, time and summary for 64+ action types, committed in the same transaction as the action. | ✅ |
| NFR-7.15 | **Integrity**: secrets management | Credentials never in source control | Production settings and SMTP password kept out of git (user-secrets in development, a password manager or encrypted storage for production); seeded admin accounts must be secured at first sign-in (deployment plan §7). | ✅ |
| NFR-7.16 | Data minimisation | Store only what enrollment needs | No uploaded documents (papers are recorded, not stored), no grades (only passed / failed / dropped), no tuition or payment data. | ✅ |

### 9.2 Security settings at a glance

| Setting | Value |
|---|---|
| Password rule | ≥ 8 characters, letters + digits |
| Account lockout | 5 failed sign-ins → locked 15 min |
| Sign-in rate limit (login, 2FA, password reset) | 20 per minute per IP address |
| Public form rate limit (registration, SIS, term activation, email change, survey) | 30 per minute per IP address |
| 2FA code | 6 digits, 10-minute expiry, 5 attempts |
| Session (JWT) lifetime | 480 minutes |
| One-time links | Hashed, single-use, expiring |
| First sign-in (SIS accounts) | Temporary password; must be changed before any other screen |

---

## 10. NFR-8 Compatibility

*Can it exchange information and coexist with the school's other systems and tools?*

| ID | Requirement | Target / measure | How SEN-GEN meets it | Status |
|---|---|---|---|---|
| NFR-8.1 | **Co-existence** with the student-records system | SEN-GEN complements, not replaces, the official records | The official student number is recorded from that system; SEN-GEN keeps subject verdicts, not grades; SIS data is stored in ALL CAPS to match it. | ✅ |
| NFR-8.2 | **Interoperability**: spreadsheets | Data moves in and out through Excel | .xlsx import (pre-enrollment, academic history) and export (every report, semester bundle, master data, room and faculty grids) via ClosedXML; survey CSV with a byte-order mark so Filipino text opens correctly in Excel. | ✅ |
| NFR-8.3 | Interoperability: printable documents | Official outputs print the same anywhere | PDF via QuestPDF: faculty loading forms, prospectus, transferee evaluation, certificate of registration. | ✅ |
| NFR-8.4 | Interoperability: email | Notifications reach ordinary mailboxes | Standard SMTP (Gmail app password); HTML email layout. | ✅ |
| NFR-8.5 | Browser coexistence | Runs alongside other tabs and apps without conflict | Plain web app; per-device preferences in local storage only; no plug-ins or installs. | ✅ |
| NFR-8.6 | Local time | Dates read correctly for the campus | Timestamps stored in UTC and shown in Philippine time (`InstitutionClock`). Verified by `InstitutionClockTests`. | ✅ |

---

## 11. Operational requirements

| ID | Requirement | How it is met | Reference |
|---|---|---|---|
| OPS-1 | Hosting | Windows/IIS shared hosting with Microsoft SQL Server (SmarterASP.NET) | `deploy/deploymentplan.md` §2 |
| OPS-2 | Repeatable deploys | One script builds `publish.zip`; `app_offline.htm` during upload; migrations run on start; smoke tests after | §6, §9 |
| OPS-3 | Rollback | Redeploy the previous tagged build; restore the pre-deploy backup if a migration ran | §9 |
| OPS-4 | Backups | Before every deploy, weekly, daily in enrollment week, at term end; stored off the server; restore tested | §8 |
| OPS-5 | Monitoring | Uptime check on `/health` every 5 minutes; error references in logs; audit trail; email outbox page; monthly quota check | §10 |
| OPS-6 | Change windows | No deploys during enrollment week except urgent fixes; evenings or weekends otherwise | §9 |
| OPS-7 | Routine maintenance | Daily, weekly and per-term tasks with named roles | `deploy/maintenanceplan.md` |
| OPS-8 | Term rollover | Activating the next semester is one action; queues follow the active term; institutional settings carry over unchanged | `Functions.md` §11 |

---

## 12. Design constraints

| Constraint | Detail |
|---|---|
| Architecture | Three-tier web application: React (Vite) client, ASP.NET Core Minimal API server, Microsoft SQL Server database |
| Code organisation | Vertical Slice Architecture, built with Feature-Driven Development |
| Scheduling approach | Deterministic, rule-based CSP; **no machine learning or predictive models** |
| Section capacity | Institutional cap of 40 seats per section |
| Scope | Enrollment stages 1–3 only. **No** tuition or payment, multi-campus, government-portal or apply.sti.edu integration |
| Data held | No uploaded files, no grades, no financial data |
| Language of SIS data | Canonical ALL CAPS, matching the paper SIS |

---

## 13. Verification: tests and evaluation

### 13.1 Automated tests (`SENGENSystem.Server.Tests`)

| Area | Test files | What they prove |
|---|---|---|
| Scheduling engine | `CspSchedulerHardConstraintTests`, `CspSchedulerDiagnosticsTests`, `CspSchedulerBlockGranularityTests`, `CspSchedulerPigeonholeTests`, `CspSchedulerReproducibilityTests` | Zero hard-constraint violations, clear failure reasons, correct block sizes, impossible problems detected, same inputs give the same timetable |
| Enrollment rules | `EnlistmentPlannerTests`, `AcademicHistoryTests`, `EnrollmentCompletionTests`, `YearLevelPolicyTests`, `ReminderPolicyTests` | Correct plans, prerequisites, retakes, enrolled status, year levels and reminder targeting |
| Data integrity | `DatabaseConstraintTests`, `ApprovalTests`, `RequestAndBulkTests`, `BulkPreAuthorizationTests`, `RequirementCatalogTests`, `SelfServiceAndQueueTests` | Seat caps, concurrency, bulk actions, requirement scoping, self-service limits, term-scoped queues |
| Security | `AuthorizationSweepTests`, `EndpointGateTests`, `AccountAuthorityTests`, `LoginThrottleTests`, `JwtKeyGuardTests`, `SeedAdminTests`, `SeedPasswordTests` | Role boundaries on every endpoint, lockout, key strength, safe seeding |
| Email | `EmailOutboxTests`, `OutboxDispatchTests`, `OutboxPathTests`, `EmailLayoutTests` | Queueing, retry with backoff, permanent failure, layout |
| Reports and formatting | `ReportFidelityTests`, `ReportSmokeTests`, `FormattingTests`, `InstitutionClockTests`, `SubjectPaletteTests` | Official forms match, every report renders, dates and times read correctly |

About **117 test methods** in total. Integration tests run against a real SQL Server database rather than an in-memory substitute, so constraints and concurrency behave as in production.

### 13.2 User evaluation

The ISO/IEC 25010 survey (§1) is dispatched and collected inside SEN-GEN by the Super Admin, who can choose recipients, send reminders, watch the response goal, read weighted means per characteristic, and export the raw responses to CSV for the study write-up.

---

## 14. Open items

These are the non-functional points not yet fully evidenced. None blocks use of the system, but each should be closed before claiming the requirement in the study.

| # | Item | Why it matters | Suggested action |
|---|---|---|---|
| 1 | **Load test** (NFR-2.7) | Enlistment day is the peak; responsiveness under many concurrent users is designed for but not measured | Simulate a cohort (for example 200–400 concurrent students browsing and requesting seats) against a staging copy and record response times and error rates |
| 2 | **Accessibility audit** (NFR-3.9) | Usability is the headline survey characteristic; keyboard and screen-reader use haven't been formally checked | Run a WCAG 2.1 AA review of the student pages (SIS, enlistment, My subjects) and the main staff queues |
| 3 | **Production availability** (NFR-4.10) | Uptime during a real enrollment period hasn't been observed | Enable the uptime monitor at go-live and report the availability figure for the first enrollment period |
| 4 | **Backup restore test** (NFR-4.8) | A backup that has never been restored doesn't count | Perform the restore test in `deploy/deploymentplan.md` §8 before go-live and record the date |
