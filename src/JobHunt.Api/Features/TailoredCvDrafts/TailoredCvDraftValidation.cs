using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
using Location = JobHunt.Api.Features.Locations.Location;

namespace JobHunt.Api.Features.TailoredCvDrafts;

public static class TailoredCvDraftValidation
{
    public const int MaxJsonLength = 200_000;
    // The PDF fits ~95 characters per line (11pt Source Sans 3 over 17 cm), so this keeps the summary to 3 lines.
    public const int MaxSummaryLength = 300;
    private const int MaxEntriesPerSection = 100;
    private const int MaxBulletsPerEntry = 20;
    public const int MaxBulletLength = 500;
    internal const int MaxTextLength = 200;
    private const int MaxSkills = 200;
    private const int MaxLanguages = 20;
    private const int MaxAddressLength = 300;
    private const int MaxLinks = 10;

    public static TailoredCvContent Normalize(TailoredCvContentInput input, Language? postingLanguage)
    {
        if (input is null) throw new InvalidTailoredCvDraftException("content is required.");
        var content = new TailoredCvContent(input.Summary?.Trim(),
            input.Experiences?.Select(e => new TailoredCvExperience(CleanRequired(e.Employer), CleanRequired(e.Title), e.StartMonth,
                e.EndMonth, (e.Bullets ?? []).Select(b => new TailoredCvBullet(CleanRequired(b.Text), b.SourceBulletId)).ToList(), e.SourceEntryId,
                Inputs.Clean(e.Location), Inputs.Clean(e.Paragraph), CleanNames(e.Technologies))).ToList(),
            input.Educations?.Select(e => new TailoredCvEducation(CleanRequired(e.School), Inputs.Clean(e.Degree), Inputs.Clean(e.Field),
                e.StartMonth, e.EndMonth, (e.Bullets ?? []).Select(b => new TailoredCvBullet(CleanRequired(b.Text), b.SourceBulletId)).ToList(), e.SourceEntryId,
                Inputs.Clean(e.Location), Inputs.Clean(e.Paragraph))).ToList(),
            input.Certifications?.Select(e => new TailoredCvCertification(CleanRequired(e.Name), Inputs.Clean(e.Issuer), e.IssuedMonth,
                e.ExpiresMonth, Inputs.Clean(e.Url), (e.Bullets ?? []).Select(b => new TailoredCvBullet(CleanRequired(b.Text), b.SourceBulletId)).ToList(), e.SourceEntryId,
                Inputs.Clean(e.Paragraph))).ToList(),
            input.Projects?.Select(e => new TailoredCvProject(CleanRequired(e.Name), Inputs.Clean(e.Url), e.StartMonth, e.EndMonth,
                (e.Bullets ?? []).Select(b => new TailoredCvBullet(CleanRequired(b.Text), b.SourceBulletId)).ToList(), e.SourceEntryId,
                Inputs.Clean(e.Location), Inputs.Clean(e.Paragraph), e.Kind, CleanNames(e.Technologies))).ToList(),
            input.Skills?.Select(s => new TailoredCvSkill(CleanRequired(s.Name), s.Level, s.SourceSkillId, s.CategoryId,
                Inputs.Clean(s.Group))).ToList(),
            input.Languages?.Select(l => new TailoredCvLanguage(CleanRequired(l.Name), l.Written, l.Spoken)).ToList(),
            Inputs.Clean(input.FullName), Inputs.Clean(input.Headline), Inputs.Clean(input.Email), Inputs.Clean(input.Phone), Inputs.Clean(input.Address),
            input.Links?.Select(l => new TailoredCvLink(CleanRequired(l.Label), CleanRequired(l.Url))).ToList(),
            input.Language ?? postingLanguage ?? Language.English, input.DateOfBirth, input.ReferencesOnRequest,
            input.SectionOrder ?? TailoredCvSectionOrder.Standard,
            input.ContentStyle ?? TailoredCvContentStyle.Bullets,
            input.Template ?? TailoredCvTemplate.Detailed);

        Validate(content);
        if (JsonSerializer.Serialize(content, TailoredCvJson.Options).Length > MaxJsonLength)
            throw new InvalidTailoredCvDraftException($"Draft content must be at most {MaxJsonLength} characters.");
        return content;
    }

