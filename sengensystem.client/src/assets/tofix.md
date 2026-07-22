# SEN-GEN — To Fix & To Improve

A running backlog of known limitations, assumptions, and follow-ups. Grouped by area, with
file references so each item is actionable. Items marked **[decision]** need a product call, not
just code. Full detail for each item is in the sections below.

---

## Quick checklist

**Scheduling engine (CSP)**
- [ ] Time-grid granularity — 2h subjects round up to a 3h block
- [ ] No UI to reproduce a specific arrangement/seed
- [ ] **[decision]** Determinism per-seed vs. absolute
- [ ] Pigeonhole pre-check message counts base slots, not blocks

**Generation error handling**
- [ ] **[decision]** Whether to expose raw exception detail to the Academic Head

**Schedule board**
- [ ] No proactive "Finalized — reopen to edit" banner
- [ ] Fullscreen height doesn't re-fit on window resize
- [ ] Hover tooltip missing on the My schedule view

**Finalize / publish**
- [ ] **[decision]** Publish doesn't require finalization

**Confirmation of Faculty Loading report**
- [ ] Program Head role not modeled (reuses Academic Head)
- [ ] STI lab-credit factor not modeled (Teaching vs Contact hrs)
- [ ] **[decision]** Institution/branch name hardcoded "STI"
- [ ] "Class No." is a proxy (`SectionCode`)

**Grid schedules**
- [ ] Per-faculty schedule grid lacks the instructor detail + colour-coding

**Testing**
- [ ] No automated tests at all (start with the CSP engine)
- [ ] No CI pipeline

**Client architecture (duplication)**
- [ ] `parseError` duplicated in 19 api modules → shared `apiFetch`
- [ ] ProblemDetails `detail`/`reference` parsed only in scheduling api
- [ ] Time/day formatting duplicated (client + server)

**Auth & security**
- [ ] **[decision]** JWT in `localStorage` (XSS exposure)
- [ ] No global 401/expiry handling
- [ ] No login rate-limiting / lockout (brute force)
- [ ] 8-hour token lifetime

**Configuration & secrets**
- [ ] Secrets committed to `appsettings.json` (JWT key, seed admin password, SMTP)
- [ ] Per-environment config baked in

**Data integrity / concurrency**
- [ ] No optimistic concurrency (`RowVersion`) on schedule writes

**Correctness — time zones**
- [ ] `DateTime.Now` vs `DateTime.UtcNow` mixed across ~33 files

**Performance / scalability**
- [ ] In-memory aggregation in report/list endpoints
- [ ] No timeout/cancel on client downloads
- [ ] List endpoints cap (`Take(1000)`) instead of paginating

**Accessibility**
- [ ] `.alert` banners lack `role="alert"` / `aria-live`
- [ ] Verify icon-button labels + modal focus handling

**Web hardening (HTTP headers)**
- [ ] No security response headers (nosniff, frame-ancestors/CSP, Referrer-Policy)

**Observability / ops**
- [ ] No `/health` endpoint
- [ ] Sparse application logging

**Client resilience**
- [ ] No top-level React error boundary

**Operational**
- [ ] Server restart needed after backend changes (deployment note)

---

## Scheduling engine (CSP)

- **Time-grid granularity.** The engine builds class blocks from the 90-minute base grid, so a
  subject whose weekly hours aren't a multiple of 1.5h can't land exactly — e.g. a **2-hour**
  subject rounds **up** to a 3h block (full coverage, never short). Fix by supporting 30/60-min
  base periods, or by synthesizing sub-period blocks.
  `Features/Scheduling/Engine/CspScheduler.cs` (`BuildContiguousBlocks`).
- **Reproduce a specific arrangement.** Generation now varies each run via a random seed and
  returns/audits it, but there's no UI to re-enter a seed and reproduce a past timetable. Add an
  optional "reproduce arrangement #___" field on the Generate page.
  `GenerateSchedulePage.jsx`, `GenerateScheduleEndpoint.cs` (already accepts `Seed`).
- **[decision] Determinism vs. variety.** Output is now deterministic *per seed*, not absolute.
  If the spec requires one fixed timetable for identical inputs, revisit.
- **Pigeonhole pre-check messaging.** The infeasibility pre-check still counts base time slots,
  not blocks, so its "only X time slots" message can understate the real constraint now that a
  subject consumes several consecutive periods. `CspScheduler.cs`.

## Schedule generation — error handling

- **[decision] Exception detail exposure.** On an unexpected 500, the raw exception type/message
  is returned to the Academic Head (with a trace id). Fine for an internal tool; if that's too
  much, show only the trace id and keep the message server-side.
  `GenerateScheduleEndpoint.cs`, global handler in `Program.cs`.

## Schedule board

