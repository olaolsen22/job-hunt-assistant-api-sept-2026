using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Locations;

namespace JobHunt.Api.Features.Profiles;

public class InvalidProfileImportException(string message) : Exception(message);
public record ProfileCvWarning(string Path, string Message);

public record ProfileCvLocation(string? City, string? CountryCode);
public record ProfileCvLink([property: JsonRequired] LinkKind Kind, string? Label, string? Url);

public record ProfileCvBasics(string? FullName = null, string? Headline = null, string? Summary = null,
    string? Email = null, string? Phone = null, ProfileCvLocation? Location = null);
public record ProfileCvExperience(string? Employer = null, string? Title = null, ProfileCvLocation? Location = null,
    string? StartMonth = null, string? EndMonth = null, List<string>? SkillNames = null, List<string>? Bullets = null);
public record ProfileCvEducation(string? School = null, string? Degree = null, string? Field = null,
    ProfileCvLocation? Location = null, string? StartMonth = null, string? EndMonth = null, List<string>? Bullets = null);
public record ProfileCvCertification(string? Name = null, string? Issuer = null, string? IssuedMonth = null,
    string? ExpiresMonth = null, string? Url = null, List<string>? Bullets = null);
public record ProfileCvProject(string? Name = null, string? Url = null, ProfileCvLocation? Location = null,
    string? StartMonth = null, string? EndMonth = null, List<string>? SkillNames = null, List<string>? Bullets = null);
// CategoryName is a suggestion by name: it only fills an uncategorized skill, and an unknown name is ignored.
public record ProfileCvSkill(string? Name = null, int? Level = null, string? CategoryName = null);
public record ProfileCvLanguage(string? Name = null, LanguageProficiency? Written = null, LanguageProficiency? Spoken = null);

public class ProfileCvCandidate
{
    public required int SchemaVersion { get; init; }
    public required Language ContentLanguage { get; init; }
    public ProfileCvBasics? Basics { get; init; }
    public List<ProfileCvLink> Links { get; init; } = [];
    public List<string> Hobbies { get; init; } = [];
    public List<ProfileCvExperience> Experiences { get; init; } = [];
    public List<ProfileCvEducation> Educations { get; init; } = [];
    public List<ProfileCvCertification> Certifications { get; init; } = [];
    public List<ProfileCvProject> Projects { get; init; } = [];
    public List<ProfileCvSkill> Skills { get; init; } = [];
    public List<ProfileCvLanguage> Languages { get; init; } = [];
    public List<ProfileCvWarning> Warnings { get; init; } = [];
}

