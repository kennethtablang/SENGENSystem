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

### FR-AUTH — Authentication & User Management

| ID | Requirement |
|----|-------------|
| FR-AUTH-01 | The system shall allow students to register an account (self-service) with name, email address, and password. |
| FR-AUTH-02 | Registration shall require explicit **terms-and-conditions acknowledgment**, and the acknowledgment timestamp shall be persisted (addresses documented apply.sti.edu gap). |
| FR-AUTH-03 | The system shall **enforce proper name capitalization** on registration input (addresses documented apply.sti.edu gap). |
| FR-AUTH-04 | The system shall **validate all registration inputs** (required fields, email format, password strength) and present clear validation prompts for incomplete/invalid entries (addresses documented apply.sti.edu gap). |
| FR-AUTH-05 | The system shall authenticate users via email + password login and issue a session token (JWT) carrying the user's role. |
| FR-AUTH-06 | Passwords shall be stored only as salted cryptographic hashes — never in plain text (RA 10173 Data Privacy Act). |
| FR-AUTH-07 | The School Admin shall be able to create, update, deactivate, and assign roles to user accounts for all six roles. |
| FR-AUTH-08 | Every API endpoint shall be guarded by role-based authorization middleware; users must never access functions or data outside their role. |
| FR-AUTH-09 | Duplicate account registration (same email) shall be rejected with a clear message. |

### FR-DOC — Document Submission & Requirements Checklist

| ID | Requirement |
|----|-------------|
| FR-DOC-01 | The system shall maintain a **digital requirements checklist** per new enrollee covering pertinent papers: **Form 137, birth certificate, good moral certificate** (extensible list). |
| FR-DOC-02 | The Admission Officer shall be able to record, verify, and update the submission status of each required document per student. |
| FR-DOC-03 | The system shall display each enrollee's completion status (complete / incomplete, per-document state) as an auditable record. |
| FR-DOC-04 | The system shall report document submission completion rates on the administrative dashboard. |
| FR-DOC-05 | The system shall send automated **document submission reminder** emails for incomplete checklists. |

### FR-SIS — Digital Student Information Sheet (Registration)

| ID | Requirement |
|----|-------------|
| FR-SIS-01 | The system shall provide a **fully digital SIS registration module** replacing the paper SIS form, with a structured data-entry workflow for personal and academic details. |
| FR-SIS-02 | SIS submission shall require terms-and-conditions acknowledgment with a captured timestamp. |
| FR-SIS-03 | SIS fields shall enforce formatting rules (proper name capitalization, required-field validation, completeness validation) before acceptance. |
| FR-SIS-04 | The Registrar shall manage (view, correct, confirm) registration data captured through the SIS module. |
| FR-SIS-05 | Registration confirmation shall trigger an automated email notification to the student. |

### FR-PRE — Pre-Enrollment (ETL Import)

| ID | Requirement |
|----|-------------|
| FR-PRE-01 | The Registrar shall be able to **import prospective student lists from `.xlsx` files** via an ETL pipeline: **Extract** from .xlsx → **Validate** field completeness and format consistency → **Transform** to target schema → **Load** with duplicate detection. |
| FR-PRE-02 | Imported students shall be **pre-authorized** for online subject slot selection only after completing document submission and SIS registration. |
| FR-PRE-03 | Import errors (missing fields, format issues, duplicates) shall be reported row-by-row without aborting valid rows. |
| FR-PRE-04 | The Admission Officer shall be able to pre-authorize incoming and returning students. |

### FR-SCHED — CSP-Based Generative Scheduling Engine

