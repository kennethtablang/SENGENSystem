"""
Generates the SEN-GEN user seeder for SSMS:

    deploy/seed-users.sql               run in SSMS (safe to commit: holds password HASHES only)
    deploy/seed-users-credentials.csv   who signs in with what (git-ignored: plain passwords)

Accounts:
    - one each: School Admin, Academic Head, Registrar, Admission Officer, Faculty member
      (no Super Admin: that one is created by the site's first start)
    - 50 students, each with a CONFIRMED Student Information Sheet for the active semester, a
      student number, and the admission-requirements checklist their program asks for

Every account gets its own random temporary password and must change it at first sign-in.
SQL Server can't compute ASP.NET Core Identity password hashes, so this script computes them in
the exact format the app's PasswordHasher<User> verifies (v3: PBKDF2-HMAC-SHA512, 100,000
iterations, 16-byte salt, 32-byte subkey).

Usage (from the repo root):
    python deploy/make-users-seeder.py                      # emails @stialaminos.local
    python deploy/make-users-seeder.py --domain school.edu.ph

Re-running makes NEW passwords: run the new seed-users.sql only on a database that has not had the
old one, or the accounts already there keep their old passwords (the script skips existing emails).
"""
import argparse
import base64
import csv
import hashlib
import os
import random
import secrets
import string
import struct

HERE = os.path.dirname(os.path.abspath(__file__))

# ---- Password hashing (ASP.NET Core Identity PasswordHasher v3) --------------------------------

def identity_v3_hash(password: str) -> str:
    salt = secrets.token_bytes(16)
    iterations = 100_000
    subkey = hashlib.pbkdf2_hmac('sha512', password.encode('utf-8'), salt, iterations, 32)
    # 0x01 marker | PRF (2 = HMACSHA512) | iteration count | salt length | salt | subkey, big-endian
    blob = b'\x01' + struct.pack('>III', 2, iterations, len(salt)) + salt + subkey
    return base64.b64encode(blob).decode('ascii')


def temp_password() -> str:
    # The app's policy: 8+ characters with letters and digits. 12 characters, no look-alikes.
    letters = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz'
    digits = '23456789'
    body = [secrets.choice(letters) for _ in range(8)] + [secrets.choice(digits) for _ in range(3)]
    secrets.SystemRandom().shuffle(body)
    return 'S' + ''.join(body)


# ---- Fictional people ---------------------------------------------------------------------------

FIRST_M = ['JUAN', 'MIGUEL', 'JOSE', 'CARLO', 'PAOLO', 'RAFAEL', 'MARK', 'JOHN', 'KENNETH', 'JEROME',
           'ANGELO', 'CHRISTIAN', 'JUSTIN', 'RENZ', 'NATHAN', 'ADRIAN', 'GABRIEL', 'JOSHUA', 'DANIEL',
           'LORENZO', 'BRYAN', 'IVAN', 'EMMANUEL', 'RYAN', 'VINCENT']
FIRST_F = ['MARIA', 'ANGELICA', 'KRISTINE', 'JASMINE', 'PRINCESS', 'NICOLE', 'CAMILLE', 'ANDREA',
           'BEA', 'JOANNA', 'PATRICIA', 'SOPHIA', 'CLARISSE', 'DANICA', 'HANNAH', 'ELLA', 'JULIA',
           'RICA', 'TRISHA', 'MIKAELA', 'ALYSSA', 'LIEZEL', 'SHAINA', 'ROWENA', 'JOY']
LAST = ['DELA CRUZ', 'SANTOS', 'REYES', 'GARCIA', 'MENDOZA', 'RAMOS', 'CASTILLO', 'VILLANUEVA',
        'FERNANDEZ', 'AQUINO', 'BAUTISTA', 'NAVARRO', 'TORRES', 'DE GUZMAN', 'PASCUAL', 'SORIANO',
        'CABRERA', 'MERCADO', 'SALVADOR', 'ROSARIO', 'CRUZ', 'GONZALES', 'LOPEZ', 'DOMINGO', 'ESTRADA',
        'MAGBANUA', 'ABALOS', 'CARINO', 'ESPINO', 'FLORES', 'MACARAEG', 'ROMERO', 'VALDEZ']
