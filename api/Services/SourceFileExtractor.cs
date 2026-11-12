using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DistillForge.Api.Services;

public sealed class SourceFileException(string message) : Exception(message);

/// <summary>Converts an uploaded source document to canonical Markdown-like text.
/// Images are deliberately ignored: source evidence must always be selectable text.</summary>
public sealed class SourceFileExtractor
{
    public const long MaxUploadBytes = 10_000_000;

    public async Task<string> ExtractAsync(Stream input, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".txt" or ".md" or ".markdown" => await ReadTextAsync(input, ct),
                ".docx" => ExtractDocx(input),
                ".pdf" => ExtractPdf(input),
                ".doc" => throw new SourceFileException("Legacy .doc files are not supported. Save the document as .docx first."),
                _ => throw new SourceFileException("Choose a .txt, .md, .docx or text-based .pdf file.")
            };
        }
        catch (SourceFileException) { throw; }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or ArgumentException)
        {
            throw new SourceFileException("That document could not be read. It may be damaged, password-protected, or not a valid file of that type.");
        }
    }

    private static async Task<string> ReadTextAsync(Stream input, CancellationToken ct)
    {
        using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static string ExtractDocx(Stream input)
    {
        using var document = WordprocessingDocument.Open(input, false);
        var mainPart = document.MainDocumentPart
            ?? throw new SourceFileException("The DOCX file has no main document part.");
        var body = mainPart.Document?.Body
            ?? throw new SourceFileException("The DOCX file has no document body.");
        var output = new StringBuilder();
        foreach (var child in body.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    AppendParagraph(output, paragraph);
                    break;
                case Table table:
                    AppendTable(output, table);
                    break;
            }
        }
        return output.ToString().Trim();
    }

    private static void AppendParagraph(StringBuilder output, Paragraph paragraph)
    {
        var text = TextOf(paragraph);
        if (string.IsNullOrWhiteSpace(text)) return;
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var heading = style?.ToLowerInvariant() switch
        {
            "heading1" or "heading 1" => "# ",
            "heading2" or "heading 2" => "## ",
            "heading3" or "heading 3" => "### ",
            _ => ""
        };
        output.Append(heading).AppendLine(text).AppendLine();
    }

    private static void AppendTable(StringBuilder output, Table table)
    {
        var rows = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Select(TextOf).ToList())
            .Where(row => row.Count > 0)
            .ToList();
        if (rows.Count == 0) return;

        int columns = rows.Max(row => row.Count);
        foreach (var row in rows) while (row.Count < columns) row.Add("");
        AppendMarkdownRow(output, rows[0]);
        AppendMarkdownRow(output, Enumerable.Repeat("---", columns));
        foreach (var row in rows.Skip(1)) AppendMarkdownRow(output, row);
        output.AppendLine();
    }

    private static void AppendMarkdownRow(StringBuilder output, IEnumerable<string> cells) =>
        output.Append("| ").Append(string.Join(" | ", cells.Select(EscapeCell))).AppendLine(" |");

    private static string TextOf(OpenXmlElement element)
    {
        var parts = element.Descendants<Text>().Select(t => t.Text)
            .Concat(element.Descendants<TabChar>().Select(_ => "\t"));
        return string.Join("", parts).Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|").Replace("\n", " ");

    private static string ExtractPdf(Stream input)
    {
        using var pdf = PdfDocument.Open(input);
        // Page.Text follows the PDF's drawing order. That often has no word or line
        // boundaries at all, so reconstruct readable lines from the positioned words.
        var text = string.Join("\n\n", pdf.GetPages().Select(ExtractPdfPage).Where(page => page.Length > 0));
        if (string.IsNullOrWhiteSpace(text))
            throw new SourceFileException("No selectable text was found in this PDF. Image-only PDFs need OCR, which DistillForge does not use.");
        return text;
    }

    private static string ExtractPdfPage(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left)
            .ToList();
        if (words.Count == 0) return "";

        var lines = new List<PdfLine>();
        foreach (var word in words)
        {
            // Different fonts on a single printed line can have slightly different
            // bounding boxes. Half the word height is tolerant of that without
            // merging adjacent lines in normal documents.
            var tolerance = Math.Max(2, (word.BoundingBox.Top - word.BoundingBox.Bottom) / 2);
            var line = lines.FirstOrDefault(l => Math.Abs(l.Baseline - word.BoundingBox.Bottom) <= tolerance);
            if (line is null)
            {
                line = new PdfLine(word.BoundingBox.Bottom);
                lines.Add(line);
            }
            line.Words.Add(word);
        }

        return string.Join("\n", lines
            .OrderByDescending(line => line.Baseline)
            .Select(line => string.Join(" ", line.Words.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))));
    }

    private sealed class PdfLine(double baseline)
    {
        public double Baseline { get; } = baseline;
        public List<Word> Words { get; } = [];
    }
}
