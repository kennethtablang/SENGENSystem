/* =====================================================================================
   SEN-GEN - first school data seeder (AY 2026-2027, Second Semester)
   -------------------------------------------------------------------------------------
   Inserts the first-hand academic setup into the LIVE database:
     1. Buildings      Old Building, New Building
     2. Rooms          10 rooms, each typed as a lecture room or a laboratory
     3. School year    AY 2026-2027 (active)
     4. Semesters      First Semester (inactive, for recording 1st-sem results later)
                       Second Semester, Jan 11 - Jun 12, 2027 (ACTIVE, Preparation stage)
     5. Curricula      ITP, HRA, HRS linked to AY 2026-2027 (reuses the ones the app seeded)
     6. Subjects       Sample subjects, 3 units each, Year 1 and Year 2, both terms
     7. Class sections ITP, HRA, HRS: Year 1 and Year 2, sections A and B (Second Semester)

   HOW TO RUN (SSMS 2022) - see deploy/deploymentplan.md section 3.3 and Step 2:
     - Connect to sql1003.site4now.net with SQL Server Authentication,
       Encryption = Mandatory, user db_ad16f0_sengendb_admin.
     - Pick db_ad16f0_sengendb in the toolbar database dropdown.
     - TAKE A BACKUP FIRST (deploymentplan.md section 8).
     - Open this file and press F5.

   SAFE TO RE-RUN: every row is matched by its natural key (building name, room name,
   school-year name, semester term, curriculum program, subject code, section) and only
   inserted when missing, so running it twice creates no duplicates. The whole script runs in
   one transaction: if anything fails, nothing is changed.

   Rows inserted here do NOT appear in the Audit trail (the app writes audit entries, SQL does
   not). Everything can be edited afterwards in the app's Academic setup pages.
   ===================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any error rolls the whole script back

/* ---- Edit these before running if the real dates differ ------------------------------ */
DECLARE @SchoolYearName nvarchar(60) = N'AY 2026-2027';
DECLARE @Sem1Start      date = '2026-08-03';   -- ASSUMED first-semester start: change to the real date
DECLARE @Sem1End        date = '2026-12-19';   -- ASSUMED first-semester end:   change to the real date
DECLARE @Sem2Start      date = '2027-01-11';   -- Second Semester classes start (given)
DECLARE @Sem2End        date = '2027-06-12';   -- Second Semester classes end   (given)
DECLARE @DefaultRoomCapacity int = 40;         -- = the section seat cap; adjust per room later in Rooms
/* -------------------------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.Subjects') IS NULL OR OBJECT_ID(N'dbo.ClassSections') IS NULL
    THROW 50000, N'SEN-GEN tables not found. Select the SEN-GEN database in the toolbar dropdown (and make sure the site has started once or migrate.sql has been run).', 1;

DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @Dash nvarchar(1) = NCHAR(8212);   -- the em dash the app uses in semester names

BEGIN TRANSACTION;

/* =====================================================================================
   1. BUILDINGS
   ===================================================================================== */
DECLARE @Buildings TABLE (Name nvarchar(120), Code nvarchar(20));
INSERT INTO @Buildings VALUES (N'Old Building', N'OB'), (N'New Building', N'NB');

INSERT INTO dbo.Buildings (Id, Name, Code)
SELECT NEWID(), b.Name, b.Code
FROM @Buildings b
WHERE NOT EXISTS (SELECT 1 FROM dbo.Buildings x WHERE x.Name = b.Name);

UPDATE x SET Code = b.Code
FROM dbo.Buildings x JOIN @Buildings b ON b.Name = x.Name
WHERE x.Code IS NULL;

DECLARE @OldBuilding uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Buildings WHERE Name = N'Old Building');
DECLARE @NewBuilding uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Buildings WHERE Name = N'New Building');

/* =====================================================================================
   2. ROOMS
   Room kinds the system knows: LectureRoom, ComputerLaboratory, KitchenLaboratory.
   "Lecture + Lab" is not a room type: it belongs to a SUBJECT, which is then scheduled as two
   meetings (the lecture part in a lecture room, the lab part in a lab of the right kind).
   The hospitality labs (F&B, Bar Lab, Kitchen Lab, Suite Room) are all KitchenLaboratory,
   the only hospitality lab type the scheduler has, so HRA/HRS lab meetings can use any of
   them and lectures never take them.
   ===================================================================================== */
