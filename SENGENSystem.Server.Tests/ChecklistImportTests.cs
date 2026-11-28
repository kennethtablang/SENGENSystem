using ClosedXML.Excel;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents.Transfer;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The checklist import's reading rules, against the layouts of the admissions office's real
    /// workbook ("Student Admission 2026-27"): the free-text-name "Admission Requirements" sheet and
    /// the per-program sheets with a STUDENT ID and split names.
    /// </summary>
    public class ChecklistImportTests
    {
        private static readonly RequirementTarget[] Catalog =
        [
            new("Form138_SF9", "Form 138 / SF9-SHS (Report Card)"),
            new("Form137_SF10", "Form 137 / SF10-SHS (Permanent Record)"),
            new("GoodMoral", "Certificate of Good Moral Character"),
            new("PsaBirthCertificate", "PSA Birth Certificate"),
            new("OfficialTranscript", "Official Transcript of Records"),
            new("HonorableDismissal", "Certificate of Transfer (Honorable Dismissal)"),
            new("HepaA", "Hepatitis A Vaccination Record"),
            new("HepaB", "Hepatitis B Vaccination Record"),
            new("Xray", "Chest X-ray Result")
        ];

        private static Dictionary<string, string?> Bound(params string[] headers) =>
            ChecklistColumns.Bind(headers, Catalog)
                .ToDictionary(b => b.Header, b => b.Role == ColumnRole.Requirement ? b.RequirementCode : b.Role.ToString());

        [Fact]
        public void Admission_requirements_sheet_headers_bind_to_roles_and_requirements()
        {
            var bound = Bound(
                "CHOSEN PROGRAM / TRACK & STRAND / SPECIALIZATION ", "Student’s Name", "New Student/ Transferee",
                "Program/Course", "Hepa A", "Hepa B", "X-ray",
                "Original Form 138/SF9-SHS (Progress Report Card)", "Original Form 137/SF10-SHS (Permanent Record)",
                "Certificate of Transfer (Honorable Dismissal)", "Official Transcript of Records(Copy for STI)",
                "Good Moral", "PSA-issued Birth Certificate", "Column1");

            Assert.Equal("Program", bound["CHOSEN PROGRAM / TRACK & STRAND / SPECIALIZATION"]);
            Assert.Equal("FullName", bound["Student’s Name"]);
            Assert.Equal("StudentType", bound["New Student/ Transferee"]);
            Assert.Equal("Program", bound["Program/Course"]);
            Assert.Equal("HepaA", bound["Hepa A"]);
            Assert.Equal("HepaB", bound["Hepa B"]);
            Assert.Equal("Xray", bound["X-ray"]);
            Assert.Equal("Form138_SF9", bound["Original Form 138/SF9-SHS (Progress Report Card)"]);
            Assert.Equal("Form137_SF10", bound["Original Form 137/SF10-SHS (Permanent Record)"]);
            Assert.Equal("HonorableDismissal", bound["Certificate of Transfer (Honorable Dismissal)"]);
            Assert.Equal("OfficialTranscript", bound["Official Transcript of Records(Copy for STI)"]);
            Assert.Equal("GoodMoral", bound["Good Moral"]);
            Assert.Equal("PsaBirthCertificate", bound["PSA-issued Birth Certificate"]);
            Assert.Equal("Ignored", bound["Column1"]);
        }

        [Fact]
        public void Program_sheet_headers_bind_including_the_short_forms()
        {
            var bound = Bound(
                "STUDENT ID", "LAST NAME", "FIRST NAME", "MIDDLE NAME", "LEVEL", "UNITS IN PROGRESS",
                "LAST SCHOOL ATTENDED", "HEPA A (HBsAg)", "HEPA B (HBsAb)", "XRAY", "FORM 138", "FORM 137",
                "PSA", "Certificates", "REMARKS", "COURSE");

            Assert.Equal("StudentId", bound["STUDENT ID"]);
            Assert.Equal("LastName", bound["LAST NAME"]);
            Assert.Equal("FirstName", bound["FIRST NAME"]);
            Assert.Equal("MiddleName", bound["MIDDLE NAME"]);
            Assert.Equal("HepaA", bound["HEPA A (HBsAg)"]);
            Assert.Equal("HepaB", bound["HEPA B (HBsAb)"]);
            Assert.Equal("Form138_SF9", bound["FORM 138"]);
            Assert.Equal("Form137_SF10", bound["FORM 137"]);
            Assert.Equal("PsaBirthCertificate", bound["PSA"]);
            Assert.Equal("Program", bound["COURSE"]);
            // Neither "Certificates" (a remarks column) nor "UNITS IN PROGRESS" may be mistaken for anything.
            Assert.Equal("Ignored", bound["Certificates"]);
            Assert.Equal("Ignored", bound["UNITS IN PROGRESS"]);
            Assert.Equal("Ignored", bound["LEVEL"]);
        }

        [Fact]
        public void The_exports_own_headers_read_back_exactly()
        {
            var headers = new[] { "Registration No.", "Student ID", "Last Name", "First Name", "Middle Name", "Program", "Student Type", "Term", "SIS Status" }
                .Concat(Catalog.Select(c => c.Name))
                .Concat(["Papers Received", "Checklist", "Clearance"])
                .ToArray();
            var bound = Bound(headers);

            Assert.Equal("RegistrationNumber", bound["Registration No."]);
            Assert.Equal("StudentId", bound["Student ID"]);
            foreach (var r in Catalog) Assert.Equal(r.Code, bound[r.Name]);
            Assert.Equal("Ignored", bound["Papers Received"]);
            Assert.Equal("Ignored", bound["Clearance"]);
        }

        [Theory]
        [InlineData("✅ Submitted", DocumentStatus.Submitted)]
        [InlineData("              ✅ Submitted", DocumentStatus.Submitted)]
        [InlineData("✅ 9/23/2026", DocumentStatus.Submitted)]
        [InlineData("✅11/17/2026", DocumentStatus.Submitted)]
        [InlineData("Submitted", DocumentStatus.Submitted)]
        [InlineData("COMPLETE", DocumentStatus.Submitted)]
        [InlineData("Xerox Copy", DocumentStatus.XeroxCopy)]
        [InlineData("XEROX  COPY", DocumentStatus.XeroxCopy)]
        [InlineData("XeroxCopy", DocumentStatus.XeroxCopy)]
        [InlineData("copy", DocumentStatus.XeroxCopy)]
        [InlineData("Certified True Copy", DocumentStatus.XeroxCopy)]
        [InlineData("Certificate of Grades", DocumentStatus.CertificateOfGrades)]
        [InlineData("Not Submitted", DocumentStatus.NotSubmitted)]
        [InlineData("no req", DocumentStatus.NotSubmitted)]
        [InlineData("not in file", DocumentStatus.NotSubmitted)]
        public void Cell_values_read_as_statuses(string text, DocumentStatus expected)
        {
            var reading = ChecklistCellReader.Read(new SheetCell(text, false));
            Assert.Equal(CellMeaning.Status, reading.Meaning);
            Assert.Equal(expected, reading.Status);
        }

        [Theory]
        [InlineData("", "Blank")]
        [InlineData("   ", "Blank")]
        [InlineData("N/A", "Blank")]
        [InlineData("None", "Blank")]
        [InlineData("`", "Blank")]
        [InlineData("transferee", "NotApplicable")]
        [InlineData("tranferee", "NotApplicable")]
        [InlineData("NCII COOKERY", "Unrecognised")]
        [InlineData("z.", "Unrecognised")]
        public void Cell_values_that_are_not_statuses_change_nothing(string text, string expected)
        {
            Assert.Equal(expected, ChecklistCellReader.Read(new SheetCell(text, false)).Meaning.ToString());
        }

        [Fact]
        public void A_real_date_cell_reads_as_received()
        {
            var reading = ChecklistCellReader.Read(new SheetCell("9/23/2026", true));
            Assert.Equal(DocumentStatus.Submitted, reading.Status);
        }

        // ---------------- Matching ----------------

        private static MatchCandidate Student(string last, string first, string middle,
            ProgramTrack program = ProgramTrack.ITP, string? officialId = null, StudentType type = StudentType.NewStudent) =>
            new()
            {
                Id = Guid.NewGuid(),
                StudentNumber = $"2026-{Random.Shared.Next(100000, 999999)}",
                OfficialStudentNumber = officialId,
                LastName = last,
                FirstName = first,
                MiddleName = middle,
                Program = program,
                StudentType = type,
                InActiveTerm = true
            };

        private static NameMatch Grade(MatchCandidate c, string cell) =>
            StudentMatcher.Grade(c.Prepare(), StudentMatcher.Tokens(cell));

        [Theory]
        [InlineData("ALFEREZ, JOSHUA SEÑALISTA")]
        [InlineData("ALFEREZ JOSHUA SENALISTA")]
        [InlineData("Alferez, Joshua, Senalista")]
        [InlineData("JOSHUA SEÑALISTA ALFEREZ")]
        [InlineData("ALFEREZ, JOSHUA")]
        [InlineData("ALFEREZ, JOSHUA S.")]
        public void Name_order_punctuation_accents_and_a_missing_middle_still_match_exactly(string cell)
        {
            var joshua = Student("ALFEREZ", "JOSHUA", "SEÑALISTA");
            Assert.Equal(NameMatch.Exact, Grade(joshua, cell));
        }

        [Fact]
        public void A_one_letter_slip_is_only_probable()
        {
            var hazel = Student("ANCHETA", "HAZEL ANNE", "TAMANG");
            Assert.Equal(NameMatch.Probable, Grade(hazel, "ANCHETA HAZEL ANN T."));
        }

        [Fact]
        public void A_missing_second_given_name_is_only_probable()
        {
            var abby = Student("ARIOLA", "ABBY JOY", "RABAGO");
            Assert.Equal(NameMatch.Probable, Grade(abby, "ARIOLA, ABBY, RABAGO"));
        }

        [Theory]
        [InlineData("ALFEREZ, MARK SEÑALISTA")]   // different given name
        [InlineData("JOSHUA SEÑALISTA")]          // no surname
        [InlineData("ALFEREZ, JOSHUA DELA CRUZ")] // a different middle name entirely
        public void A_different_person_does_not_match(string cell)
        {
            var joshua = Student("ALFEREZ", "JOSHUA", "SEÑALISTA");
            Assert.Equal(NameMatch.None, Grade(joshua, cell));
        }

        [Fact]
        public void Student_ids_match_without_their_leading_zero()
        {
            var student = Student("ABARRA", "CRISZEL", "MIRANTE", officialId: "02000494658");
            var matcher = new StudentMatcher([student]);

            Assert.Same(student, matcher.FindByIdentifier("2000494658", out var viaOfficial));
            Assert.True(viaOfficial);
            Assert.Same(student, matcher.FindByIdentifier(" 02000494658 ", out _));
        }

        [Fact]
        public void Two_students_with_the_same_name_are_both_returned_for_the_officer_to_choose()
        {
            var a = Student("RANCHES", "RYA TRESHA", "LOPEZ", ProgramTrack.HRS);
            var b = Student("RANCHES", "RYA TRESHA", "LOPEZ", ProgramTrack.HRA);
            var matcher = new StudentMatcher([a, b, Student("REYES", "JEFFREY", "CARASI")]);

            var found = matcher.FindByName(StudentMatcher.Tokens("RANCHES, RYA TRESHA LOPEZ"));
            Assert.Equal(2, found.Count(f => f.Grade == NameMatch.Exact));
        }

        [Theory]
        [InlineData("HAZEL", "HAZEL", true)]
        [InlineData("ANN", "ANNE", true)]
        [InlineData("PAGUPOD", "PAGUDPOD", true)]
        [InlineData("REYES", "REYNA", false)]
        public void One_edit_apart(string a, string b, bool expected) =>
            Assert.Equal(expected, StudentMatcher.WithinOneEdit(a, b));

        // ---------------- Files ----------------

        [Fact]
        public void A_workbook_finds_its_header_below_a_title_and_reports_hidden_sheets()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("HRS");
            sheet.Cell(1, 1).Value = "STI College Alaminos — HRS requirements";
            sheet.Cell(3, 1).Value = "STUDENT ID";
            sheet.Cell(3, 2).Value = "LAST NAME";
            sheet.Cell(3, 3).Value = "FIRST NAME";
            sheet.Cell(3, 4).Value = "HEPA A";
            sheet.Cell(4, 1).SetValue("02000494658");
            sheet.Cell(4, 2).Value = "ABARRA";
            sheet.Cell(4, 3).Value = "CRISZEL";
            sheet.Cell(4, 4).Value = "✅ 9/23/2026";
            var hidden = workbook.AddWorksheet("ITP_AR");
            hidden.Cell(1, 1).Value = "Student’s Name";
            hidden.Visibility = XLWorksheetVisibility.Hidden;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            var tables = ChecklistWorkbookReader.Read(stream, "admission.xlsx");

            var hrs = tables.Single(t => t.Name == "HRS");
            Assert.True(hrs.IsVisible);
            Assert.Equal(3, hrs.HeaderRowNumber);
            Assert.Single(hrs.Rows);
            Assert.Equal("02000494658", hrs.Rows[0].At(0).Text);
            Assert.False(tables.Single(t => t.Name == "ITP_AR").IsVisible);
        }

        [Fact]
        public void A_csv_with_quotes_semicolons_and_a_bom_reads()
        {
            var csv = "﻿Student ID;Student's Name;Good Moral\r\n"
                + "02000482919;\"ALFEREZ, JOSHUA SEÑALISTA\";✅ Submitted\r\n"
                + "02000490560;\"ANCHETA \"\"HAZEL\"\" ANNE\";Xerox Copy\r\n";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

            var table = ChecklistWorkbookReader.Read(stream, "checklist.csv").Single();

            Assert.Equal(["Student ID", "Student's Name", "Good Moral"], table.Headers);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("ALFEREZ, JOSHUA SEÑALISTA", table.Rows[0].At(1).Text);
            Assert.Equal("ANCHETA \"HAZEL\" ANNE", table.Rows[1].At(1).Text);
        }
        // ---------------- Dates ----------------

        [Theory]
        [InlineData("✅ 9/23/2026", 2026, 9, 23)]
        [InlineData("✅11/17/2026", 2026, 11, 17)]
        [InlineData("Xerox 10/5/2026", 2026, 10, 5)]
        public void A_status_cell_carries_its_date(string text, int y, int m, int d)
        {
            Assert.Equal(new DateOnly(y, m, d), ChecklistCellReader.Read(new SheetCell(text, false)).ReceivedOn);
        }

        [Fact]
        public void A_status_with_no_date_has_none()
        {
            Assert.Null(ChecklistCellReader.Read(new SheetCell("✅ Submitted", false)).ReceivedOn);
        }

        [Theory]
        [InlineData("9/23/2026", 2026, 9, 23)]
        [InlineData("23/9/2026", 2026, 9, 23)]   // a first number over 12 can only be the day
        [InlineData("2026-09-23", 2026, 9, 23)]
        [InlineData("Sep 23, 2026", 2026, 9, 23)]
        [InlineData("9/23/26", 2026, 9, 23)]
        public void Dates_read_the_way_the_office_writes_them(string text, int y, int m, int d)
        {
            Assert.Equal(new DateOnly(y, m, d), ChecklistCellReader.ParseDate(text));
        }

        [Fact]
        public void A_date_received_column_binds_to_its_paper()
        {
            var bound = ChecklistColumns.Bind(
                ["Registration No.", "Form 138 / SF9-SHS (Report Card)", "Form 138 / SF9-SHS (Report Card) — Date received", "HEPA A DATE"],
                Catalog);

            Assert.Equal(ColumnRole.Requirement, bound[1].Role);
            Assert.Equal(ColumnRole.RequirementDate, bound[2].Role);
            Assert.Equal("Form138_SF9", bound[2].RequirementCode);
            Assert.Equal(ColumnRole.RequirementDate, bound[3].Role);
            Assert.Equal("HepaA", bound[3].RequirementCode);
        }
    }
}