// Both AI paths use this decoder. Candidates can be incomplete; selected saves cannot.
public static class ProfileCvDecoder
{
    public const int MaxJsonLength = 200_000;
    private const int MaxHistoryEntries = 100;
    private const int MaxSkills = 200;
    private const int MaxLanguages = 20;
    private const int MaxSkillTags = 200;
    private const int MaxWarnings = 1000;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
    };

    public static ProfileCvCandidate Decode(string json)
    {
        if (json.Length > MaxJsonLength)
            throw new InvalidProfileImportException($"json: maximum length is {MaxJsonLength} characters.");
        ProfileCvCandidate candidate;
        try
        {
            using var document = JsonDocument.Parse(json);
            CheckStructure(document.RootElement, "$");
            candidate = JsonSerializer.Deserialize<ProfileCvCandidate>(json, Options)
                ?? throw new InvalidProfileImportException("json: expected a candidate object.");
        }
        catch (JsonException ex)
        {
            // Do not expose parser messages containing pasted CV data.
            throw new InvalidProfileImportException($"{ex.Path ?? "json"}: invalid candidate JSON or value type.");
        }
        if (candidate.SchemaVersion != 1 || candidate.ContentLanguage != Language.English)
            throw new InvalidProfileImportException("schemaVersion/contentLanguage: expected version 1 and ENGLISH.");

        Limit(candidate.Links, ProfileMutations.MaxLinks, "links");
        Limit(candidate.Hobbies, ProfileMutations.MaxHobbies, "hobbies");
        Limit(candidate.Experiences, MaxHistoryEntries, "experiences");
        Limit(candidate.Educations, MaxHistoryEntries, "educations");
        Limit(candidate.Certifications, MaxHistoryEntries, "certifications");
        Limit(candidate.Projects, MaxHistoryEntries, "projects");
        Limit(candidate.Skills, MaxSkills, "skills");
        Limit(candidate.Languages, MaxLanguages, "languages");
        Limit(candidate.Warnings, MaxWarnings, "warnings");

        if (candidate.Warnings.Any(w => string.IsNullOrWhiteSpace(w.Path) || string.IsNullOrWhiteSpace(w.Message)))
            throw new InvalidProfileImportException("warnings: path and message must be nonblank.");
        new FieldReview(candidate.Warnings).Review(candidate);
        return candidate;
    }

    // Candidates can be incomplete, so a value the profile would reject becomes a warning to review, not an error.
    private sealed class FieldReview(List<ProfileCvWarning> warnings)
    {
        public void Review(ProfileCvCandidate candidate)
        {
            if (candidate.Basics is { } basics) Basics(basics);
            for (var i = 0; i < candidate.Experiences.Count; i++) Experience(candidate.Experiences[i], $"experiences[{i}]");
            for (var i = 0; i < candidate.Educations.Count; i++) Education(candidate.Educations[i], $"educations[{i}]");
            for (var i = 0; i < candidate.Certifications.Count; i++) Certification(candidate.Certifications[i], $"certifications[{i}]");
            for (var i = 0; i < candidate.Projects.Count; i++) Project(candidate.Projects[i], $"projects[{i}]");
            for (var i = 0; i < candidate.Skills.Count; i++)
            {
                Required(candidate.Skills[i].Name, $"skills[{i}].name");
                if (candidate.Skills[i].Level is not (>= 1 and <= 5))
                    warnings.Add(new($"skills[{i}].level", "Choose a reviewed level from 1 to 5."));
            }
            for (var i = 0; i < candidate.Languages.Count; i++) LanguageEntry(candidate.Languages[i], $"languages[{i}]");
            for (var i = 0; i < candidate.Hobbies.Count; i++) Text(candidate.Hobbies[i], $"hobbies[{i}]", ProfileMutations.MaxHobbyLength);
            Check("links", () => ProfileMutations.ValidateLinks(
                [.. candidate.Links.Select(l => new ProfileLinkInput(l.Kind, l.Label, l.Url ?? ""))]));
        }

        private void Basics(ProfileCvBasics basics)
        {
            Text(basics.FullName, "basics.fullName");
            Text(basics.Headline, "basics.headline");
            Text(basics.Summary, "basics.summary", Inputs.MaxMarkdownLength);
            Text(basics.Phone, "basics.phone");
            Check("basics.email", () => ProfileMutations.Email(basics.Email));
            Location(basics.Location, "basics.location");
        }

        private void Experience(ProfileCvExperience e, string p)
        {
            Required(e.Employer, p + ".employer");
            Required(e.Title, p + ".title");
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth", true);
            if (e.EndMonth is null) warnings.Add(new(p + ".endMonth", "Confirm this experience is current before saving a null end month."));
            Location(e.Location, p + ".location");
            Tags(e.SkillNames, p);
            Bullets(e.Bullets, p);
        }

        private void Education(ProfileCvEducation e, string p)
        {
            Required(e.School, p + ".school");
            Text(e.Degree, p + ".degree");
            Text(e.Field, p + ".field");
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth", true);
            if (e.EndMonth is null) warnings.Add(new(p + ".endMonth", "Confirm this education is ongoing before saving a null end month."));
            Location(e.Location, p + ".location");
            Bullets(e.Bullets, p);
        }

        private void Certification(ProfileCvCertification e, string p)
        {
            Required(e.Name, p + ".name");
            Text(e.Issuer, p + ".issuer");
            Check(p + ".url", () => ProfileMutations.Url(e.Url));
            Dates(e.IssuedMonth, e.ExpiresMonth, p, "issuedMonth", "expiresMonth");
            Bullets(e.Bullets, p);
        }

        private void Project(ProfileCvProject e, string p)
        {
            Required(e.Name, p + ".name");
            Check(p + ".url", () => ProfileMutations.Url(e.Url));
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth");
            Location(e.Location, p + ".location");
            Tags(e.SkillNames, p);
            Bullets(e.Bullets, p);
        }

        private void LanguageEntry(ProfileCvLanguage l, string p)
        {
            Required(l.Name, p + ".name");
            Text(l.Name, p + ".name", ProfileLanguage.MaxNameLength);
            if (l.Written is null) warnings.Add(new(p + ".written", "Choose a reviewed written level."));
            if (l.Spoken is null) warnings.Add(new(p + ".spoken", "Choose a reviewed spoken level."));
        }

        private void Check(string path, Action validate)
        {
            try { validate(); }
            catch (InvalidProfileException) { warnings.Add(new(path, "Review this value against the profile field rules.")); }
        }

        private void Required(string? value, string path) => Check(path, () => ProfileMutations.Required(value, path));

        private void Text(string? value, string path, int max = ProfileMutations.MaxTextLength) =>
            Check(path, () => ProfileMutations.Text(value, path, max));

        private void Date(string? value, string path, bool required = false)
        {
            if (value is null && !required) return;
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Day != 1)
                warnings.Add(new(path, "Supply a known month as YYYY-MM-01; do not invent missing months."));
        }

        private void Dates(string? start, string? end, string path, string startField, string endField, bool required = false)
        {
            Date(start, $"{path}.{startField}", required);
            Date(end, $"{path}.{endField}");
            if (DateOnly.TryParse(start, out var s) && DateOnly.TryParse(end, out var e) && e < s)
                warnings.Add(new($"{path}.{endField}", "End must not precede start."));
        }

        private void Location(ProfileCvLocation? location, string path)
        {
            if (location is null) return;
            Text(location.City, path + ".city");
            if (location.CountryCode is null || !LocationLookup.IsCountryCode(location.CountryCode.Trim().ToUpperInvariant()))
                warnings.Add(new(path + ".countryCode", "Supply a valid ISO country code."));
        }

        private void Bullets(List<string>? bullets, string path)
        {
            if (bullets is null) return;
            Limit(bullets, ProfileBulletMutations.MaxPerEntry, path + ".bullets");
            for (var b = 0; b < bullets.Count; b++)
                Check($"{path}.bullets[{b}]", () => ProfileMutations.Required(bullets[b], "Bullet", ProfileBulletMutations.MaxTextLength));
        }

        private void Tags(List<string>? names, string path)
        {
            if (names is null) return;
            Limit(names, MaxSkillTags, path + ".skillNames");
            for (var n = 0; n < names.Count; n++) Required(names[n], $"{path}.skillNames[{n}]");
        }
    }

    private static void Limit<T>(List<T>? items, int max, string path)
    {
        if (items is null || items.Count > max)
            throw new InvalidProfileImportException($"{path}: expected a list of at most {max} items.");
    }

    private static void CheckStructure(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidProfileImportException($"{path}: repeated property.");
                CheckStructure(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new InvalidProfileImportException($"{path}[{i}]: null item.");
                CheckStructure(item, $"{path}[{i++}]");
            }
        }
    }
}
