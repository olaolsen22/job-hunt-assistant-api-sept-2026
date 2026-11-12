using System.Text.RegularExpressions;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// How the AI may change a selected bullet: copy it (a base CV's DETAILED), keep to its own and the listing's words
// (CONCISE, or any CV tailored to a listing), or restate it in plain words (a base CV's rewrite).
public enum TailoredCvWordingMode { Exact, SourceWords, Rewrite }

// Tailored, shortened and rewritten bullets and paragraphs are AI-written, so the server holds them to their source bullets.
public static partial class TailoredCvWording
{
    // Room for grammar and inflection, such as "utviklet" for "utvikle" or an added "and".
    public const int MaxNewWords = 2;
    // A paragraph joins several bullets, so it needs a few more linking words.
    public const int MaxNewParagraphWords = 4;

    // listing: the CV is tailored to a listing, so even DETAILED bullets may take on its terms.
    public static TailoredCvWordingMode ModeFor(TailoredCvTemplate template, bool rewrite = false, bool listing = false) =>
        rewrite ? TailoredCvWordingMode.Rewrite
        : listing || template == TailoredCvTemplate.Concise ? TailoredCvWordingMode.SourceWords
        : TailoredCvWordingMode.Exact;

    // A bullet that breaks the mode's rule falls back to its source text; such a paragraph is dropped, so the PDF joins
    // the bullets instead. Exact text is checked by ValidateSourceIdsAsync.
    // sourceTexts(id) is what a bullet may draw on: its own source and translation, or its whole entry when bullets may
    // be combined.
    internal static TailoredCvContent KeepSourceWording(TailoredCvContent content, TailoredCvWordingMode mode,
        Func<int, IEnumerable<string>> sourceTexts, Func<int, string> fallback, string listingText,
        IReadOnlyCollection<string> skillNames)
    {
        if (mode == TailoredCvWordingMode.Exact) return content;

        bool Keeps(string text, IEnumerable<int> ids, int maxNewWords)
        {
            var sources = ids.SelectMany(sourceTexts).ToList();
            return mode == TailoredCvWordingMode.Rewrite
                ? KeepsSourceFacts(text, sources, skillNames)
                : KeepsSourceWords(text, sources, listingText, maxNewWords);
        }

        var checkedBullets = content.MapBullets(bullets => [.. bullets.Select(b =>
            b.SourceBulletId is not { } id || Keeps(b.Text, [id], MaxNewWords) ? b : b with { Text = fallback(id) })]);
        // A DETAILED paragraph may rephrase freely, even when its bullets take on the listing's terms.
        if (mode == TailoredCvWordingMode.SourceWords && content.Template == TailoredCvTemplate.Detailed)
            return checkedBullets;
        return checkedBullets
            .MapParagraphs((bullets, paragraph) =>
            {
                var ids = bullets.Where(b => b.SourceBulletId is not null).Select(b => b.SourceBulletId!.Value).ToList();
                return paragraph is not null && ids.Count > 0 && Keeps(paragraph, ids, MaxNewParagraphWords) ? paragraph : null;
            });
    }

    public static bool KeepsSourceWords(string text, IEnumerable<string> sourceTexts, string listingText,
        int maxNewWords = MaxNewWords)
    {
        var source = sourceTexts.SelectMany(Words).ToHashSet();
        var listing = Words(listingText).ToHashSet();
        var words = Words(text).ToList();
        // A number only the listing has ("5 years", "40%") would be an invented metric.
        if (words.Any(w => w.Any(char.IsDigit) && !source.Contains(w))) return false;
        return words.Count(w => !source.Contains(w) && !listing.Contains(w)) <= maxNewWords;
    }

    // A plain-language rewrite needs new words, so only the checkable facts are held to the source: every number and
    // every skill name ("React", "Git") it mentions must appear in the source too.
    public static bool KeepsSourceFacts(string text, IEnumerable<string> sourceTexts, IEnumerable<string> skillNames)
    {
        var sources = sourceTexts.Select(t => Words(t).ToList()).ToList();
        var words = Words(text).ToList();
        if (words.Any(w => w.Any(char.IsDigit) && !sources.Any(s => s.Contains(w)))) return false;
        return skillNames.Select(n => Words(n).ToList()).Where(n => n.Count > 0)
            .All(name => !ContainsPhrase(words, name) || sources.Any(s => ContainsPhrase(s, name)));
    }

    private static bool ContainsPhrase(List<string> words, List<string> phrase) =>
        Enumerable.Range(0, Math.Max(0, words.Count - phrase.Count + 1))
            .Any(i => words.Skip(i).Take(phrase.Count).SequenceEqual(phrase));

    public static string ListingText(string title, string? description, IEnumerable<string> skillNames) =>
        string.Join('\n', [title, description ?? "", .. skillNames]);

    // Keeps "C#", "C++", ".NET" and "Node.js" as single words.
    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant());

    [GeneratedRegex(@"\.?[\p{L}\p{N}](?:[\p{L}\p{N}+#]|\.(?=[\p{L}\p{N}]))*")]
    private static partial Regex WordPattern();
}