- **Finalized lock has no proactive banner.** Board edits on a finalized schedule are blocked
  server-side (clear 409), but the board doesn't show a "🔒 Finalized — reopen to edit" banner up
  front. Return the finalized flag from the board GET and surface it.
  `Features/Scheduling/Board/ScheduleBoardEndpoints.cs`, `ScheduleBoardPage.jsx`.
- **Fullscreen height doesn't re-fit on resize.** Calendar height is computed when fullscreen
  toggles; resizing the window while already fullscreen won't re-fit until toggled. Add a resize
  listener. `ScheduleBoardPage.jsx`.
- **Tooltip parity.** The hover tooltip exists on the board but not on the read-only
  **My schedule** view, which uses the same FullCalendar setup. `SchedulePage.jsx`.

## Finalize / publish workflow

- **[decision] Publish doesn't require finalization.** The Registrar can publish a draft that was
  never finalized. If the intended flow is strictly Draft → Finalized → Published, add a guard in
  the publish endpoint. `Features/Publishing/PublishSchedule/PublishScheduleEndpoint.cs`.

## Reports — Confirmation of Faculty Loading

- **Program Head not modeled.** The system has no Program Head role, so the memo's **THRU** and
  **FROM** both use the active Academic Head (Noted = School Admin). Add a Program Head role/field
  if the form needs a distinct signatory.
- **STI lab-credit factor missing.** The official form shows Teaching hrs < Contact hrs for labs
  (per-subject crediting). We only store Units + Hours, so the report shows Units (credited load)
  + Contact hours (meeting duration). Add a per-subject teaching-credit factor for an exact match.
  `Features/Reports/FacultyLoading/FacultyLoadingPdfModels.cs`.
- **[decision] Institution/branch name hardcoded.** The form prints `"STI"`; the template said
  "STI ALAMINOS". Make the institution/branch name configurable in System Parameters.
- **Class No. proxy.** "Class No." maps to `Section.SectionCode` (no real STI class number exists
  in the data).

## Reports — grid schedules

- **Individual (per-faculty) schedule grid** doesn't yet have the instructor detail + subject
  color-coding that the class-program grid now has. Apply the same treatment for consistency.
  `Features/Reports/FacultyLoading/FacultyScheduleGridWorkbook.cs`.

## Cross-cutting / code quality

- **Duplicated subject-color palette.** The hue set + HSL logic lives in both the client
  (`features/scheduling/calendarUtils.js`) and the server
  (`FacultyLoadingReportsEndpoints.cs`, `HueFor`/`HslToHex`). They must be kept in sync by hand;
  consider a single documented source of truth.
- **Client bundle size.** The build warns the main chunk is > 500 kB (FullCalendar, QuestPDF-free
  client, etc.). Introduce route-level code-splitting (dynamic `import()`), especially for the
  scheduling/report pages.
- **Assumption baked into reports:** "No. of students" uses `Section.EnrolledCount`, falling back
  to `Capacity` when enrollment is 0 — confirm this matches how the registrar reads the figure.

## Testing

- **No automated tests exist** (0 test projects / test files in the repo). Highest-value target is
  the CSP engine, which is written to be pure and unit-testable — add xUnit coverage for: hard
  constraints never violated, full weekly-hours block coverage, seeded reproducibility (same seed →
  same timetable), and infeasibility diagnostics. Then smoke tests for report generation (PDF/XLSX
  builders return non-empty, valid files). `Features/Scheduling/Engine/*`, `Features/Reports/*`.
- No CI pipeline to run build + tests on push (add once tests exist).

## Client architecture (duplication)

- **`parseError` is copy-pasted into 19 `api.js` modules** (auth, curriculum, faculty, reports,
  scheduling, users, …), each with its own `fetch` + `Authorization` header wiring. Extract one
  shared `apiFetch(url, opts)` + `parseError` (same way `shell/download.js` was centralised) so
  auth, error shape, and base handling live in one place.
- **ProblemDetails parsing is inconsistent.** Only `scheduling/api.js` reads the `detail` /
  `reference` fields; every other module reads just `message`/`title`, so any endpoint returning
  ProblemDetails (e.g. the new global 500 handler) shows the generic "Something went wrong" there.
  Fold `detail`/`reference` into the shared helper.
- **Time/day formatting duplicated.** `hhmm`/day helpers live in `calendarUtils.js`, but the
  confirmation report + grid re-implement 12-hour and day-abbreviation formatting on the server,
  and `DAY_ABBR`/`DAY_NAMES` are redeclared in more than one client file.

## Auth & security

- **[decision] JWT stored in `localStorage`.** Readable by any injected script — an XSS bug
  becomes full token theft. Consider an httpOnly, SameSite cookie, or short-lived access tokens
  with refresh. `features/auth/api.js`.
