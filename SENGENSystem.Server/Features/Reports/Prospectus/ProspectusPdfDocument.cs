using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.Reports.Prospectus
{
    /// <summary>What a year level's prospectus prints — one subject line.</summary>
    internal sealed record ProspectusRow(
        string Code,
        string Title,
        int Units,
        int LectureHours,
        int LaboratoryHours,
        string Prerequisites);

    /// <summary>A year level's worth of subjects, split by term.</summary>
    internal sealed record ProspectusYear(int YearLevel, List<ProspectusRow> FirstTerm, List<ProspectusRow> SecondTerm)
    {
        public int TotalUnits => FirstTerm.Sum(r => r.Units) + SecondTerm.Sum(r => r.Units);
    }

    /// <summary>Identifies whose prospectus this is, when it is printed for one student.</summary>
    internal sealed record StudentProspectusHeader(
        string FullName, string StudentNumber, string StudentTypeLabel, string YearLevelLabel);

    /// <summary>
    /// The curriculum prospectus (FR-RPT-05): the subjects a student at a given year level takes,
    /// term by term, with units, the lecture/laboratory hour split, and prerequisites. This is the
    /// sheet the Registrar hands a student who asks "what am I taking this year?" — so it prints
    /// per year level, and prints the whole ladder when no single year is asked for.
    /// <para>
    /// A transferee's copy is the same document with their credited subjects shaded and marked, so
    /// the sheet shows both what was accounted for and what remains — a subject silently dropped
    /// from the list would look like an omission rather than a credit.
    /// </para>
    /// </summary>
    internal sealed class ProspectusPdfDocument(
        string programCode,
        string programName,
        string? curriculumNote,
        List<ProspectusYear> years,
        StudentProspectusHeader? student,
        IReadOnlySet<string> creditedCodes,
        DateTime generatedAt) : IDocument
    {
        private static readonly Color Ink = Color.FromHex("#111111");
        private static readonly Color Muted = Color.FromHex("#5b6c99");
        private static readonly Color Brand = Color.FromHex("#003399");
        private static readonly Color Line = Color.FromHex("#8fa3cf");
        private static readonly Color CreditFill = Color.FromHex("#eaf3ea");
        private static readonly Color Paper = Color.FromHex("#ffffff");

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Curriculum Prospectus — {programCode}",
            Author = "SEN-GEN",
            Subject = "Subjects by year level"
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink).FontFamily(Fonts.Calibri));

                page.Header().Element(Header);
                page.Content().PaddingTop(10).Element(Body);
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
                column.Item().Text("Curriculum Prospectus").FontSize(10).FontColor(Muted);
                column.Item().PaddingTop(4).Text($"{programCode} — {programName}").FontSize(11).Bold();
                if (!string.IsNullOrWhiteSpace(curriculumNote))
                {
                    column.Item().Text(curriculumNote).FontSize(8).FontColor(Muted);
                }

                if (student is not null)
                {
                    column.Item().PaddingTop(6).Border(1).BorderColor(Line).Padding(6).Column(box =>
                    {
                        box.Item().Text(student.FullName).Bold().FontSize(10);
                        box.Item().Text(
                            $"{student.StudentNumber}  ·  {student.StudentTypeLabel}  ·  {student.YearLevelLabel}")
                            .FontSize(8).FontColor(Muted);
                        if (creditedCodes.Count > 0)
                        {
                            box.Item().PaddingTop(2).Text(
                                $"{creditedCodes.Count} subject(s) credited from the previous school are shaded and "
                                + "marked CREDITED — the rest is what this student still takes.")
                                .FontSize(7.5f).FontColor(Muted);
                        }
                    });
                }

                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);
            });

        private void Body(IContainer container) =>
            container.Column(column =>
            {
                if (years.Count == 0)
                {
                    column.Item().PaddingTop(30).Text("This curriculum has no subjects yet.")
                        .FontSize(10).FontColor(Muted);
                    return;
                }

                foreach (var year in years)
                {
                    column.Item().PaddingTop(10)
                        .Text(YearLevelPolicy.Label(year.YearLevel).ToUpperInvariant())
                        .FontSize(10).Bold().FontColor(Brand);

                    TermBlock(column, "FIRST SEMESTER", year.FirstTerm);
                    TermBlock(column, "SECOND SEMESTER", year.SecondTerm);

                    column.Item().PaddingTop(3).AlignRight()
                        .Text($"Year total: {year.TotalUnits} units").FontSize(8.5f).Bold();
                }

                column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Line);
                column.Item().PaddingTop(4).AlignRight()
                    .Text($"TOTAL: {years.Sum(y => y.TotalUnits)} units").FontSize(10).Bold().FontColor(Brand);
            });

        private void TermBlock(ColumnDescriptor column, string label, List<ProspectusRow> rows)
        {
            if (rows.Count == 0) return;

            column.Item().PaddingTop(5).Text(label).FontSize(8.5f).Bold().FontColor(Muted);
            column.Item().PaddingTop(2).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(58);   // code
                    c.RelativeColumn(3);    // descriptive title
                    c.ConstantColumn(34);   // units
                    c.ConstantColumn(48);   // lec/lab
                    c.RelativeColumn(1.4f); // prerequisite
                });

                table.Header(header =>
                {
                    Head(header.Cell(), "CODE");
                    Head(header.Cell(), "DESCRIPTIVE TITLE");
                    Head(header.Cell(), "UNITS", center: true);
                    Head(header.Cell(), "LEC/LAB", center: true);
                    Head(header.Cell(), "PREREQUISITE");
                });

                foreach (var row in rows)
                {
                    // A credited subject stays on the sheet — the student needs to see it was
                    // accounted for, not silently dropped — but reads as settled, not pending.
                    var credited = creditedCodes.Contains(row.Code);
                    var fill = credited ? CreditFill : Paper;

                    Body(table.Cell(), row.Code, fill).Bold();
                    Body(table.Cell(), credited ? $"{row.Title}  ·  CREDITED" : row.Title, fill);
                    Body(table.Cell(), row.Units.ToString(), fill, center: true);
                    Body(table.Cell(), $"{row.LectureHours}/{row.LaboratoryHours}", fill, center: true);
                    Body(table.Cell(), string.IsNullOrWhiteSpace(row.Prerequisites) ? "—" : row.Prerequisites, fill);
                }

                table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Line).PaddingVertical(2)
                    .AlignRight().Text("Term total").FontSize(8).Bold();
                table.Cell().BorderTop(1).BorderColor(Line).PaddingVertical(2)
                    .AlignCenter().Text(rows.Sum(r => r.Units).ToString()).FontSize(8).Bold();
                table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Line).PaddingVertical(2).Text(string.Empty);
            });
        }

        private void Head(IContainer container, string text, bool center = false)
        {
            var cell = container.Background(Brand).PaddingVertical(4).PaddingHorizontal(4);
            (center ? cell.AlignCenter() : cell)
                .Text(text).FontSize(7.5f).Bold().FontColor(Paper);
        }

        private TextSpanDescriptor Body(IContainer container, string text, Color fill, bool center = false)
        {
            var cell = container.Background(fill).BorderBottom(1).BorderColor(Line)
                .PaddingVertical(3).PaddingHorizontal(4);
            return (center ? cell.AlignCenter() : cell).Text(text).FontSize(8);
        }
    }
}
