using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public record ProfileCvSkill(string? Name = null, int? Level = null);
public record ProfileCvLanguage([property: JsonRequired] Language Language, CefrLevel? Level = null);

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

        Limit(candidate.Links, 10, "links"); Limit(candidate.Hobbies, 20, "hobbies");
        Limit(candidate.Experiences, 100, "experiences"); Limit(candidate.Educations, 100, "educations");
        Limit(candidate.Certifications, 100, "certifications"); Limit(candidate.Projects, 100, "projects");
        Limit(candidate.Skills, 200, "skills"); Limit(candidate.Languages, 2, "languages");
        Limit(candidate.Warnings, 1000, "warnings");

        if (candidate.Warnings.Any(w => string.IsNullOrWhiteSpace(w.Path) || string.IsNullOrWhiteSpace(w.Message)))
            throw new InvalidProfileImportException("warnings: path and message must be nonblank.");
        var warnings = candidate.Warnings;
        void Check(string path, Action validate)
        {
            try { validate(); }
            catch (InvalidProfileException) { warnings.Add(new(path, "Review this value against the profile field rules.")); }
        }
        void Required(string? value, string path) => Check(path, () => ProfileMutations.Required(value, path));
        void Text(string? value, string path, int max = 200) => Check(path, () => ProfileMutations.Text(value, path, max));
        void Date(string? value, string path, bool required = false)
        {
            if (value is null && !required) return;
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Day != 1)
                warnings.Add(new(path, "Supply a known month as YYYY-MM-01; do not invent missing months."));
        }
        void Dates(string? start, string? end, string path, string startField, string endField, bool required = false)
        {
            Date(start, $"{path}.{startField}", required); Date(end, $"{path}.{endField}");
            if (DateOnly.TryParse(start, out var s) && DateOnly.TryParse(end, out var e) && e < s)
                warnings.Add(new($"{path}.{endField}", "End must not precede start."));
        }
        void Location(ProfileCvLocation? location, string path)
        {
            if (location is null) return;
            Text(location.City, path + ".city");
            if (location.CountryCode is null || !LocationLookup.IsCountryCode(location.CountryCode.Trim().ToUpperInvariant()))
                warnings.Add(new(path + ".countryCode", "Supply a valid ISO country code."));
        }
        void Bullets(List<string>? bullets, string path)
        {
            if (bullets is null) return;
            Limit(bullets, 20, path + ".bullets");
            for (var b = 0; b < bullets.Count; b++)
                Check($"{path}.bullets[{b}]", () => ProfileMutations.Required(bullets[b], "Bullet", 500));
        }
        void Tags(List<string>? names, string path)
        {
            if (names is null) return;
            Limit(names, 200, path + ".skillNames");
            for (var n = 0; n < names.Count; n++) Required(names[n], $"{path}.skillNames[{n}]");
        }

        if (candidate.Basics is { } basics)
        {
            Text(basics.FullName, "basics.fullName"); Text(basics.Headline, "basics.headline");
            Text(basics.Summary, "basics.summary", 10_000); Text(basics.Phone, "basics.phone");
            Check("basics.email", () => ProfileMutations.Email(basics.Email)); Location(basics.Location, "basics.location");
        }
        for (var i = 0; i < candidate.Experiences.Count; i++)
        {
            var e = candidate.Experiences[i]; var p = $"experiences[{i}]";
            Required(e.Employer, p + ".employer"); Required(e.Title, p + ".title");
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth", true);
            if (e.EndMonth is null) warnings.Add(new(p + ".endMonth", "Confirm this experience is current before saving a null end month."));
            Location(e.Location, p + ".location"); Tags(e.SkillNames, p); Bullets(e.Bullets, p);
        }
        for (var i = 0; i < candidate.Educations.Count; i++)
        {
            var e = candidate.Educations[i]; var p = $"educations[{i}]";
            Required(e.School, p + ".school"); Text(e.Degree, p + ".degree"); Text(e.Field, p + ".field");
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth", true);
            if (e.EndMonth is null) warnings.Add(new(p + ".endMonth", "Confirm this education is ongoing before saving a null end month."));
            Location(e.Location, p + ".location"); Bullets(e.Bullets, p);
        }
        for (var i = 0; i < candidate.Certifications.Count; i++)
        {
            var e = candidate.Certifications[i]; var p = $"certifications[{i}]";
            Required(e.Name, p + ".name"); Text(e.Issuer, p + ".issuer"); Check(p + ".url", () => ProfileMutations.Url(e.Url));
            Dates(e.IssuedMonth, e.ExpiresMonth, p, "issuedMonth", "expiresMonth"); Bullets(e.Bullets, p);
        }
        for (var i = 0; i < candidate.Projects.Count; i++)
        {
            var e = candidate.Projects[i]; var p = $"projects[{i}]";
            Required(e.Name, p + ".name"); Check(p + ".url", () => ProfileMutations.Url(e.Url));
            Dates(e.StartMonth, e.EndMonth, p, "startMonth", "endMonth");
            Location(e.Location, p + ".location"); Tags(e.SkillNames, p); Bullets(e.Bullets, p);
        }
        for (var i = 0; i < candidate.Skills.Count; i++)
        {
            Required(candidate.Skills[i].Name, $"skills[{i}].name");
            if (candidate.Skills[i].Level is not (>= 1 and <= 5)) warnings.Add(new($"skills[{i}].level", "Choose a reviewed level from 1 to 5."));
        }
        for (var i = 0; i < candidate.Languages.Count; i++)
            if (candidate.Languages[i].Level is null) warnings.Add(new($"languages[{i}].level", "Choose a reviewed CEFR level."));
        for (var i = 0; i < candidate.Hobbies.Count; i++) Text(candidate.Hobbies[i], $"hobbies[{i}]", 100);
        Check("links", () => ProfileMutations.ValidateLinks(candidate.Links.Select(l => new ProfileLinkInput(l.Kind, l.Label, l.Url ?? "")).ToList()));
        return candidate;
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