DECLARE @Rooms TABLE (Name nvarchar(60), Building nvarchar(20), Kind nvarchar(30));
INSERT INTO @Rooms VALUES
    (N'Computer Laboratory', N'Old', N'ComputerLaboratory'),   -- "Comlab"
    (N'Room 201',            N'Old', N'LectureRoom'),
    (N'Room 303',            N'Old', N'LectureRoom'),
    (N'Room 401',            N'Old', N'LectureRoom'),
    (N'Room 402',            N'Old', N'LectureRoom'),
    (N'F&B Laboratory',      N'New', N'KitchenLaboratory'),
    (N'Bar Laboratory',      N'New', N'KitchenLaboratory'),
    (N'Kitchen Laboratory',  N'New', N'KitchenLaboratory'),
    (N'Room 301',            N'New', N'LectureRoom'),
    (N'Suite Room',          N'New', N'KitchenLaboratory');

-- Existing rows (for example a "Kitchen Laboratory" the app created on start) are moved to the
-- right building and kind; their capacity is left alone so edits made in the app survive.
UPDATE x
SET BuildingId = CASE r.Building WHEN N'Old' THEN @OldBuilding ELSE @NewBuilding END,
    Kind = r.Kind
FROM dbo.Rooms x JOIN @Rooms r ON r.Name = x.Name;

INSERT INTO dbo.Rooms (Id, Name, Capacity, Kind, BuildingId)
SELECT NEWID(), r.Name, @DefaultRoomCapacity, r.Kind,
       CASE r.Building WHEN N'Old' THEN @OldBuilding ELSE @NewBuilding END
FROM @Rooms r
WHERE NOT EXISTS (SELECT 1 FROM dbo.Rooms x WHERE x.Name = r.Name);

/* =====================================================================================
   3. SCHOOL YEAR  (single active)
   ===================================================================================== */
IF NOT EXISTS (SELECT 1 FROM dbo.SchoolYears WHERE Name = @SchoolYearName)
    INSERT INTO dbo.SchoolYears (Id, Name, IsActive, StartDate, EndDate)
    VALUES (NEWID(), @SchoolYearName, 0, @Sem1Start, @Sem2End);
ELSE
    UPDATE dbo.SchoolYears SET EndDate = @Sem2End WHERE Name = @SchoolYearName;

DECLARE @SchoolYear uniqueidentifier = (SELECT TOP 1 Id FROM dbo.SchoolYears WHERE Name = @SchoolYearName);
UPDATE dbo.SchoolYears SET IsActive = CASE WHEN Id = @SchoolYear THEN 1 ELSE 0 END;

/* =====================================================================================
   4. SEMESTERS  (one per term per school year; single active)
   ===================================================================================== */
-- First Semester: inactive. It ran before SEN-GEN; it exists so 1st-sem results (passed /
-- failed / dropped) can be recorded against it in Academic records. Never overwritten.
IF NOT EXISTS (SELECT 1 FROM dbo.Semesters WHERE SchoolYearId = @SchoolYear AND Term = N'FirstSemester')
    INSERT INTO dbo.Semesters (Id, Name, Term, IsActive, EnrollmentStage, IsArchived, ArchivedAtUtc, StartDate, EndDate, SchoolYearId)
    VALUES (NEWID(), @SchoolYearName + N' ' + @Dash + N' First Semester', N'FirstSemester', 0,
            N'Preparation', 0, NULL, @Sem1Start, @Sem1End, @SchoolYear);

-- Second Semester: the term being set up. Starts in Preparation (students can't register yet).
IF NOT EXISTS (SELECT 1 FROM dbo.Semesters WHERE SchoolYearId = @SchoolYear AND Term = N'SecondSemester')
    INSERT INTO dbo.Semesters (Id, Name, Term, IsActive, EnrollmentStage, IsArchived, ArchivedAtUtc, StartDate, EndDate, SchoolYearId)
    VALUES (NEWID(), @SchoolYearName + N' ' + @Dash + N' Second Semester', N'SecondSemester', 0,
            N'Preparation', 0, NULL, @Sem2Start, @Sem2End, @SchoolYear);
ELSE
    UPDATE dbo.Semesters SET StartDate = @Sem2Start, EndDate = @Sem2End
    WHERE SchoolYearId = @SchoolYear AND Term = N'SecondSemester';

DECLARE @Sem2 uniqueidentifier =
    (SELECT TOP 1 Id FROM dbo.Semesters WHERE SchoolYearId = @SchoolYear AND Term = N'SecondSemester');
UPDATE dbo.Semesters SET IsActive = CASE WHEN Id = @Sem2 THEN 1 ELSE 0 END;

/* =====================================================================================
   5. CURRICULA  (reuse the program's active curriculum; create it only if missing)
   ===================================================================================== */
DECLARE @Programs TABLE (Code nvarchar(20), Name nvarchar(160), CurriculumId uniqueidentifier NULL);
INSERT INTO @Programs (Code, Name) VALUES
    (N'ITP', N'2-yr Information Technology Program'),
    (N'HRA', N'3-yr Hotel and Restaurant Administration'),
    (N'HRS', N'2-yr Hospitality and Restaurant Services');

INSERT INTO dbo.Curricula (Id, ProgramCode, ProgramName, IsActive, IsArchived, ArchivedAtUtc, ArchiveReason)
SELECT NEWID(), p.Code, p.Name, 1, 0, NULL, NULL
FROM @Programs p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Curricula c WHERE c.ProgramCode = p.Code AND c.IsArchived = 0);