MIDDLE = ['ABAD', 'BELEN', 'CORPUZ', 'DIAZ', 'EBUEN', 'FRIAS', 'GALANG', 'HERNANDEZ', 'IBARRA',
          'JIMENEZ', 'LACSON', 'MANALO', 'NUNEZ', 'OCAMPO', 'PADILLA', 'QUIAMBAO', 'RIVERA', 'SISON']
# Barangays of Alaminos City, Pangasinan.
BARANGAYS = ['POBLACION', 'BOLANEY', 'LUCAP', 'PANGAPISAN', 'TELBANG', 'PALAMIS', 'BUED', 'PANDAN',
             'AMANDIEGO', 'TANAYTAY', 'BALANGOBONG', 'LINMANSANGAN', 'MAGSAYSAY', 'SAN JOSE',
             'POCALPOCAL', 'TANGCARANG']
SHS_STRANDS = ['ICT', 'HUMSS', 'ABM', 'GAS', 'TVL - HOME ECONOMICS', 'STEM']
SHS_SCHOOLS = ['ALAMINOS CITY NATIONAL HIGH SCHOOL', 'BOLANEY NATIONAL HIGH SCHOOL',
               'LUCAP INTEGRATED SCHOOL', 'PANGAPISAN NATIONAL HIGH SCHOOL', 'TELBANG NATIONAL HIGH SCHOOL']

# Program -> (year level -> how many students). 50 in all.
COHORTS = [('ITP', 1, 10), ('ITP', 2, 10), ('HRA', 1, 8), ('HRA', 2, 7), ('HRS', 1, 8), ('HRS', 2, 7)]

STAFF = [
    # role, first, last, email local part
    ('SchoolAdmin', 'School', 'Administrator', 'schooladmin'),
    ('AcademicHead', 'Academic', 'Head', 'academichead'),
    ('Registrar', 'Office', 'Registrar', 'registrar'),
    ('AdmissionOfficer', 'Admission', 'Officer', 'admissionofficer'),
    ('FacultyMember', 'Faculty', 'Member', 'faculty'),
]


def sql_str(value):
    return 'NULL' if value is None else "N'" + str(value).replace("'", "''") + "'"


def proper(name: str) -> str:
    return ' '.join(w.capitalize() for w in name.split(' '))


