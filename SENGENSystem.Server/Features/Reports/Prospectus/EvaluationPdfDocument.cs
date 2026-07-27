using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.Reports.Prospectus
{
    /// <summary>One ruled subject on the evaluation sheet.</summary>
    internal sealed record EvaluationPdfRow(
        int YearLevel,
        string TermLabel,
        string Code,
        string Title,
        int Units,
        bool Credited,
        string? SourceSubject,
        string? SourceGrade);

    /// <summary>
    /// The transferee evaluation sheet (FR-EVAL-03, FR-RPT-05): the Registrar's credit ruling as a
    /// signable record. Credited subjects print with what they were credited <i>from</i> — a credit
    /// nobody can trace back is a credit nobody can defend — and the remainder prints as the
    /// student's actual load ahead. The assigned year level and a signature block close it, because
    /// this is the document that goes in the student's folder.
    /// </summary>
    internal sealed class EvaluationPdfDocument(
        string studentName,
        string studentNumber,
        string programLabel,
        string? previousSchool,
        string? curriculumNote,
        string statusLabel,
        int creditedUnits,
        int toTakeUnits,
        int assignedYearLevel,
        int recommendedYearLevel,
        string? remarks,
        string? evaluatedBy,
        DateTime? evaluatedAt,
        List<EvaluationPdfRow> rows,
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
            Title = $"Transferee Credit Evaluation — {studentName}",
            Author = "SEN-GEN",
            Subject = "Transferee subject credit evaluation"
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
                column.Item().Text("Transferee Credit Evaluation").FontSize(10).FontColor(Muted);

                column.Item().PaddingTop(6).Border(1).BorderColor(Line).Padding(6).Column(box =>
                {
                    box.Item().Text(studentName).Bold().FontSize(11);
                    box.Item().Text($"{studentNumber}  ·  {programLabel}").FontSize(8.5f).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(previousSchool))
                    {
                        box.Item().Text($"Previous school: {previousSchool}").FontSize(8).FontColor(Muted);
                    }
                    if (!string.IsNullOrWhiteSpace(curriculumNote))
                    {
                        box.Item().Text($"Evaluated against: {curriculumNote}").FontSize(8).FontColor(Muted);
                    }
                    box.Item().PaddingTop(2).Text($"Status: {statusLabel}").FontSize(8).Bold();
                });

                column.Item().PaddingTop(6).Row(row =>
                {
                    Stat(row.RelativeItem(), "CREDITED", $"{creditedUnits} units");
                    Stat(row.RelativeItem(), "TO TAKE", $"{toTakeUnits} units");
                    Stat(row.RelativeItem(), "YEAR LEVEL", YearLevelPolicy.Label(assignedYearLevel));
                });
                if (assignedYearLevel != recommendedYearLevel)
                {
                    column.Item().PaddingTop(2).Text(
                        $"Year level set by the Registrar; the credited units derive "
                        + $"{YearLevelPolicy.Label(recommendedYearLevel)}.")
                        .FontSize(7.5f).FontColor(Muted);
                }

                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);
            });

        private void Stat(IContainer container, string label, string value) =>
            container.Border(1).BorderColor(Line).Padding(5).Column(col =>
            {
                col.Item().Text(label).FontSize(7).Bold().FontColor(Muted);
                col.Item().Text(value).FontSize(11).Bold().FontColor(Brand);
            });

        private void Body(IContainer container) =>
            container.Column(column =>
            {
                if (rows.Count == 0)
                {
                    column.Item().PaddingTop(30)
                        .Text("No subjects have been evaluated yet.").FontSize(10).FontColor(Muted);
                    return;
                }

                Section(column, "CREDITED — not retaken here", rows.Where(r => r.Credited).ToList(), credited: true);
                Section(column, "TO TAKE — this student's remaining load", rows.Where(r => !r.Credited).ToList(), credited: false);

                if (!string.IsNullOrWhiteSpace(remarks))
                {
                    column.Item().PaddingTop(10).Text("REMARKS").FontSize(8).Bold().FontColor(Muted);
                    column.Item().PaddingTop(2).Border(1).BorderColor(Line).Padding(6)
                        .Text(remarks).FontSize(8.5f);
                }

                column.Item().PaddingTop(20).Row(row =>
                {
                    SignBlock(row.RelativeItem(), "Evaluated by:", evaluatedBy, "Registrar",
                        evaluatedAt is { } at ? $"{at:dd MMM yyyy}" : null);
                    row.ConstantItem(40);
                    SignBlock(row.RelativeItem(), "Conforme:", studentName, "Student", null);
                });
            });

        private void Section(ColumnDescriptor column, string label, List<EvaluationPdfRow> subset, bool credited)
        {
            column.Item().PaddingTop(10).Text(label).FontSize(9).Bold().FontColor(Brand);
            if (subset.Count == 0)
            {
                column.Item().PaddingTop(2).Text("None.").FontSize(8).FontColor(Muted);
                return;
            }

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(44);   // year / term
                    c.ConstantColumn(58);   // code
                    c.RelativeColumn(3);    // title
                    c.ConstantColumn(34);   // units
                    c.RelativeColumn(2);    // credited from / blank
                });

                table.Header(header =>
                {
                    Head(header.Cell(), "YR/TERM");
                    Head(header.Cell(), "CODE");
                    Head(header.Cell(), "DESCRIPTIVE TITLE");
                    Head(header.Cell(), "UNITS", center: true);
                    Head(header.Cell(), credited ? "CREDITED FROM" : "REMARKS");
                });

                var fill = credited ? CreditFill : Paper;
                foreach (var row in subset)
                {
                    Body(table.Cell(), $"{row.YearLevel} · {ShortTerm(row.TermLabel)}", fill);
                    Body(table.Cell(), row.Code, fill).Bold();
                    Body(table.Cell(), row.Title, fill);
                    Body(table.Cell(), row.Units.ToString(), fill, center: true);
                    Body(table.Cell(), credited ? Source(row) : string.Empty, fill);
                }

                table.Cell().ColumnSpan(3).BorderTop(1).BorderColor(Line).PaddingVertical(2)
                    .AlignRight().Text("Subtotal").FontSize(8).Bold();
                table.Cell().BorderTop(1).BorderColor(Line).PaddingVertical(2)
                    .AlignCenter().Text(subset.Sum(r => r.Units).ToString()).FontSize(8).Bold();
                table.Cell().BorderTop(1).BorderColor(Line).PaddingVertical(2).Text(string.Empty);
            });
        }

        private static string Source(EvaluationPdfRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.SourceSubject)) parts.Add(row.SourceSubject);
            if (!string.IsNullOrWhiteSpace(row.SourceGrade)) parts.Add($"({row.SourceGrade})");
            return parts.Count == 0 ? "—" : string.Join(" ", parts);
        }

        private static string ShortTerm(string termLabel) =>
            termLabel.StartsWith('2') ? "2nd" : "1st";

        private void Head(IContainer container, string text, bool center = false)
        {
            var cell = container.Background(Brand).PaddingVertical(4).PaddingHorizontal(4);
            (center ? cell.AlignCenter() : cell).Text(text).FontSize(7.5f).Bold().FontColor(Paper);
        }

        private TextSpanDescriptor Body(IContainer container, string text, Color fill, bool center = false)
        {
            var cell = container.Background(fill).BorderBottom(1).BorderColor(Line)
                .PaddingVertical(3).PaddingHorizontal(4);
            return (center ? cell.AlignCenter() : cell).Text(text).FontSize(8);
        }

        private void SignBlock(IContainer container, string label, string? name, string role, string? dated) =>
            container.Column(col =>
            {
                col.Item().Text(label).FontSize(9).Bold();
                col.Item().PaddingTop(24).LineHorizontal(1).LineColor(Ink);
                col.Item().Text(string.IsNullOrWhiteSpace(name) ? "Signature over printed name" : name)
                    .FontSize(8.5f).Bold();
                col.Item().Text(role).FontSize(7.5f).FontColor(Muted);
                if (dated is not null)
                {
                    col.Item().Text($"Date: {dated}").FontSize(7.5f).FontColor(Muted);
                }
            });
    }
}
