using System.Text.RegularExpressions;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Concise bullets are AI-shortened, so the wording must stay the candidate's: nearly every word comes from the
// source bullet, or from the listing where it names the same thing (exact listing terms help ATS matching).
public static partial class TailoredCvWording
{
    // Room for grammar and inflection, such as "utviklet" for "utvikle" or an added "and".
    private const int MaxNewWords = 2;

    public static bool KeepsSourceWords(string text, IEnumerable<string> sourceTexts, string listingText)
    {
        var source = sourceTexts.SelectMany(Words).ToHashSet();
        var listing = Words(listingText).ToHashSet();
        var words = Words(text).ToList();
        // A number only the listing has ("5 years", "40%") would be an invented metric.
        if (words.Any(w => w.Any(char.IsDigit) && !source.Contains(w))) return false;
        return words.Count(w => !source.Contains(w) && !listing.Contains(w)) <= MaxNewWords;
    }

    public static string ListingText(string title, string? description, IEnumerable<string> skillNames) =>
        string.Join('\n', [title, description ?? "", .. skillNames]);

    // Keeps "C#", "C++", ".NET" and "Node.js" as single words.
    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant());

    [GeneratedRegex(@"\.?[\p{L}\p{N}](?:[\p{L}\p{N}+#]|\.(?=[\p{L}\p{N}]))*")]
    private static partial Regex WordPattern();
}
