using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.FacultyLoading
{
    /// <summary>
    /// The Confirmation of Faculty Loading form (FR-RPT-02, FR-FAC-04): one memo per faculty
    /// member — a TO/THRU/FROM/DATE header, the assignment table (code, description, class no.,
    /// type, section, days, time, room, units, contact hours, students) with running totals, and
    /// Conforme/Noted signature blocks. Modelled on STI's official form so a printed page can be
    /// signed and filed as-is.
    /// </summary>
    internal sealed class FacultyLoadingPdfDocument(
        Semester semester,
        List<FacultyLoadReportDto> reports,
        FacultyLoadingSignatories signatories,
        DateTime generatedAt) : IDocument
    {
        private static readonly Color Ink = Color.FromHex("#111111");
        private static readonly Color Muted = Color.FromHex("#5b6c99");
        private static readonly Color Brand = Color.FromHex("#003399");
        private static readonly Color Line = Color.FromHex("#8fa3cf");
        private static readonly Color HeaderFill = Color.FromHex("#dfe7f7");
        private static readonly Color AlertInk = Color.FromHex("#b02a4a");

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Confirmation of Faculty Loading — {semester.Name}",
            Author = "SEN-GEN",
            Subject = "Faculty academic load and schedule"
        };

        public void Compose(IDocumentContainer container)
        {
            if (reports.Count == 0)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(36);
                    page.Content().PaddingTop(40).Text($"No faculty records exist for {semester.Name}.")
                        .FontSize(11).FontColor(Muted);
                });
                return;
            }

            // One memo per member keeps each confirmation filable on its own.
            foreach (var report in reports)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(36);
                    page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink).FontFamily(Fonts.Calibri));

                    page.Footer().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text($"Generated {generatedAt:dd MMM yyyy HH:mm} · SEN-GEN")
                            .FontSize(7.5f).FontColor(Muted);
                        row.ConstantItem(120).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                            t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);
                        col.Item().Element(Title);
                        col.Item().Element(e => Memo(e, report));
                        col.Item().PaddingTop(2).Text("Please be informed that you are assigned the following:")
                            .FontSize(9.5f);
                        col.Item().Element(e => Table(e, report));
                        col.Item().PaddingTop(6)
                            .Text("Please acknowledge acceptance by affixing your signature on the space provided below.")
                            .FontSize(9);
                        col.Item().PaddingTop(10).Element(e => Signatures(e, report));
                    });
                });
            }
        }

        private void Title(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text("CONFIRMATION OF FACULTY LOADING")
                    .FontSize(14).Bold().FontColor(Brand).LetterSpacing(0.03f);
                col.Item().AlignCenter().Text($"{signatories.Institution} · {semester.Name}")
                    .FontSize(9.5f).FontColor(Muted);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Brand);
            });
        }

        /// <summary>The TO / THRU / FROM / DATE routing block.</summary>
        private void Memo(IContainer container, FacultyLoadReportDto r)
        {
            container.Column(col =>
            {
                col.Spacing(4);
                col.Item().Element(e => MemoRow(e, "TO", r.Name, r.EmployeeId == "—" ? null : $"Employee ID {r.EmployeeId}"));
                col.Item().Element(e => MemoRow(e, "THRU", signatories.ProgramHead, "Program Head"));
                col.Item().Element(e => MemoRow(e, "FROM", signatories.AcademicHead, "Academic Head"));
                col.Item().Element(e => MemoRow(e, "DATE", generatedAt.ToString("dd MMMM yyyy"), null));
            });
        }

        private void MemoRow(IContainer container, string label, string value, string? subtitle)
        {
            container.Row(row =>
            {
                row.ConstantItem(58).Text($"{label} :").FontSize(9.5f).Bold().FontColor(Muted);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(string.IsNullOrWhiteSpace(value) ? "________________________" : value)
                        .FontSize(9.5f).Bold();
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        c.Item().Text(subtitle).FontSize(7.5f).FontColor(Muted);
                });
            });
        }

        private void Table(IContainer container, FacultyLoadReportDto r)
        {
            if (r.Lines.Count == 0)
            {
                container.Border(1).BorderColor(Line).Padding(14).AlignCenter()
                    .Text("No teaching load allocated for this semester.")
                    .FontSize(9.5f).FontColor(Muted);
                return;
            }

            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(58);   // Code
                    c.RelativeColumn(3);    // Description
                    c.ConstantColumn(48);   // Class No.
                    c.ConstantColumn(34);   // Type
                    c.ConstantColumn(66);   // Section
                    c.ConstantColumn(40);   // Days
                    c.ConstantColumn(96);   // Time
                    c.ConstantColumn(64);   // Room
                    c.ConstantColumn(38);   // Units
                    c.ConstantColumn(48);   // Contact hrs
                    c.ConstantColumn(44);   // Students
                });

                table.Header(header =>
                {
                    Head(header.Cell(), "CODE");
                    Head(header.Cell(), "DESCRIPTION");
                    Head(header.Cell(), "CLASS NO.");
                    Head(header.Cell(), "TYPE");
                    Head(header.Cell(), "SECTION");
                    Head(header.Cell(), "DAYS");
                    Head(header.Cell(), "TIME");
                    Head(header.Cell(), "ROOM");
                    Head(header.Cell(), "UNITS", true);
                    Head(header.Cell(), "CONTACT HRS", true);
                    Head(header.Cell(), "NO. OF STUDENTS", true);
                });

                foreach (var line in r.Lines)
                {
                    Body(table.Cell(), line.ShowSubjectInfo ? line.SubjectCode : "").Bold();
                    Body(table.Cell(), line.ShowSubjectInfo ? line.SubjectTitle : "");
                    Body(table.Cell(), line.ShowSubjectInfo ? line.ClassNo : "");
                    Body(table.Cell(), line.Type);
                    Body(table.Cell(), line.Section);

                    if (line.IsUnscheduled)
                    {
                        table.Cell().ColumnSpan(3).BorderBottom(1).BorderColor(Line)
                            .PaddingVertical(3).PaddingHorizontal(4)
                            .Text("⚠ Not yet scheduled").FontSize(8).Bold().FontColor(AlertInk);
                    }
                    else
                    {
                        Body(table.Cell(), line.Day);
                        Body(table.Cell(), line.Time);
                        Body(table.Cell(), line.Room);
                    }

                    Body(table.Cell(), line.ShowSubjectInfo && line.Units > 0 ? line.Units.ToString() : "", true);
                    Body(table.Cell(), line.IsUnscheduled ? "" : Num(line.ContactHours), true);
                    Body(table.Cell(), line.ShowSubjectInfo && line.StudentCount > 0 ? line.StudentCount.ToString() : "", true);
                }

                table.Footer(footer =>
                {
                    footer.Cell().ColumnSpan(8).Background(HeaderFill).BorderTop(1).BorderColor(Line)
                        .PaddingVertical(4).PaddingHorizontal(5).AlignRight()
                        .Text("TOTAL").FontSize(9).Bold();
                    footer.Cell().Background(HeaderFill).BorderTop(1).BorderColor(Line)
                        .PaddingVertical(4).PaddingHorizontal(5).AlignRight()
                        .Text(r.TotalUnits.ToString()).FontSize(9).Bold();
                    footer.Cell().Background(HeaderFill).BorderTop(1).BorderColor(Line)
                        .PaddingVertical(4).PaddingHorizontal(5).AlignRight()
                        .Text(Num(r.TotalContactHours)).FontSize(9).Bold();
                    footer.Cell().Background(HeaderFill).BorderTop(1).BorderColor(Line);
                });
            });
        }

        private void Head(IContainer container, string text, bool alignRight = false)
        {
            var cell = container.Background(Brand).PaddingVertical(4).PaddingHorizontal(4);
            (alignRight ? cell.AlignRight() : cell)
                .Text(text).FontSize(7.5f).Bold().FontColor(Color.FromHex("#ffffff"));
        }

        private TextSpanDescriptor Body(IContainer container, string text, bool alignRight = false)
        {
            var cell = container.BorderBottom(1).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(4);
            return (alignRight ? cell.AlignRight() : cell).Text(text).FontSize(8);
        }

        /// <summary>Conforme (the faculty member) and Noted (the School Administrator) blocks.</summary>
        private void Signatures(IContainer container, FacultyLoadReportDto r)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(e => SignBlock(e, "Conforme:", r.Name, null));
                row.ConstantItem(40);
                row.RelativeItem().Element(e => SignBlock(e, "Noted:", signatories.SchoolAdmin, "School Administrator"));
            });
        }

        private void SignBlock(IContainer container, string label, string name, string? role)
        {
            container.Column(col =>
            {
                col.Item().Text(label).FontSize(9).Bold();
                col.Item().PaddingTop(24).LineHorizontal(1).LineColor(Ink);
                col.Item().Text(string.IsNullOrWhiteSpace(name) ? "Signature over printed name" : name)
                    .FontSize(9.5f).Bold();
                col.Item().Text(role ?? "Signature over printed name & date").FontSize(7.5f).FontColor(Muted);
            });
        }

        private static string Num(double value) =>
            value == Math.Floor(value) ? value.ToString("0") : value.ToString("0.0");
    }
}