- **No global 401/expiry handling.** An expired or invalid token isn't caught centrally — most
  flows just surface an error rather than clearing the token and redirecting to login. Add this to
  the shared fetch wrapper (clear token + redirect on 401).
- **No rate limiting / lockout on login.** Failed logins are audited (`LoginFailed`) but nothing
  throttles or locks an account after repeated failures — the API is open to credential brute
  force. Add ASP.NET rate limiting on `/api/auth/login` and/or a temporary lockout.
  `Features/Auth/Login/*`, `Program.cs`.
- **Long-lived tokens.** `Jwt.ExpiryMinutes` is 480 (8h); combined with `localStorage` storage a
  stolen token stays valid a long time. Shorten access-token lifetime (+ refresh) once storage is
  hardened.

## Configuration & secrets

- **Secrets committed to `appsettings.json`.** The JWT signing `Key` (a placeholder,
  "CHANGE-THIS-DEV-ONLY…"), the seed **admin email + password** (`admin@stialaminos.local` /
  `Admin@Sengen2026`), and the SMTP/from address are in source control. Before any real
  deployment: move these to user-secrets / environment variables / a secrets manager, generate a
  fresh 32+ char JWT key, and force a change of the seeded admin password on first login.
  `SENGENSystem.Server/appsettings.json`.
- **Per-environment config.** `Email.ClientBaseUrl` and the LocalDB connection string are baked in;
  make sure production values come from environment config, not the committed file.

## Data integrity / concurrency

- **No optimistic concurrency on schedule writes.** `ScheduleAssignment` has no `RowVersion`, so two
  admins acting at once (generate vs. finalize vs. board edit vs. publish) can race — regenerate
  deletes drafts and re-inserts while another request reads/writes the same rows (lost updates or
  a partially replaced draft). Add a concurrency token or serialize these operations per semester.
  `Domain/ScheduleAssignment.cs`, `Features/Scheduling/*`, `Features/Publishing/*`.

## Correctness — time zones

- **`DateTime.Now` vs `DateTime.UtcNow` are mixed** across ~33 files. Domain timestamps use UTC,
  but some report/date fields use local `Now`. Standardize on UTC in storage and convert only at
  the display edge (the institution's local time), so audit times, "generated" stamps, and form
  dates are consistent regardless of server locale.

## Performance / scalability

- **In-memory aggregation.** Several list/report endpoints `ToListAsync()` and then group/filter in
  memory (`BuildRowsAsync`, grid workbook, soft-constraints). Correct and fine at institutional
  scale, but push grouping/sums into SQL if data volumes grow.
  `Features/Reports/FacultyLoading/FacultyLoadingReportsEndpoints.cs`, `Features/Scheduling/SoftConstraints/*`.
- **No request timeout/cancellation on client downloads.** A slow bulk export (.zip, consolidated
  workbook) can hang the button indefinitely; add an `AbortController` timeout + a cancel affordance.
- **List endpoints cap rather than paginate.** `ListUsers` hard-caps at `.Take(1000)` and silently
  drops the rest; the audit trail is bounded but there's no page navigation. Large lists (users,
  registrations, faculty loading) need real paging (skip/take + total count + UI controls).
  `Features/UserManagement/ListUsers/ListUsersEndpoint.cs`, `Features/Registration/*`.

## Accessibility (audit needed)

- Error/success banners (`.alert`) are plain `<div>`s without `role="alert"` / `aria-live`, so
  screen readers may miss them. Add live-region roles.
- Verify icon-only buttons (search, fullscreen, row actions) all have `aria-label`/`title`, and
  that modals trap focus and restore it on close.

## Web hardening (HTTP headers)

- **No security response headers.** Only HTTPS redirect is configured — there's no
  `X-Content-Type-Options: nosniff`, no `X-Frame-Options` / CSP `frame-ancestors` (clickjacking
  protection), no `Referrer-Policy`, and no `Content-Security-Policy` for the served SPA. Add a
  headers middleware. `Program.cs`.

## Observability / ops

- **No health/readiness endpoint.** There's no `/health` for uptime monitoring or
  container/orchestrator liveness/readiness probes. Add `AddHealthChecks()` (including a DB check).
  `Program.cs`.
- **Sparse application logging.** Only schedule generation and the global exception handler emit
  logs; most requests and failures aren't logged for operations (the audit trail covers *domain*
  events, not operational diagnostics). Add structured request logging + warning-level logs on the
  notable failure paths, and consider metrics.

## Client resilience

- **No React error boundary.** A render-time exception in any feature blanks the entire SPA (white
  screen) with nothing logged. Add a top-level `ErrorBoundary` that renders a recoverable fallback
  and reports the error. `src/App.jsx`.

## Operational

- **Server restart required** after backend changes (migrations apply automatically at startup via
  `MigrateAsync`). Not a code fix — a deployment note.
