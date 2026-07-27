using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SENGENSystem.Server.Features.Reports.Prospectus
{
    /// <summary>One enrolled class on the student's registration form.</summary>
    internal sealed record EnrolledClassRow(
        string Code,
        string Title,
        int Units,
        string SectionCode,
        string Days,
        string Time,
        string Room,
        string Faculty,
        bool IsAmended);

    /// <summary>
    /// The student's Certificate of Registration (FR-RPT-05): the subjects they actually hold a
    /// seat in this term, with the schedule for each. Printed from approved enlistment against the
    /// published timetable, so it is the same information the student sees in My schedule — in the
    /// form they can hand to a cashier, a guardian, or a scholarship office.
    /// <para>
    /// A class amended after publication is flagged, so a printed copy carries the same warning the
    /// screen does rather than quietly showing a time that was already moved once.
    /// </para>
    /// </summary>
    internal sealed class RegistrationFormPdfDocument(
        string studentName,
        string studentNumber,
        string programLabel,
        string yearLevelLabel,
        string studentTypeLabel,
        string semesterName,
        List<EnrolledClassRow> rows,
        DateTime generatedAt) : IDocument
    {
        private static readonly Color Ink = Color.FromHex("#111111");
        private static readonly Color Muted = Color.FromHex("#5b6c99");
        private static readonly Color Brand = Color.FromHex("#003399");
        private static readonly Color Line = Color.FromHex("#8fa3cf");
        private static readonly Color AlertInk = Color.FromHex("#b02a4a");
        private static readonly Color Paper = Color.FromHex("#ffffff");

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Certificate of Registration — {studentName}",
            Author = "SEN-GEN",
            Subject = $"Enrolled subjects for {semesterName}"
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink).FontFamily(Fonts.Calibri));

                page.Header().Element(Header);
                page.Content().PaddingTop(8).Element(Body);
                page.Footer().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text($"Generated {generatedAt:dd MMM yyyy HH:mm} · SEN-GEN")
                        .FontSize(7).FontColor(Muted);
                    row.ConstantItem(120).AlignRight().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(7).FontColor(Muted);
                        t.Span(" / ").FontSize(7).FontColor(Muted);
                        t.TotalPages().FontSize(7).FontColor(Muted);
                    });
                });
            });
        }

        private void Header(IContainer container) =>
            container.Column(column =>
            {
                column.Item().Text("STI COLLEGE ALAMINOS").FontSize(13).Bold().FontColor(Brand);
                column.Item().Text("Certificate of Registration").FontSize(10).FontColor(Muted);
                column.Item().Text(semesterName).FontSize(9).FontColor(Muted);

                column.Item().PaddingTop(6).Border(1).BorderColor(Line).Padding(6).Column(box =>
                {
                    box.Item().Text(studentName).Bold().FontSize(11);
                    box.Item().Text($"{studentNumber}  ·  {programLabel}  ·  {yearLevelLabel}  ·  {studentTypeLabel}")
                        .FontSize(8.5f).FontColor(Muted);
                });

                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);
            });

        private void Body(IContainer container) =>
            container.Column(column =>
            {
                if (rows.Count == 0)
                {
                    column.Item().PaddingTop(30).Text(
                        "No approved subjects for this term yet. Reserve your seats under Subject enlistment; "
                        + "this form lists the classes once the Registrar approves them.")
                        .FontSize(10).FontColor(Muted);
                    return;
                }

                column.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(58);   // code
                        c.RelativeColumn(2.6f); // title
                        c.ConstantColumn(30);   // units
                        c.ConstantColumn(64);   // section
                        c.ConstantColumn(52);   // days
                        c.ConstantColumn(80);   // time
                        c.ConstantColumn(66);   // room
                        c.RelativeColumn(1.6f); // faculty
                    });

                    table.Header(header =>
                    {
                        Head(header.Cell(), "CODE");
                        Head(header.Cell(), "DESCRIPTIVE TITLE");
                        Head(header.Cell(), "UNITS", center: true);
                        Head(header.Cell(), "SECTION");
                        Head(header.Cell(), "DAYS");
                        Head(header.Cell(), "TIME");
                        Head(header.Cell(), "ROOM");
                        Head(header.Cell(), "FACULTY");
                    });

                    foreach (var row in rows)
                    {
                        Body(table.Cell(), row.Code).Bold();
                        Body(table.Cell(), row.IsAmended ? $"{row.Title}  ·  CHANGED" : row.Title);
                        Body(table.Cell(), row.Units.ToString(), center: true);
                        Body(table.Cell(), row.SectionCode);
                        Body(table.Cell(), row.Days);
                        Body(table.Cell(), row.Time);
                        Body(table.Cell(), row.Room);
                        Body(table.Cell(), row.Faculty);
                    }

                    table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Line).PaddingVertical(3)
                        .AlignRight().Text("TOTAL UNITS").FontSize(9).Bold();
                    table.Cell().BorderTop(1).BorderColor(Line).PaddingVertical(3)
                        .AlignCenter().Text(rows.Sum(r => r.Units).ToString()).FontSize(9).Bold();
                    table.Cell().ColumnSpan(5).BorderTop(1).BorderColor(Line).PaddingVertical(3).Text(string.Empty);
                });

                if (rows.Any(r => r.IsAmended))
                {
                    column.Item().PaddingTop(6).Text(
                        "⚠ Subjects marked CHANGED were rescheduled after publication. "
                        + "Check My schedule for the current time before relying on a printed copy.")
                        .FontSize(7.5f).Bold().FontColor(AlertInk);
                }

                column.Item().PaddingTop(24).Row(row =>
                {
                    SignBlock(row.RelativeItem(), "Student:", studentName);
                    row.ConstantItem(40);
                    SignBlock(row.RelativeItem(), "Registrar:", null);
                });
            });

        private void Head(IContainer container, string text, bool center = false)
        {
            var cell = container.Background(Brand).PaddingVertical(4).PaddingHorizontal(4);
            (center ? cell.AlignCenter() : cell).Text(text).FontSize(7.5f).Bold().FontColor(Paper);
        }

        private TextSpanDescriptor Body(IContainer container, string text, bool center = false)
        {
            var cell = container.Background(Paper).BorderBottom(1).BorderColor(Line)
                .PaddingVertical(3).PaddingHorizontal(4);
            return (center ? cell.AlignCenter() : cell).Text(text).FontSize(8);
        }

        private void SignBlock(IContainer container, string label, string? name) =>
            container.Column(col =>
            {
                col.Item().Text(label).FontSize(9).Bold();
                col.Item().PaddingTop(24).LineHorizontal(1).LineColor(Ink);
                col.Item().Text(string.IsNullOrWhiteSpace(name) ? "Signature over printed name" : name)
                    .FontSize(8.5f).Bold();
            });
    }
}
