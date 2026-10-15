using System.Text.RegularExpressions;

namespace JobHunt.Api.Features.CoverLetters;

// Turns a pasted letter into a candidate without AI. Paragraphs are separated by blank lines; text with no blank
// lines at all (pasted from Word or email) treats each line as a paragraph. Lines before the body are header:
// date, contact and address lines are dropped (the server header has them), a greeting is kept, and the last
// remaining short line is the subject. Everything from the sign-off on is closing plus signature.
public static partial class CoverLetterTextParser
{
    public const int MaxTextLength = 30_000;
    private const int MaxHeaderLineLength = 150;

    public static CoverLetterContent Parse(string text, CoverLetterContent header)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            throw new InvalidCoverLetterException($"text: paste a letter of 1 to {MaxTextLength} characters.");
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).ToList();
        var blocks = lines.Any(l => l.Length == 0)
            ? string.Join("\n", lines).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Where(b => b.Count > 0).ToList()
            : lines.Select(l => new List<string> { l }).ToList();

        string? closing = null;
        var end = blocks.FindIndex(b => Closing().IsMatch(b[0]));
        if (end >= 0)
        {
            closing = blocks[end][0].TrimEnd(',');
            blocks = blocks[..end];
        }

        string? subject = null, greeting = null;
        var start = 0;
        for (; start < blocks.Count && IsHeader(blocks[start]); start++)
        {
            foreach (var line in blocks[start])
            {
                if (Greeting().IsMatch(line)) greeting = line;
                else if (!IsKnownHeaderLine(line, header)) subject = line;
            }
        }

        // Lines inside a paragraph are wrapped text, except list items, which keep their own line.
        var paragraphs = blocks[start..].Select(b => string.Join("", b.Select((line, i) =>
            i == 0 ? line : ListItem().IsMatch(line) ? "\n" + line : " " + line))).ToList();
        if (paragraphs.Count == 0)
            throw new InvalidCoverLetterException("text: no letter body was found. Separate paragraphs with blank lines.");
        return CoverLetterPrompt.WithBody(header, subject, greeting, paragraphs, closing);
    }

    // Header blocks are short lines that don't end a sentence: addresses, dates, subject, greeting.
    private static bool IsHeader(List<string> block) =>
        block.All(l => l.Length <= MaxHeaderLineLength && !SentenceEnd().IsMatch(l) || Greeting().IsMatch(l));

    private static bool IsKnownHeaderLine(string line, CoverLetterContent header) =>
        LetterDate().IsMatch(line) || ContactLabel().IsMatch(line) || line.StartsWith('[')
        || new[] { header.FullName, header.Phone, header.Email, header.Organization, header.RecipientName }
            .Any(v => v is not null && line.Contains(v, StringComparison.OrdinalIgnoreCase))
        || (header.Address ?? "").Split('\n').Contains(line, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(med\s+)?vennlig\s+hilsen|^mvh\b|^beste\s+hilsen|^(kind|best|warm)\s+regards|^regards\b|^(yours\s+)?(sincerely|faithfully)|^best\s+wishes", RegexOptions.IgnoreCase)]
    private static partial Regex Closing();

    [GeneratedRegex(@"^(hei|hallo|kjære|god\s+dag|til|dear|hi|hello|to)\b.*[,!:]$", RegexOptions.IgnoreCase)]
    private static partial Regex Greeting();

    [GeneratedRegex(@"[.!?]$")]
    private static partial Regex SentenceEnd();

    // "15. oktober 2026", "15 October 2026", "October 15, 2026", "15.10.2026", "2026-10-15", optionally after a place.
    [GeneratedRegex(@"(\d{1,2}\.?\s+\p{L}+\s+\d{4}|\p{L}+\s+\d{1,2},\s*\d{4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex LetterDate();

    [GeneratedRegex(@"^(telefon|tlf|mobil|e-?post|phone|mobile|email|adresse|address|bosted)\.?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ContactLabel();

    [GeneratedRegex(@"^([-–•*]|\d+[.)])\s")]
    private static partial Regex ListItem();
}
