using System.Text.Json;
using JobHunt.Api.Data;

namespace JobHunt.Api.Features.TailoredCvDrafts;

internal static class TailoredCvAnalysisDecoder
{
    private const int MaxSelectedSourceIds = 200;

    // Null when the answer is unreadable or names ids outside the context the model was sent.
    public static TailoredCvAnalysisResult? Decode(string json, TailoredCvSection section, int? sourceEntryId,
        string contextJson, TailoredCvTemplate template)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var context = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return section switch
            {
                TailoredCvSection.Experience or TailoredCvSection.Education or TailoredCvSection.Project =>
                    DecodeEntry(root, context.RootElement, section, sourceEntryId, template),
                TailoredCvSection.Skills => DecodeSkills(root, context.RootElement),
                TailoredCvSection.Summary => DecodeSummary(root),
                _ => throw new JsonException(),
            };
        }
        // GreenDonut has its own KeyNotFoundException, hence the full name.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            return null;
        }
    }

    private static TailoredCvAnalysisResult DecodeEntry(JsonElement root, JsonElement context, TailoredCvSection section,
        int? sourceEntryId, TailoredCvTemplate template)
    {
        var entry = context.GetProperty(section.ToString().ToLowerInvariant());
        var sources = entry.GetProperty("bullets").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetInt32(), b => b.GetProperty("text").GetString()!);
        var concise = template == TailoredCvTemplate.Concise;
        var listingText = ListingText(context.GetProperty("listing"));
        List<string> skillNames =
        [
            .. context.GetProperty("listing").GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString() ?? ""),
            .. entry.TryGetProperty("skills", out var entrySkills)
                ? entrySkills.EnumerateArray().Select(s => s.GetProperty("name").GetString() ?? "") : [],
        ];
        // A concise bullet may merge and condense its entry's bullets, so it is held to their facts instead of words.
        Func<string, string, bool> keeps = concise
            ? (text, _) => TailoredCvWording.KeepsSourceFacts(text, sources.Values, skillNames)
            : (text, source) => TailoredCvWording.KeepsSourceWords(text, [source], listingText);
        var bullets = DecodeBullets(root, sources, keeps);
        if (bullets.Count > MaxSelectedSourceIds) throw new JsonException();
        bullets = [.. bullets.DistinctBy(b => b.SourceBulletId)];
        var paragraph = DecodeParagraph(root);
        // A concise paragraph is an overview of the role in plain words: it may state no number or listing skill the
        // entry's bullets do not; otherwise the PDF joins the bullets instead.
        if (concise && paragraph is not null && !TailoredCvWording.KeepsSourceFacts(paragraph, sources.Values, skillNames))
            paragraph = null;
        return new(TailoredCvAnalysisStatus.Succeeded, section, sourceEntryId,
            [.. bullets.Select(b => b.SourceBulletId!.Value)], [], null, null,
            Paragraph: bullets.Count == 0 ? null : paragraph,
            Technologies: section == TailoredCvSection.Education ? null : DecodeTechnologies(root, entry),
            Bullets: bullets);
    }

    private static TailoredCvAnalysisResult DecodeSkills(JsonElement root, JsonElement context)
    {
        var ids = context.GetProperty("profileSkills").EnumerateArray()
            .Select(s => s.GetProperty("skillId").GetInt32()).ToHashSet();
        var selected = root.GetProperty("selectedSkillIds").EnumerateArray().Select(v => v.GetInt32()).ToList();
        if (selected.Count > MaxSelectedSourceIds || selected.Any(id => !ids.Contains(id))) throw new JsonException();
        return new(TailoredCvAnalysisStatus.Succeeded, TailoredCvSection.Skills, null, [], [.. selected.Distinct()], null, null);
    }

    private static TailoredCvAnalysisResult DecodeSummary(JsonElement root)
    {
        var summary = root.GetProperty("summary");
        if (summary.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || summary.ValueKind == JsonValueKind.String && summary.GetString()!.Length > TailoredCvDraftValidation.MaxSummaryLength)
            throw new JsonException();
        return new(TailoredCvAnalysisStatus.Succeeded, TailoredCvSection.Summary, null, [], [],
            summary.ValueKind == JsonValueKind.Null ? null : summary.GetString()?.Trim(), null,
            Headline: DecodeHeadline(root));
    }

    private static string? DecodeParagraph(JsonElement root)
    {
        if (!root.TryGetProperty("paragraph", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new JsonException();
        var paragraph = value.GetString()?.Trim();
        return paragraph?.Length > Inputs.MaxMarkdownLength ? throw new JsonException() : paragraph;
    }

    // keeps(text, source) is the template's wording check. A bullet failing it, without text, blank or too long falls
    // back to the source text; an unknown id fails the section.
    private static List<TailoredCvBullet> DecodeBullets(JsonElement root, Dictionary<int, string> sources,
        Func<string, string, bool> keeps) =>
        [.. root.GetProperty("bullets").EnumerateArray().Select(b =>
        {
            var id = b.GetProperty("id").GetInt32();
            if (!sources.TryGetValue(id, out var source)) throw new JsonException();
            var text = b.TryGetProperty("text", out var value) ? value.GetString()?.Trim() : null;
            return new TailoredCvBullet(text is { Length: > 0 and <= TailoredCvDraftValidation.MaxBulletLength }
                && keeps(text, source) ? text : source, id);
        })];

    private static string ListingText(JsonElement listing) =>
        TailoredCvWording.ListingText(listing.GetProperty("title").GetString() ?? "",
            listing.GetProperty("description").GetString(),
            listing.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString() ?? ""));

    // Absent, null, blank or too long for the draft = no suggestion, so the UI keeps the current headline; a wrong
    // type still makes the answer unreadable.
    private static string? DecodeHeadline(JsonElement root)
    {
        if (!root.TryGetProperty("headline", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new JsonException();
        var headline = Inputs.Clean(value.GetString());
        return headline?.Length > TailoredCvDraftValidation.MaxTextLength ? null : headline;
    }

    // Absent = no suggestion, so the UI keeps the entry's current list. IDs must be the entry's own skills.
    private static List<string>? DecodeTechnologies(JsonElement root, JsonElement entry)
    {
        if (!root.TryGetProperty("technologySkillIds", out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        var names = entry.GetProperty("skills").EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetInt32(), s => s.GetProperty("name").GetString()!);
        var ids = value.EnumerateArray().Select(v => v.GetInt32()).Distinct().ToList();
        if (ids.Count > MaxSelectedSourceIds || ids.Any(id => !names.ContainsKey(id))) throw new JsonException();
        return [.. ids.Select(id => names[id])];
    }
}
