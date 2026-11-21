using System.Text.RegularExpressions;

namespace JobHunt.Api.Features.Prompts;

internal static partial class PromptText
{
    // Enough for a real lookup list, alphabetical, so names late in the alphabet are still reused; still caps the
    // prompt (and its cost on the API-key path) for users with very many roles, skills or industries.
    public const int MaxNames = 300;

    // Built from the C# enums, using Hot Chocolate's naming (FixedTerm → FIXED_TERM), so values can't drift.
    public static string Values<TEnum>() where TEnum : struct, Enum =>
        string.Join(" | ", Enum.GetNames<TEnum>().Select(n => $"\"{WordBoundary().Replace(n, "_").ToUpperInvariant()}\""));

    public static string NameList(List<string> names) => names.Count == 0 ? "(none yet)" : string.Join(", ", names);

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
