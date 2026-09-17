using System.Text;
using System.Text.RegularExpressions;
using PolicyLens.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PolicyLens.Api.Services;

/// <summary>
/// Extracts the policy PDF's text and splits it into one chunk per numbered section
/// (e.g. "3. International Remote Work"), since each section is a self-contained topic.
/// </summary>
public sealed class PdfIngestionService
{
    private static readonly Regex SectionHeadingRegex = new(@"^(\d{1,2})\.\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex PageHeaderRegex = new(@"^Page\s+\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<PolicySection> ExtractSections(string pdfPath)
    {
        var lines = new List<string>();
        using (var document = PdfDocument.Open(pdfPath))
        {
            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page);
                lines.AddRange(pageText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        var sections = new List<PolicySection>();
        int? currentNumber = null;
        string? currentTitle = null;
        var body = new StringBuilder();

        void FlushCurrentSection()
        {
            if (currentNumber is int number && currentTitle is not null && body.Length > 0)
            {
                sections.Add(new PolicySection(number, currentTitle, body.ToString().Trim()));
            }
            body.Clear();
        }

        foreach (var line in lines)
        {
            if (PageHeaderRegex.IsMatch(line))
            {
                continue;
            }

            var heading = SectionHeadingRegex.Match(line);
            if (heading.Success)
            {
                FlushCurrentSection();
                currentNumber = int.Parse(heading.Groups[1].Value);
                currentTitle = heading.Groups[2].Value.Trim();
                continue;
            }

            if (currentNumber is not null)
            {
                body.Append(line).Append(' ');
            }
        }

        FlushCurrentSection();
        return sections;
    }
}