def email_part(name: str) -> str:
    return ''.join(c for c in name.lower() if c in string.ascii_lowercase)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--domain', default='stialaminos.local', help='email domain for every account')
    args = ap.parse_args()
    domain = args.domain.strip().lstrip('@').lower()

    rng = random.Random(20270111)   # fixed: the same people every run (passwords still differ)
    used_names = set()
    students = []
    seq = 0
    for program, year, count in COHORTS:
        for _ in range(count):
            seq += 1
            while True:
                female = rng.random() < 0.5
                first = rng.choice(FIRST_F if female else FIRST_M)
                last = rng.choice(LAST)
                if (first, last) not in used_names:
                    used_names.add((first, last))
                    break
            middle = rng.choice(MIDDLE)
            birth_year = 2007 if year == 1 else 2006
            dob = f'{birth_year}-{rng.randint(1, 12):02d}-{rng.randint(1, 28):02d}'
            barangay = rng.choice(BARANGAYS)
            father_first = rng.choice(FIRST_M)
            mother_first = rng.choice(FIRST_F)
            mother_maiden = rng.choice(MIDDLE)
            local = f'{email_part(first)}.{email_part(last)}{seq:02d}'
            students.append({
                'seq': seq, 'program': program, 'year': year,
                'first': first, 'last': last, 'middle': middle,
                'gender': 'Female' if female else 'Male', 'dob': dob,
                'barangay': barangay,
                'address': f'{rng.randint(1, 250)} {rng.choice(["RIZAL ST.", "MABINI ST.", "QUEZON AVE.", "BONIFACIO ST.", "MARCOS AVE.", "PUROK 3", "PUROK 5"])}',
                'mobile': f'0900{seq:07d}',
                'father': f'{father_first} {last}', 'father_mobile': f'0901{seq:07d}',
                'mother': f'{mother_first} {mother_maiden} {last}', 'mother_mobile': f'0902{seq:07d}',
                'email': f'{local}@{domain}',
                'password': temp_password(),
                # Year 1: straight from senior high. Year 2: continuing from their first year here.
                'last_level': 'SeniorHighSchool' if year == 1 else 'College',
                'school': rng.choice(SHS_SCHOOLS) if year == 1 else 'STI COLLEGE ALAMINOS',
                'school_program': rng.choice(SHS_STRANDS) if year == 1 else program,
                'school_year': '2025-2026',
                'grade': 'Grade12' if year == 1 else 'FirstYear',
            })

    staff = [{'role': r, 'first': f, 'last': l, 'email': f'{e}@{domain}', 'password': temp_password()}
             for r, f, l, e in STAFF]

    # ---- credentials CSV (git-ignored) ----
    csv_path = os.path.join(HERE, 'seed-users-credentials.csv')
    with open(csv_path, 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(['Role', 'Name', 'Email', 'Temporary password', 'Program', 'Year level'])
        for s in staff:
            w.writerow([s['role'], f"{s['first']} {s['last']}", s['email'], s['password'], '', ''])
        for s in students:
            w.writerow(['Student', f"{proper(s['first'])} {proper(s['last'])}", s['email'], s['password'], s['program'], s['year']])

    # ---- SQL ----
    staff_rows = ',\n'.join(
        f"    ({sql_str(s['role'])}, {sql_str(s['first'])}, {sql_str(s['last'])}, {sql_str(s['email'])}, {sql_str(identity_v3_hash(s['password']))})"
        for s in staff)
    student_rows = ',\n'.join(
        '    (' + ', '.join([
            str(s['seq']), sql_str(s['program']), str(s['year']),
            sql_str(s['last']), sql_str(s['first']), sql_str(s['middle']),
            sql_str(proper(s['first'])), sql_str(proper(s['last'])),
            sql_str(s['gender']), sql_str(s['dob']),
            sql_str(s['address']), sql_str(s['barangay']),
            sql_str(s['mobile']), sql_str(s['email']),
            sql_str(s['father']), sql_str(s['father_mobile']),
            sql_str(s['mother']), sql_str(s['mother_mobile']),
            sql_str(s['last_level']), sql_str(s['school']), sql_str(s['school_program']),
            sql_str(s['school_year']), sql_str(s['grade']),
            sql_str(identity_v3_hash(s['password']))
        ]) + ')'
        for s in students)

    sql = TEMPLATE.replace('{{DOMAIN}}', domain) \
                  .replace('{{STAFF_ROWS}}', staff_rows) \
                  .replace('{{STUDENT_ROWS}}', student_rows) \
                  .replace('{{STUDENT_COUNT}}', str(len(students)))
    sql_path = os.path.join(HERE, 'seed-users.sql')
    with open(sql_path, 'w', newline='\r\n', encoding='utf-8') as f:
        f.write(sql)

    print(f'Wrote {sql_path}')
    print(f'Wrote {csv_path}  (plain passwords: keep it private, it is git-ignored)')
    print(f'{len(staff)} staff + {len(students)} students, emails @{domain}')


TEMPLATE = r"""/* =====================================================================================
   SEN-GEN - user accounts seeder
   GENERATED by deploy/make-users-seeder.py - edit the generator, not this file.
   -------------------------------------------------------------------------------------
   Creates (emails @{{DOMAIN}}):
     - School Admin, Academic Head, Registrar, Admission Officer: one account each
     - Faculty member: one account + the faculty profile the scheduling side needs
     - {{STUDENT_COUNT}} students: a login, a CONFIRMED Student Information Sheet for the ACTIVE
       semester, a student number, and their program's admission-requirements checklist
       (ITP 10 + 10, HRA 8 + 7, HRS 8 + 7 across Year 1 + Year 2)
   No Super Admin: the site's first start creates that one.

   Passwords: every account has its own temporary password, listed in
   deploy/seed-users-credentials.csv (NOT in git). Everyone must change it at first sign-in.

   RUN deploy/seeder.sql FIRST: students are attached to the active semester it sets up.

   HOW TO RUN (SSMS 2022, see deploy/deploymentplan.md section 3.3 / Step 2):
     back up the database, select db_ad16f0_sengendb in the toolbar dropdown, press F5.

   SAFE TO RE-RUN: accounts are matched by email and skipped when they exist. One transaction:
   on any error nothing is changed. Rows inserted here do not appear in the Audit trail.
   ===================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* ---- Option ------------------------------------------------------------------------------- */
DECLARE @PreAuthorize bit = 0;   -- 1 = students may pick subjects right away (skips the Admission
                                 --     Officer's pre-authorization step); 0 = they wait for it
/* -------------------------------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.StudentRegistrations') IS NULL
    THROW 50000, N'SEN-GEN tables not found. Select the SEN-GEN database in the toolbar dropdown.', 1;

DECLARE @SemesterId uniqueidentifier, @SemesterStart date;
SELECT TOP 1 @SemesterId = Id, @SemesterStart = StartDate FROM dbo.Semesters WHERE IsActive = 1;
IF @SemesterId IS NULL
    THROW 50001, N'No active semester. Run deploy/seeder.sql first (it creates and activates AY 2026-2027 Second Semester).', 1;

DECLARE @Now datetime2 = SYSUTCDATETIME();

BEGIN TRANSACTION;

/* =====================================================================================
   1. STAFF  (Role, FirstName, LastName, Email, PasswordHash)
   ===================================================================================== */
DECLARE @Staff TABLE (Role nvarchar(30), FirstName nvarchar(100), LastName nvarchar(100),
                      Email nvarchar(256), PasswordHash nvarchar(max));
INSERT INTO @Staff VALUES
{{STAFF_ROWS}};

INSERT INTO dbo.Users (Id, Email, FirstName, LastName, PasswordHash, Role, IsActive, MustChangePassword,
                       FailedLoginCount, TwoFactorAttempts, TwoFactorEnabled, CreatedAtUtc)
SELECT NEWID(), s.Email, s.FirstName, s.LastName, s.PasswordHash, s.Role, 1, 1, 0, 0, 0, @Now
FROM @Staff s
WHERE NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Email = s.Email);

-- The faculty member's scheduling profile: without it they can't be given a teaching load.
INSERT INTO dbo.FacultyProfiles (Id, UserId, ProgramCode, MaxLoadUnits, EmployeeId)
SELECT NEWID(), u.Id, N'ITP', 24, N'STI-F001'
FROM dbo.Users u
WHERE u.Email = (SELECT Email FROM @Staff WHERE Role = N'FacultyMember')
  AND NOT EXISTS (SELECT 1 FROM dbo.FacultyProfiles f WHERE f.UserId = u.Id);

/* =====================================================================================
   2. STUDENTS
   ===================================================================================== */
DECLARE @Students TABLE (
    Seq int, Program nvarchar(10), YearLevel int,
    LastName nvarchar(100), FirstName nvarchar(100), MiddleName nvarchar(100),
    UserFirst nvarchar(100), UserLast nvarchar(100),
    Gender nvarchar(10), DateOfBirth date,
    AddressLine nvarchar(200), Barangay nvarchar(160),
    MobileNumber nvarchar(20), Email nvarchar(256),
    FatherName nvarchar(160), FatherMobile nvarchar(20),
    MotherName nvarchar(160), MotherMobile nvarchar(20),
    LastSchoolLevel nvarchar(30), SchoolName nvarchar(200), SchoolProgram nvarchar(200),
    SchoolYear nvarchar(20), YearGradeLastAttended nvarchar(20),
    PasswordHash nvarchar(max),
    UserId uniqueidentifier NULL, RegistrationId uniqueidentifier NULL, StudentNumber nvarchar(20) NULL);

INSERT INTO @Students (Seq, Program, YearLevel, LastName, FirstName, MiddleName, UserFirst, UserLast,
    Gender, DateOfBirth, AddressLine, Barangay, MobileNumber, Email, FatherName, FatherMobile,
    MotherName, MotherMobile, LastSchoolLevel, SchoolName, SchoolProgram, SchoolYear,
    YearGradeLastAttended, PasswordHash)
VALUES
{{STUDENT_ROWS}};

-- Only students whose email is free, both as a login and as an SIS email.
DELETE s FROM @Students s
WHERE EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Email = s.Email)
   OR EXISTS (SELECT 1 FROM dbo.StudentRegistrations r WHERE r.Email = s.Email);

-- Student numbers continue after the highest one already issued for the semester's year, the
-- same rule the app uses, so the next online registration doesn't collide with these.
DECLARE @Prefix nvarchar(5) = CAST(YEAR(@SemesterStart) AS nvarchar(4)) + N'-';
DECLARE @LastSeq int = ISNULL((
    SELECT MAX(TRY_CAST(SUBSTRING(StudentNumber, 6, 10) AS int))
    FROM dbo.StudentRegistrations WHERE StudentNumber LIKE @Prefix + N'%'), 0);

WITH numbered AS (SELECT *, ROW_NUMBER() OVER (ORDER BY Seq) AS n FROM @Students)
UPDATE numbered
SET UserId = NEWID(), RegistrationId = NEWID(),
    StudentNumber = @Prefix + RIGHT(N'000000' + CAST(@LastSeq + n AS nvarchar(10)), 6);

INSERT INTO dbo.Users (Id, Email, FirstName, LastName, PasswordHash, Role, IsActive, MustChangePassword,
                       FailedLoginCount, TwoFactorAttempts, TwoFactorEnabled, CreatedAtUtc)
SELECT UserId, Email, UserFirst, UserLast, PasswordHash, N'Student', 1, 1, 0, 0, 0, @Now
FROM @Students;

INSERT INTO dbo.StudentRegistrations (
    Id, StudentNumber, SemesterId, UserId, Status, StudentType, Program, YearLevel,
    LastName, FirstName, MiddleName, Gender, CivilStatus, Citizenship, DateOfBirth, Birthplace,
    Email, MobileNumber, AddressLine, Barangay, CityMunicipality, Province, ZipCode,
    FatherName, FatherMobile, MotherName, MotherMobile,
    GuardianName, GuardianMobile, GuardianRelationship,
    LastSchoolLevel, SchoolName, SchoolProgram, SchoolYear, YearGradeLastAttended, LastTerm,
    IsPreAuthorized, PreAuthorizedAtUtc, CreatedAtUtc)
SELECT
    RegistrationId, StudentNumber, @SemesterId, UserId, N'Confirmed', N'NewStudent', Program, YearLevel,
    LastName, FirstName, MiddleName, Gender, N'Single', N'FILIPINO', DateOfBirth, N'ALAMINOS CITY, PANGASINAN',
    Email, MobileNumber, AddressLine, Barangay, N'CITY OF ALAMINOS', N'PANGASINAN', N'2404',
    FatherName, FatherMobile, MotherName, MotherMobile,
    MotherName, MotherMobile, N'Mother',
    LastSchoolLevel, SchoolName, SchoolProgram, SchoolYear, YearGradeLastAttended, N'Second',
    @PreAuthorize, CASE WHEN @PreAuthorize = 1 THEN @Now END, @Now
FROM @Students;

-- Admission-requirements checklist: one row per active requirement that applies to the
-- student's program and to new students (what the app seeds on registration), all NotSubmitted.
INSERT INTO dbo.RegistrationDocuments (Id, StudentRegistrationId, RequirementCode, Status)
SELECT NEWID(), s.RegistrationId, r.Code, N'NotSubmitted'
FROM @Students s
JOIN dbo.AdmissionRequirementPrograms p ON p.Program = s.Program
JOIN dbo.AdmissionRequirements r ON r.Id = p.AdmissionRequirementId
WHERE r.IsActive = 1 AND r.AppliesToNewStudents = 1;

COMMIT TRANSACTION;

/* =====================================================================================
   RESULT
   ===================================================================================== */
SELECT u.Role, COUNT(*) AS Accounts
FROM dbo.Users u
WHERE u.Email LIKE N'%@{{DOMAIN}}'
GROUP BY u.Role ORDER BY u.Role;

SELECT r.Program, r.YearLevel, COUNT(*) AS Students, MIN(r.StudentNumber) AS FirstNumber,
       MAX(r.StudentNumber) AS LastNumber,
       (SELECT COUNT(*) FROM dbo.RegistrationDocuments d
        JOIN dbo.StudentRegistrations x ON x.Id = d.StudentRegistrationId
        WHERE x.Program = r.Program AND x.YearLevel = r.YearLevel AND x.Email LIKE N'%@{{DOMAIN}}') AS ChecklistRows
FROM dbo.StudentRegistrations r
WHERE r.Email LIKE N'%@{{DOMAIN}}'
GROUP BY r.Program, r.YearLevel
ORDER BY r.Program, r.YearLevel;
"""

if __name__ == '__main__':
    main()