| ID | Requirement |
|----|-------------|
| FR-SCHED-01 | The system shall automatically generate **conflict-free class schedules** by modeling sections, rooms, time slots, and faculty as CSP variables assigned values that satisfy all hard constraints. |
| FR-SCHED-02 | **Hard constraints** (must never be violated): no room double-booking; no faculty double-assignment; no time-slot overlap within a student section; room capacity respected; faculty academic load limits respected. |
| FR-SCHED-03 | **Soft constraints** (optimized, not mandatory): faculty time-slot preferences; minimized idle periods between consecutive classes; **balanced faculty load distribution consistent with STI's institutional loading guidelines**. |
| FR-SCHED-04 | The engine shall be **curriculum-aware**: section assignments must respect prerequisite and co-requisite structures per program so that fixed-curriculum cohorts have feasible schedules. |
| FR-SCHED-05 | Engine inputs: subject records, section configurations, faculty profiles and availability, room records (capacity/attributes), academic calendar, and system parameters (unit-load limits, allowable time slots, section capacities). |
| FR-SCHED-06 | The Academic Head shall trigger schedule generation and review generated schedules before publishing. |
| FR-SCHED-07 | Schedule generation shall complete within operationally practical time for STI Alaminos-scale datasets (incremental constraint evaluation and early-termination heuristics; target well under 30 seconds for typical datasets). |
| FR-SCHED-08 | The engine is **deterministic, rule-based AI** — no machine learning / predictive models (explicit design exclusion). |

### FR-FAC — Faculty Assignment & Load Management

| ID | Requirement |
|----|-------------|
| FR-FAC-01 | The system shall support assignment of faculty members to subject loads. |
| FR-FAC-02 | The system shall provide **manual class scheduling override** capability (adjust algorithmically generated assignments) while maintaining automatic conflict detection on every override. |
| FR-FAC-03 | Faculty load shall be validated against applicable regulatory/institutional limits at assignment time. |
| FR-FAC-04 | The system shall continuously **monitor faculty load distribution** so the Academic Head can verify balance without manual cross-referencing, and shall surface imbalances/overloading. |
| FR-FAC-05 | Faculty members shall have a digital interface to view their finalized assigned schedules and section enrollment counts. |

### FR-ENL — Student Subject Enlistment

| ID | Requirement |
|----|-------------|
| FR-ENL-01 | Students shall browse **published** class schedules showing subjects, sections, times, rooms, and faculty. |
| FR-ENL-02 | The interface shall display **real-time slot availability** per section. |
| FR-ENL-03 | Each class section shall be capped at a **maximum of 40 slots**, enforced at the system/database level at the moment of enlistment — requests beyond capacity are automatically rejected. |
| FR-ENL-04 | Slot selection shall be routed through an **approval workflow** (student requests a seat → Registrar approves), with slot-approval email confirmation. |
| FR-ENL-05 | Only **pre-authorized, registered** students (document submission + SIS registration complete) may enlist. |
| FR-ENL-06 | Enlistment shall be available online 24/7 from any internet-connected device during the enlistment window — no in-person visit required. |
| FR-ENL-07 | The system shall prevent a student from enlisting in sections with overlapping time slots. |

### FR-PUB — Schedule Publishing & Distribution

| ID | Requirement |
|----|-------------|
| FR-PUB-01 | The Registrar shall publish finalized, constraint-verified schedules **before the enrollment period opens**. |
| FR-PUB-02 | Finalized schedules shall be distributable to students and faculty **by week, by day, and by class**. |
| FR-PUB-03 | Schedule publication shall trigger automated email notifications to affected students and faculty. |

### FR-DASH — Semester-Aware Administrative Dashboard

| ID | Requirement |
|----|-------------|
| FR-DASH-01 | The dashboard shall automatically filter all displayed metrics to the **active (or selected) semester**. |
| FR-DASH-02 | The dashboard shall show real-time: enrollment and enlistment statistics (counts by section), room utilization analysis, faculty academic load reports, document submission completion rates, and pre-enrollment application volume. |
| FR-DASH-03 | The dashboard shall expose the constraints/preferences that influenced scheduling assignment decisions (scheduling transparency). |

### FR-NOTIF — Automated Email Notifications

| ID | Requirement |
|----|-------------|
| FR-NOTIF-01 | The system shall dispatch automated emails for at least these lifecycle events: **document submission reminders, registration confirmations, slot approvals, schedule publication / schedule milestones**. |
| FR-NOTIF-02 | Notification dispatches shall be logged (see FR-AUD). |

### FR-RPT — Reports & Analytics

| ID | Requirement |
|----|-------------|
| FR-RPT-01 | The system shall produce: validated registration reports, enlistment results, faculty load summaries, room utilization reports, and document checklist completion reports. |
| FR-RPT-02 | Reports shall be semester-scoped and exportable for institutional planning. |

