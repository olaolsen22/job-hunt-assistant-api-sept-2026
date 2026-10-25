using System.Diagnostics.CodeAnalysis;

namespace JobHunt.Api.Data;

// Input rules every feature shares, so a field is trimmed and a link is checked the same way everywhere.
// Each caller still throws its own typed error.
public static class Inputs
{
    // Practical browser limit; real links are far shorter.
    public const int MaxUrlLength = 2048;

    // Notes, summaries and other Markdown fields.
    public const int MaxMarkdownLength = 10_000;

    // Trimmed, and blank means none, so an emptied field doesn't store whitespace.
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // http(s) only: the UI renders these as links, and a javascript: URL would run on click.
    public static bool TryHttpUrl(string? value, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        var text = value?.Trim();
        return text is { Length: <= MaxUrlLength }
            && Uri.TryCreate(text, UriKind.Absolute, out url)
            && url.Scheme is "http" or "https";
    }
}
