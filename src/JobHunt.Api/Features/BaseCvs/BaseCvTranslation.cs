using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;

namespace JobHunt.Api.Features.BaseCvs;

// UntranslatedFields: paths like "experiences[0].bullets[2]" whose text had no profile translation to swap in.
public record BaseCvTranslation(TailoredCvContent Content, Language FromLanguage, List<string> UntranslatedFields);

internal static class BaseCvTranslator
{
    // Profile-copied text in `from` becomes the profile's text in `to`, via source ids. Proper nouns stay.
    internal static BaseCvTranslation Translate(TailoredCvContent source, Language from, Language to, Profile profile)
    {
        if (from == to) return new(source, from, []);

        var untranslated = new List<string>();
        var original = profile.ContentLanguage;
        var translation = profile.Translations.FirstOrDefault(t => t.Language == to);
        var fromTranslation = profile.Translations.FirstOrDefault(t => t.Language == from);
        var bullets = profile.Experiences.SelectMany(e => e.Bullets).Concat(profile.Educations.SelectMany(e => e.Bullets))
            .Concat(profile.Certifications.SelectMany(c => c.Bullets)).Concat(profile.Projects.SelectMany(p => p.Bullets))
            .ToDictionary(b => b.Id);
        var experiences = profile.Experiences.ToDictionary(e => e.Id);
        var educations = profile.Educations.ToDictionary(e => e.Id);
        var projects = profile.Projects.ToDictionary(p => p.Id);
        var skills = profile.Skills.ToDictionary(s => s.SkillId, s => s.Skill);

        string? Swap(string? current, string? inFrom, string? inTo, bool hasTarget, string path)
        {
            if (string.IsNullOrEmpty(current)) return current;
            if (current == inFrom && hasTarget) return inTo;
            untranslated.Add(path);
            return current;
        }

        string? Keep(string? current, string path)
        {
            if (!string.IsNullOrEmpty(current)) untranslated.Add(path);
            return current;
        }

        List<TailoredCvBullet> Bullets(List<TailoredCvBullet> list, string path) =>
        [
            .. list.Select((b, i) => bullets.GetValueOrDefault(b.SourceBulletId ?? 0) is { } src
                ? b with
                {
                    Text = Swap(b.Text, TailoredCvLanguageText.Bullet(src, from), TailoredCvLanguageText.Bullet(src, to),
                        to == original || src.Translations.Any(t => t.Language == to), $"{path}.bullets[{i}]")!,
                }
                : b with { Text = Keep(b.Text, $"{path}.bullets[{i}]")! }),
        ];

        string? Place(string? current, Locations.Location? location, string path) => Swap(current,
            location is null ? null : TailoredCvDraftValidation.FormatPlace(location, from),
            location is null ? null : TailoredCvDraftValidation.FormatPlace(location, to), location is not null, path);

        // Technologies and skills are names, mostly the same in both languages, so an unmatched one isn't reported.
        static List<string>? Technologies(List<string>? names, IEnumerable<Skills.Skill> entrySkills, Language from,
            Language to)
        {
            if (names is null) return null;
            var byName = entrySkills.GroupBy(s => s.NameIn(from), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().NameIn(to), StringComparer.OrdinalIgnoreCase);
            return [.. names.Select(n => byName.GetValueOrDefault(n) ?? n)];
        }

        var content = source with
        {
            Language = to,
            Summary = Swap(source.Summary, fromTranslation?.Summary ?? profile.Summary,
                translation?.Summary ?? profile.Summary, to == original || translation?.Summary is not null, "summary"),
            Headline = Swap(source.Headline, fromTranslation?.Headline ?? profile.Headline,
                translation?.Headline ?? profile.Headline, to == original || translation?.Headline is not null, "headline"),
            Experiences = source.Experiences?.Select((e, i) =>
            {
                var path = $"experiences[{i}]";
                var src = experiences.GetValueOrDefault(e.SourceEntryId ?? 0);
                return e with
                {
                    Title = Swap(e.Title, src?.TitleIn(from), src?.TitleIn(to),
                        src is not null && (to == original || src.Translations.Any(t => t.Language == to)), $"{path}.title")!,
                    Location = Place(e.Location, src?.Location, $"{path}.location"),
                    Bullets = Bullets(e.Bullets, path),
                    Paragraph = Keep(e.Paragraph, $"{path}.paragraph"),
                    Technologies = Technologies(e.Technologies, src?.Skills ?? [], from, to),
                };
            }).ToList(),
            Educations = source.Educations?.Select((e, i) =>
            {
                var path = $"educations[{i}]";
                var src = educations.GetValueOrDefault(e.SourceEntryId ?? 0);
                var target = src?.Translations.FirstOrDefault(t => t.Language == to);
                return e with
                {
                    Degree = Swap(e.Degree, src?.DegreeIn(from), src?.DegreeIn(to),
                        src is not null && (to == original || target?.Degree is not null), $"{path}.degree"),
                    Field = Swap(e.Field, src?.FieldIn(from), src?.FieldIn(to),
                        src is not null && (to == original || target?.Field is not null), $"{path}.field"),
                    Location = Place(e.Location, src?.Location, $"{path}.location"),
                    Bullets = Bullets(e.Bullets, path),
                    Paragraph = Keep(e.Paragraph, $"{path}.paragraph"),
                };
            }).ToList(),
            Certifications = source.Certifications?.Select((c, i) => c with
            {
                Bullets = Bullets(c.Bullets, $"certifications[{i}]"),
                Paragraph = Keep(c.Paragraph, $"certifications[{i}].paragraph"),
            }).ToList(),
            Projects = source.Projects?.Select((p, i) =>
            {
                var path = $"projects[{i}]";
                var src = projects.GetValueOrDefault(p.SourceEntryId ?? 0);
                return p with
                {
                    Location = Place(p.Location, src?.Location, $"{path}.location"),
                    Bullets = Bullets(p.Bullets, path),
                    Paragraph = Keep(p.Paragraph, $"{path}.paragraph"),
                    Technologies = Technologies(p.Technologies, src?.Skills ?? [], from, to),
                };
            }).ToList(),
            Skills = source.Skills?.Select((s, i) =>
                skills.GetValueOrDefault(s.SourceSkillId ?? 0) is { } skill && s.Name == skill.NameIn(from)
                    ? s with { Name = skill.NameIn(to), Group = Keep(s.Group, $"skills[{i}].group") }
                    : s with { Group = Keep(s.Group, $"skills[{i}].group") }).ToList(),
            Address = Place(source.Address, profile.Location, "address"),
        };
        return new(content, from, untranslated);
    }
}