UPDATE p SET CurriculumId = (
    SELECT TOP 1 c.Id FROM dbo.Curricula c
    WHERE c.ProgramCode = p.Code AND c.IsArchived = 0
    ORDER BY c.IsActive DESC)
FROM @Programs p;

-- Make the chosen curriculum the program's active one, and effective for AY 2026-2027.
UPDATE c SET IsActive = 1 FROM dbo.Curricula c JOIN @Programs p ON p.CurriculumId = c.Id;

INSERT INTO dbo.CurriculumSchoolYears (Id, CurriculumId, SchoolYearId)
SELECT NEWID(), p.CurriculumId, @SchoolYear
FROM @Programs p
WHERE NOT EXISTS (SELECT 1 FROM dbo.CurriculumSchoolYears x
                  WHERE x.CurriculumId = p.CurriculumId AND x.SchoolYearId = @SchoolYear);

/* =====================================================================================
   6. SUBJECTS  (SAMPLE list - replace with the official prospectus when available)
   All 3 units. Delivery: LectureOnly (3 lec hrs), LectureLaboratory (2 lec + 3 lab hrs),
   LaboratoryOnly (3 lab hrs). No prerequisites, so no student is blocked at enlistment
   before first-semester results are recorded.
   Codes the app already seeded (ITP101, HRA103, ...) are updated in place to these values.
   ===================================================================================== */
DECLARE @Subjects TABLE (
    Program nvarchar(20), Code nvarchar(20), Title nvarchar(160), YearLevel int, Term nvarchar(20),
    Delivery nvarchar(30), Lec int, Lab int, LabKind nvarchar(30) NULL);

