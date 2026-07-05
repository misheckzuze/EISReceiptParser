using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FiscalReceiptParser.Services;

public static class PdfTextExtractor
{
    /// <summary>
    /// Opens the PDF and returns its content as a list of trimmed, non-empty lines,
    /// reconstructed from word positions rather than the page's raw text.
    ///
    /// PdfPig's Page.Text property does NOT reliably preserve line breaks for every
    /// PDF (this receipt's content stream produces one long run of text with no
    /// usable newlines). Grouping words by their vertical (Y) position on the page
    /// rebuilds the actual printed rows regardless of how the PDF encodes them.
    /// </summary>
    public static List<string> ExtractLines(string pdfPath)
    {
        var lines = new List<string>();
        const double yTolerance = 2.0;

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0) continue;

            var lineGroups = new List<List<Word>>();

            // Top of page first, left to right within each row.
            foreach (var word in words
                         .OrderByDescending(w => w.BoundingBox.Bottom)
                         .ThenBy(w => w.BoundingBox.Left))
            {
                var group = lineGroups.FirstOrDefault(g =>
                    Math.Abs(g[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= yTolerance);

                if (group != null)
                    group.Add(word);
                else
                    lineGroups.Add(new List<Word> { word });
            }

            foreach (var group in lineGroups)
            {
                var lineText = string.Join(" ",
                    group.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));

                lineText = lineText.Trim();
                if (lineText.Length > 0)
                    lines.Add(lineText);
            }
        }

        return lines;
    }
}