### FR-AUD — Audit Trail

| ID | Requirement |
|----|-------------|
| FR-AUD-01 | The system shall keep accountability logs (audit trail entries) for security- and data-relevant actions: registrations, checklist changes, slot requests/approvals, schedule generation/overrides/publication, user management actions, and notification dispatches. |

---

## 4. Non-Functional Requirements (ISO/IEC 25010:2023)

The system will be evaluated by 45 purposively sampled respondents (30 students, 10 faculty, 5 administrative staff) using a 5-point Likert ISO 25010 questionnaire across six dimensions. **Target: overall weighted mean ≥ 4.00 ("Very Good")** — the empirically established threshold for sustainable adoption.

### NFR-1 Functional Suitability
- Generated schedules must have **zero hard-constraint violations** (100% hard-constraint satisfaction; benchmark systems fulfill ~78% of soft constraints).
- Automated outputs (schedules, enrollment confirmations, capacity counts) must be accurate.

### NFR-2 Performance Efficiency
- Schedule generation completes in practical time for STI Alaminos-scale data (dozens of sections; benchmark: comparable engines flagged when generation exceeded 30 s).
- The system remains responsive under concurrent multi-user access during peak enrollment periods.
- Real-time capacity counts must be consistent under concurrent enlistment (transactional enforcement of the 40-slot cap).

### NFR-3 Usability
- Role-differentiated interfaces tailored to each user type's functional needs.
- Student-facing modules optimized for minimal cognitive load and intuitive navigation.
- Mobile-responsive web interface (accessible via web and mobile browsers).

### NFR-4 Reliability
- Data integrity through transactional database operations.
- Reliable availability during peak enrollment periods (IIS-hosted application layer).
- ETL import must not corrupt or duplicate records (duplicate detection on load).

### NFR-5 Maintainability
- Modular, component-based architecture: ASP.NET Core dependency injection; React component hierarchy; **vertical slices per feature** so features can be built, tested, and accepted independently (FDD).
- EF Core code-first migrations keep schema consistent and evolvable.

### NFR-6 Portability
- Cross-platform runtime (ASP.NET Core) and component-based front-end (React) working across diverse device and browser configurations.

### NFR-7 Security & Compliance (cross-cutting)
- **RA 10173 (Data Privacy Act of 2012)**: secure handling of student and faculty personal data; passwords hashed; least-privilege RBAC; audit trail.
- Authentication middleware at the application layer; role-based middleware validation on every request.
- No financial data is stored in the system (out of scope).

---

## 5. Data Requirements (core entities)

Derived from the paper's data inputs and document analysis (SIS, Room Utilization Report, Class Scheduling Grid/Matrix, Confirmation of Faculty Loading, Student Master List, Registration records, Subjects & Units records):

- **User** (all six roles; credentials, role, status)
- **Student profile / SIS registration record** (with T&C acknowledgment timestamp)
- **Document requirement + submission status** (checklist per student)
- **Subject / Course** (units, program, prerequisites/co-requisites)
- **Section** (subject, capacity ≤ 40, semester)
- **Faculty profile** (load limits, availability/time preferences)
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

1. **Foundation**: user authentication, RBAC, SQL Server schema ← *current iteration (Register/Login)*
2. Core CSP scheduling engine
3. Student-facing enlistment interface
4. Administrative dashboard and notification modules

---

## 8. Acceptance & Evaluation Criteria

- Each feature is documented with acceptance criteria derived from stakeholder interviews and ISO 25010 dimensions, and is validated/accepted by stakeholders (FDD Phase 5).
- Unit tests are written for all scheduling engine functions and API endpoints prior to integration.
- Post-deployment: ISO 25010 questionnaire (content-validated by a panel of 3 IT experts) administered to the 45 respondents; scores computed by average weighted mean and interpreted: 4.50–5.00 Excellent, 3.50–4.49 Very Good, 2.50–3.49 Good, 1.50–2.49 Fair, 1.00–1.49 Poor.
- Primary success benchmark: **overall weighted mean ≥ 4.00**.