INSERT INTO @Subjects VALUES
-- ITP - Year 1
    (N'ITP', N'ITP101', N'Computer Programming 1',                  1, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP102', N'Web Design and Development',              1, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP111', N'Purposive Communication',                 1, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'ITP', N'ITP103', N'Database Management Systems',             1, N'SecondSemester', N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP104', N'Computer Programming 2',                  1, N'SecondSemester', N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP112', N'Mathematics in the Modern World',         1, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
-- ITP - Year 2
    (N'ITP', N'ITP201', N'Networking 1',                            2, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP203', N'Object-Oriented Programming',             2, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP211', N'Science, Technology and Society',         2, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'ITP', N'ITP202', N'Systems Analysis and Design',             2, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
    (N'ITP', N'ITP204', N'Networking 2',                            2, N'SecondSemester', N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
    (N'ITP', N'ITP205', N'Mobile Application Development',          2, N'SecondSemester', N'LectureLaboratory', 2, 3, N'ComputerLaboratory'),
-- HRA - Year 1
    (N'HRA', N'HRA101', N'Fundamentals of Food Service Operations', 1, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA102', N'Introduction to the Hospitality Industry',1, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRA', N'HRA111', N'Purposive Communication',                 1, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRA', N'HRA103', N'Culinary Arts 1',                         1, N'SecondSemester', N'LaboratoryOnly',    0, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA104', N'Kitchen Essentials and Basic Food Preparation', 1, N'SecondSemester', N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA112', N'Mathematics in the Modern World',         1, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
-- HRA - Year 2
    (N'HRA', N'HRA201', N'Food and Beverage Service',               2, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA203', N'Housekeeping Operations',                 2, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA211', N'Science, Technology and Society',         2, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRA', N'HRA202', N'Front Office Management',                 2, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
    (N'HRA', N'HRA204', N'Bar and Beverage Management',             2, N'SecondSemester', N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRA', N'HRA205', N'Tourism and Hospitality Marketing',       2, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
-- HRS - Year 1
    (N'HRS', N'HRS101', N'Basic Food Preparation',                  1, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRS', N'HRS102', N'Housekeeping Services',                   1, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRS', N'HRS111', N'Purposive Communication',                 1, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRS', N'HRS103', N'Bartending and Beverage Service',         1, N'SecondSemester', N'LaboratoryOnly',    0, 3, N'KitchenLaboratory'),
    (N'HRS', N'HRS104', N'Food and Beverage Services',              1, N'SecondSemester', N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRS', N'HRS112', N'Mathematics in the Modern World',         1, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
-- HRS - Year 2
    (N'HRS', N'HRS201', N'Front Office Services',                   2, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRS', N'HRS203', N'Commercial Cooking',                      2, N'FirstSemester',  N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRS', N'HRS211', N'Science, Technology and Society',         2, N'FirstSemester',  N'LectureOnly',       3, 0, NULL),
    (N'HRS', N'HRS202', N'Events Management Services',              2, N'SecondSemester', N'LectureOnly',       3, 0, NULL),
    (N'HRS', N'HRS204', N'Bread and Pastry Production',             2, N'SecondSemester', N'LectureLaboratory', 2, 3, N'KitchenLaboratory'),
    (N'HRS', N'HRS205', N'Customer Service Excellence',             2, N'SecondSemester', N'LectureOnly',       3, 0, NULL);

UPDATE x
SET Title = s.Title, Units = 3, YearLevel = s.YearLevel, Term = s.Term, ProgramCode = s.Program,
    Delivery = s.Delivery, LectureHours = s.Lec, LaboratoryHours = s.Lab, LabRoomKind = s.LabKind,
    Hours = s.Lec + s.Lab
FROM dbo.Subjects x
JOIN @Programs p ON p.CurriculumId = x.CurriculumId
JOIN @Subjects s ON s.Program = p.Code AND s.Code = x.Code;

INSERT INTO dbo.Subjects (Id, Code, Title, Units, ProgramCode, YearLevel, Term, Delivery,
                          LectureHours, LaboratoryHours, LabRoomKind, Hours, CurriculumId,
                          IsArchived, ArchivedAtUtc, ArchiveReason)
SELECT NEWID(), s.Code, s.Title, 3, s.Program, s.YearLevel, s.Term, s.Delivery,
       s.Lec, s.Lab, s.LabKind, s.Lec + s.Lab, p.CurriculumId,
       0, NULL, NULL
FROM @Subjects s
JOIN @Programs p ON p.Code = s.Program
WHERE NOT EXISTS (SELECT 1 FROM dbo.Subjects x WHERE x.CurriculumId = p.CurriculumId AND x.Code = s.Code);

/* =====================================================================================
   7. CLASS SECTIONS  (Second Semester; shown in the app as "ITP 1-A")
   ===================================================================================== */
DECLARE @Sections TABLE (Program nvarchar(20), YearLevel int, SectionName nvarchar(20));
INSERT INTO @Sections VALUES
    (N'ITP', 1, N'A'), (N'ITP', 1, N'B'), (N'ITP', 2, N'A'), (N'ITP', 2, N'B'),
    (N'HRA', 1, N'A'), (N'HRA', 1, N'B'), (N'HRA', 2, N'A'), (N'HRA', 2, N'B'),
    (N'HRS', 1, N'A'), (N'HRS', 1, N'B'), (N'HRS', 2, N'A'), (N'HRS', 2, N'B');

INSERT INTO dbo.ClassSections (Id, SemesterId, ProgramCode, YearLevel, SectionName, CurriculumId, CreatedAtUtc)
SELECT NEWID(), @Sem2, s.Program, s.YearLevel, s.SectionName, p.CurriculumId, @Now
FROM @Sections s
JOIN @Programs p ON p.Code = s.Program
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClassSections x
                  WHERE x.SemesterId = @Sem2 AND x.ProgramCode = s.Program
                    AND x.YearLevel = s.YearLevel AND x.SectionName = s.SectionName);

COMMIT TRANSACTION;

/* =====================================================================================
   RESULT - what is now in the database
   ===================================================================================== */
SELECT b.Name AS Building, r.Name AS Room, r.Kind, r.Capacity
FROM dbo.Rooms r LEFT JOIN dbo.Buildings b ON b.Id = r.BuildingId
ORDER BY b.Name, r.Name;

SELECT y.Name AS SchoolYear, y.IsActive AS YearActive, s.Name AS Semester, s.StartDate, s.EndDate,
       s.IsActive AS SemesterActive, s.EnrollmentStage
FROM dbo.Semesters s JOIN dbo.SchoolYears y ON y.Id = s.SchoolYearId
ORDER BY s.StartDate;

SELECT c.ProgramCode, s.YearLevel, s.Term, s.Code, s.Title, s.Units, s.Delivery,
       s.LectureHours, s.LaboratoryHours, s.LabRoomKind
FROM dbo.Subjects s JOIN dbo.Curricula c ON c.Id = s.CurriculumId
WHERE c.ProgramCode IN (N'ITP', N'HRA', N'HRS') AND s.IsArchived = 0
ORDER BY c.ProgramCode, s.YearLevel, s.Term, s.Code;

SELECT cs.ProgramCode + N' ' + CAST(cs.YearLevel AS nvarchar(2)) + N'-' + cs.SectionName AS Section,
       sem.Name AS Semester
FROM dbo.ClassSections cs JOIN dbo.Semesters sem ON sem.Id = cs.SemesterId
ORDER BY cs.ProgramCode, cs.YearLevel, cs.SectionName;