    public static void ValidateExternalCandidateItems(TailoredCvContentInput input)
    {
        ValidateItems(input.Experiences, "content.experiences");
        ValidateItems(input.Educations, "content.educations");
        ValidateItems(input.Certifications, "content.certifications");
        ValidateItems(input.Projects, "content.projects");
        ValidateItems(input.Skills, "content.skills");
        ValidateItems(input.Languages, "content.languages");
        ValidateItems(input.Links, "content.links");

        ValidateBullets(input.Experiences, "content.experiences");
        ValidateBullets(input.Educations, "content.educations");
        ValidateBullets(input.Certifications, "content.certifications");
        ValidateBullets(input.Projects, "content.projects");
    }

    private static void ValidateItems<T>(IReadOnlyList<T>? items, string path) where T : class
    {
        if (items is null) return;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is null)
                throw Invalid($"{path}[{i}]", "must not be null");
        }
    }

    private static void ValidateBullets<TEntry>(IReadOnlyList<TEntry>? entries, string path)
        where TEntry : class
    {
        if (entries is null) return;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry is null) continue; // ValidateItems reports this with the section path.
            var bullets = entry switch
            {
                TailoredCvExperienceInput value => value.Bullets,
                TailoredCvEducationInput value => value.Bullets,
                TailoredCvCertificationInput value => value.Bullets,
                TailoredCvProjectInput value => value.Bullets,
                _ => throw new InvalidOperationException("Unsupported tailored CV entry type."),
            };
            ValidateItems(bullets, $"{path}[{i}].bullets");
        }
    }

    public static async Task ValidateSourceIdsAsync(TailoredCvContent content, AppDbContext db,
        CancellationToken ct, bool requireSourceBulletText = false)
    {
        var experienceIds = content.Experiences?.Where(e => e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).Distinct().ToArray() ?? [];
        var educationIds = content.Educations?.Where(e => e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).Distinct().ToArray() ?? [];
        var certificationIds = content.Certifications?.Where(e => e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).Distinct().ToArray() ?? [];
        var projectIds = content.Projects?.Where(e => e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).Distinct().ToArray() ?? [];
        var skillIds = content.Skills?.Where(s => s.SourceSkillId is not null)
            .Select(s => s.SourceSkillId!.Value).Distinct().ToArray() ?? [];

        if (experienceIds.Length > 0 && await db.ProfileExperiences.CountAsync(e => experienceIds.Contains(e.Id), ct) != experienceIds.Length)
            throw InvalidSource("experiences.sourceEntryId");
        if (educationIds.Length > 0 && await db.ProfileEducations.CountAsync(e => educationIds.Contains(e.Id), ct) != educationIds.Length)
            throw InvalidSource("educations.sourceEntryId");
        if (certificationIds.Length > 0 && await db.ProfileCertifications.CountAsync(e => certificationIds.Contains(e.Id), ct) != certificationIds.Length)
            throw InvalidSource("certifications.sourceEntryId");
        if (projectIds.Length > 0 && await db.ProfileProjects.CountAsync(e => projectIds.Contains(e.Id), ct) != projectIds.Length)
            throw InvalidSource("projects.sourceEntryId");
        if (skillIds.Length > 0 && await db.ProfileSkills.CountAsync(s => skillIds.Contains(s.SkillId), ct) != skillIds.Length)
            throw InvalidSource("skills.sourceSkillId");
        var categoryIds = content.Skills?.Where(s => s.CategoryId is not null)
            .Select(s => s.CategoryId!.Value).Distinct().ToArray() ?? [];
        if (categoryIds.Length > 0 && await db.SkillCategories.CountAsync(c => categoryIds.Contains(c.Id), ct) != categoryIds.Length)
            throw InvalidSource("skills.categoryId");

        var bulletReferences = new List<(int Id, int? EntryId, string Kind, string Text)>();
        bulletReferences.AddRange(content.Experiences?.SelectMany(e => e.Bullets
            .Where(b => b.SourceBulletId is not null).Select(b => (b.SourceBulletId!.Value, e.SourceEntryId, "experience", b.Text))) ?? []);
        bulletReferences.AddRange(content.Educations?.SelectMany(e => e.Bullets
            .Where(b => b.SourceBulletId is not null).Select(b => (b.SourceBulletId!.Value, e.SourceEntryId, "education", b.Text))) ?? []);
        bulletReferences.AddRange(content.Certifications?.SelectMany(e => e.Bullets
            .Where(b => b.SourceBulletId is not null).Select(b => (b.SourceBulletId!.Value, e.SourceEntryId, "certification", b.Text))) ?? []);
        bulletReferences.AddRange(content.Projects?.SelectMany(e => e.Bullets
            .Where(b => b.SourceBulletId is not null).Select(b => (b.SourceBulletId!.Value, e.SourceEntryId, "project", b.Text))) ?? []);
        var bulletIds = bulletReferences.Select(b => b.Id).Distinct().ToArray();
        if (bulletIds.Length == 0) return;
        var bullets = await db.ProfileBullets.Where(b => bulletIds.Contains(b.Id))
            .Select(b => new { b.Id, EntryId = b.ExperienceId ?? b.EducationId ?? b.CertificationId ?? b.ProjectId,
                Kind = b.ExperienceId != null ? "experience" : b.EducationId != null ? "education"
                    : b.CertificationId != null ? "certification" : "project", b.Text,
                Translated = b.Translations.Where(t => t.Language == content.Language).Select(t => t.Text).FirstOrDefault() })
            .ToDictionaryAsync(b => b.Id, ct);
        foreach (var reference in bulletReferences)
        {
            if (!bullets.TryGetValue(reference.Id, out var bullet))
                throw InvalidSource("bullets.sourceBulletId");
            if (bullet.Kind != reference.Kind || reference.EntryId is { } entryId && entryId != bullet.EntryId)
                throw InvalidSource("bullets.sourceBulletId");
            // The external prompt supplies the translation in the draft language when one exists.
            if (requireSourceBulletText && !string.Equals(reference.Text.Trim(), bullet.Text.Trim(), StringComparison.Ordinal)
                && !string.Equals(reference.Text.Trim(), bullet.Translated?.Trim(), StringComparison.Ordinal))
                throw Invalid("bullets.sourceBulletId", "text must match the source bullet");
        }
    }

    // The model or UI often leaves "location" out; entries linked to a profile entry fall back to its stored place.
    public static async Task<TailoredCvContent> FillMissingLocationsAsync(TailoredCvContent content, AppDbContext db,
        CancellationToken ct)
    {
        var experienceIds = (content.Experiences ?? []).Where(e => e.Location is null && e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).ToArray();
        var educationIds = (content.Educations ?? []).Where(e => e.Location is null && e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).ToArray();
        var projectIds = (content.Projects ?? []).Where(e => e.Location is null && e.SourceEntryId is not null)
            .Select(e => e.SourceEntryId!.Value).ToArray();

        var experiences = experienceIds.Length == 0 ? [] : await db.ProfileExperiences.Where(e => experienceIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Location }).ToDictionaryAsync(e => e.Id, e => e.Location, ct);
        var educations = educationIds.Length == 0 ? [] : await db.ProfileEducations.Where(e => educationIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Location }).ToDictionaryAsync(e => e.Id, e => e.Location, ct);
        var projects = projectIds.Length == 0 ? [] : await db.ProfileProjects.Where(e => projectIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Location }).ToDictionaryAsync(e => e.Id, e => e.Location, ct);

        string? Place(Dictionary<int, Location?> places, int? id) =>
            id is { } key && places.GetValueOrDefault(key) is { } l ? FormatPlace(l, content.Language) : null;

        return content with
        {
            Experiences = content.Experiences?.Select(e => e.Location is null ? e with { Location = Place(experiences, e.SourceEntryId) } : e).ToList(),
            Educations = content.Educations?.Select(e => e.Location is null ? e with { Location = Place(educations, e.SourceEntryId) } : e).ToList(),
            Projects = content.Projects?.Select(e => e.Location is null ? e with { Location = Place(projects, e.SourceEntryId) } : e).ToList(),
        };
    }

    // Only for the external preview: the model may leave out details it was told to copy. On save, null is the
    // user's choice and stays.
    public static async Task<TailoredCvContent> FillMissingSourceDetailsAsync(TailoredCvContent content,
        AppDbContext db, CancellationToken ct)
    {
        if (!(content.Experiences ?? []).Any(e => e.SourceEntryId is not null && e.Technologies is null)
            && !(content.Projects ?? []).Any(p => p.SourceEntryId is not null && (p.Kind is null || p.Technologies is null)))
            return content;
        var profile = await ProfileQueries.LoadAsync(db, ct);
        var experiences = profile.Experiences.ToDictionary(e => e.Id);
        var projects = profile.Projects.ToDictionary(p => p.Id);
        List<string> Technologies(List<Skill> skills) =>
            TailoredCvLanguageText.Technologies(skills, profile, content.Language);
        return content with
        {
            Experiences = content.Experiences?.Select(e => e.Technologies is null && e.SourceEntryId is { } id
                && experiences.GetValueOrDefault(id) is { } source ? e with { Technologies = Technologies(source.Skills) } : e).ToList(),
            Projects = content.Projects?.Select(p => p.SourceEntryId is { } id && projects.GetValueOrDefault(id) is { } source
                ? p with { Kind = p.Kind ?? source.Kind, Technologies = p.Technologies ?? Technologies(source.Skills) } : p).ToList(),
        };
    }

    // "City, Country" per the CV guidance (docs/norwegian-cv-formatting.md).
    internal static string FormatPlace(Location location, Language language)
    {
        var country = location.CountryCode == "NO" && language == Language.Norwegian
            ? "Norge"
            : new RegionInfo(location.CountryCode).EnglishName;
        return string.IsNullOrWhiteSpace(location.City) ? country : $"{location.City}, {country}";
    }

    private static void Validate(TailoredCvContent c)
    {
        Text(c.FullName, "fullName", MaxTextLength); Text(c.Headline, "headline", MaxTextLength);
        Text(c.Email, "email", MaxTextLength); Text(c.Phone, "phone", MaxTextLength);
        Text(c.Address, "address", MaxAddressLength);
        if (c.DateOfBirth is { } born && born.Year < 1900)
            throw Invalid("dateOfBirth", "must be after 1900");
        if (c.Summary?.Length > MaxSummaryLength) throw Invalid("summary", $"must be at most {MaxSummaryLength} characters");
        Limit(c.Experiences, MaxEntriesPerSection, "experiences");
        Limit(c.Educations, MaxEntriesPerSection, "educations");
        Limit(c.Certifications, MaxEntriesPerSection, "certifications");
        Limit(c.Projects, MaxEntriesPerSection, "projects");
        Limit(c.Skills, MaxSkills, "skills"); Limit(c.Languages, MaxLanguages, "languages"); Limit(c.Links, MaxLinks, "links");
        if (c.Experiences?.Any(e => Blank(e.Employer) || Blank(e.Title)) == true)
            throw Invalid("experiences", "employer and title are required");
        if (c.Educations?.Any(e => Blank(e.School)) == true) throw Invalid("educations", "school is required");
        if (c.Certifications?.Any(e => Blank(e.Name)) == true) throw Invalid("certifications", "name is required");
        if (c.Projects?.Any(e => Blank(e.Name)) == true) throw Invalid("projects", "name is required");
        if (c.Skills?.Any(s => Blank(s.Name) || s.Level is < 1 or > 5) == true)
            throw Invalid("skills", "name is required and level must be 1–5");
        // A label prints in front of every line of the section, so it stays short like a category name.
        if (c.Skills?.Any(s => s.Group?.Length > TailoredCvSkillGroupSuggester.MaxGroupLength) == true)
            throw Invalid("skills.group", $"must be at most {TailoredCvSkillGroupSuggester.MaxGroupLength} characters");
        ValidateFields(c.Experiences?.SelectMany(e => new[] { e.Employer, e.Title, e.Location }), "experiences");
        ValidateFields(c.Educations?.SelectMany(e => new[] { e.School, e.Degree, e.Field, e.Location }), "educations");
        ValidateFields(c.Certifications?.SelectMany(e => new[] { e.Name, e.Issuer }), "certifications");
        ValidateFields(c.Projects?.SelectMany(e => new[] { e.Name, e.Location }), "projects");
        // Same cap as the skills list: a prefill can hold every skill tagged on an entry.
        foreach (var e in c.Experiences ?? []) Limit(e.Technologies, MaxSkills, "experiences.technologies");
        foreach (var e in c.Projects ?? []) Limit(e.Technologies, MaxSkills, "projects.technologies");
        ValidateFields(c.Experiences?.SelectMany(e => e.Technologies ?? []), "experiences.technologies");
        ValidateFields(c.Projects?.SelectMany(e => e.Technologies ?? []), "projects.technologies");
        ValidateFields(c.Skills?.Select(s => s.Name), "skills");
        if (c.Languages?.Any(l => Blank(l.Name) || l.Name.Length > ProfileLanguage.MaxNameLength) == true)
            throw Invalid("languages", $"name is required and at most {ProfileLanguage.MaxNameLength} characters");
        if (c.Languages?.Select(l => l.Name.ToUpperInvariant()).Distinct().Count() != c.Languages?.Count)
            throw Invalid("languages", "entries must be unique");
        if (c.Links?.Any(l => Blank(l.Label) || l.Label.Length > 50 || !Inputs.TryHttpUrl(l.Url, out _)) == true)
            throw Invalid("links", $"each link needs a label (at most 50 characters) and an HTTP(S) URL of at most {Inputs.MaxUrlLength} characters");
        if (c.Email is { } email)
        {
            try { _ = new MailAddress(email); }
            catch (FormatException) { throw Invalid("email", "must be a valid email address"); }
        }
        Paragraphs(c.Experiences?.Select(e => e.Paragraph), "experiences");
        Paragraphs(c.Educations?.Select(e => e.Paragraph), "educations");
        Paragraphs(c.Certifications?.Select(e => e.Paragraph), "certifications");
        Paragraphs(c.Projects?.Select(e => e.Paragraph), "projects");
        foreach (var e in c.Experiences ?? []) ValidateMonths(e.StartMonth, e.EndMonth, "experiences");
        foreach (var e in c.Educations ?? []) ValidateMonths(e.StartMonth, e.EndMonth, "educations");
        foreach (var e in c.Certifications ?? []) ValidateMonths(e.IssuedMonth, e.ExpiresMonth, "certifications");
        foreach (var e in c.Projects ?? []) ValidateMonths(e.StartMonth, e.EndMonth, "projects");
        ValidateBullets(c.Experiences?.Select(e => e.Bullets), "experiences");
        ValidateBullets(c.Educations?.Select(e => e.Bullets), "educations");
        ValidateBullets(c.Certifications?.Select(e => e.Bullets), "certifications");
        ValidateBullets(c.Projects?.Select(e => e.Bullets), "projects");
    }

    private static void ValidateBullets(IEnumerable<List<TailoredCvBullet>>? groups, string path)
    {
        if (groups is null) return;
        foreach (var bullets in groups)
        {
            Limit(bullets, MaxBulletsPerEntry, path + ".bullets");
            if (bullets.Any(b => Blank(b.Text) || b.Text.Length > MaxBulletLength))
                throw Invalid(path + ".bullets", $"text must be nonblank and at most {MaxBulletLength} characters");
        }
    }

    private static void ValidateFields(IEnumerable<string?>? fields, string path)
    {
        if (fields?.Any(value => value?.Length > MaxTextLength) == true)
            throw Invalid(path, $"text fields must be at most {MaxTextLength} characters");
    }

    private static void ValidateMonths(DateOnly? start, DateOnly? end, string path)
    {
        if (start is { Day: not 1 } || end is { Day: not 1 })
            throw Invalid(path, "dates must use the first day of a month");
        if (start is { } first && end is { } last && last < first)
            throw Invalid(path, "end month must not precede start month");
    }

    private static void Paragraphs(IEnumerable<string?>? paragraphs, string path)
    {
        if (paragraphs?.Any(p => p?.Length > 10_000) == true)
            throw Invalid(path, "paragraphs must be at most 10000 characters");
    }

    private static void Text(string? value, string path, int max)
    {
        if (value?.Length > max) throw Invalid(path, $"must be at most {max} characters");
    }

    private static void Limit<T>(IReadOnlyCollection<T>? values, int max, string path)
    {
        if (values?.Count > max) throw Invalid(path, $"must contain at most {max} items");
    }

    private static string CleanRequired(string? value) => value?.Trim() ?? "";
    private static List<string>? CleanNames(List<string>? values) => values?.Select(v => v?.Trim())
        .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static InvalidTailoredCvDraftException Invalid(string path, string message) =>
        new($"{path}: {message}.");
    private static InvalidTailoredCvDraftException InvalidSource(string path) =>
        new($"{path}: source item was not found in this profile.");
}
