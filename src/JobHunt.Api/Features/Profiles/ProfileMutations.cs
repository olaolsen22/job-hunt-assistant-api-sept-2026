using System.Globalization;
using System.Net.Mail;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

public record ProfileLinkInput(LinkKind Kind, string? Label, string Url);

public class InvalidProfileException(string message) : Exception(message);

// One error for every kind of profile entry: the UI handles them all the same way.
public class ProfileEntryNotFoundException(string kind, object id) : Exception($"{kind} {id} was not found.");

[MutationType]
public static class ProfileMutations
{
    internal const int MaxTextLength = 200;
    private const int MaxLinks = 10;
    private const int MaxLinkLabelLength = 50;
    private const int MaxHobbies = 20;
    private const int MaxHobbyLength = 100;

    // Framework region data (ICU), like country codes, so there's no currency list to maintain.
    private static readonly HashSet<string> Currencies = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).ISOCurrencySymbol)
        .ToHashSet();

    // Full replace of the basics: a field left out is cleared.
    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<Profile> UpdateProfileAsync(
        string? fullName, string? headline, string? summary, string? email, string? phone, LocationInput? location,
        DateOnly? dateOfBirth, string? nationality, string? workAuthorization, List<string> hobbies,
        List<ProfileLinkInput> links, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        profile.FullName = Text(fullName, "Full name");
        profile.Headline = Text(headline, "Headline");
        profile.Summary = Markdown(summary, "Summary");
        profile.Phone = Text(phone, "Phone");
        profile.Email = Email(email);

        if (dateOfBirth > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
            throw new InvalidProfileException("The date of birth can't be in the future.");
        profile.DateOfBirth = dateOfBirth;
        profile.Nationality = Text(nationality, "Nationality");
        profile.WorkAuthorization = Text(workAuthorization, "Work authorization");

        profile.Hobbies = ValidateHobbies(hobbies);

        profile.Links = ValidateLinks(links);

        if (location is null)
            profile.LocationId = null;
        else
            profile.Location = await LocationLookup.FindOrCreateAsync(location, db, ct);

        await db.SaveChangesAsync(ct);
        return await ProfileQueries.LoadAsync(db, ct);
    }

    internal static List<string> ValidateHobbies(List<string> hobbies)
    {
        var values = hobbies.Select(h => Text(h, "Hobby", MaxHobbyLength)).OfType<string>()
            .DistinctBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Count > MaxHobbies)
            throw new InvalidProfileException($"A profile can have at most {MaxHobbies} hobbies.");
        return values;
    }

    internal static List<ProfileLink> ValidateLinks(List<ProfileLinkInput> links)
    {
        if (links.Count > MaxLinks)
            throw new InvalidProfileException($"A profile can have at most {MaxLinks} links.");
        if (links.Where(l => l.Kind != LinkKind.Other).GroupBy(l => l.Kind).Any(g => g.Count() > 1))
            throw new InvalidProfileException("A profile can have only one link of each kind except Other.");
        return [.. links.Select(ToLink)];
    }

    private static ProfileLink ToLink(ProfileLinkInput input)
    {
        var url = Url(Required(input.Url, "Link URL"))!;
        var domain = input.Kind switch { LinkKind.Github => "github.com", LinkKind.Linkedin => "linkedin.com", _ => null };
        // Uri.Host is lower case already.
        if (domain is not null && new Uri(url).Host is var host && host != domain && !host.EndsWith("." + domain))
            throw new InvalidProfileException($"A {input.Kind} link must be on {domain}.");
        return new ProfileLink
        {
            Kind = input.Kind,
            Label = input.Kind == LinkKind.Other ? Required(input.Label, "Link label", MaxLinkLabelLength) : "",
            Url = url,
        };
    }

    // Separate from updateProfile so existing callers don't clear it. Both fields are replaced; null clears.
    [Error<InvalidProfileException>]
    public static async Task<Profile> UpdateProfileAddressAsync(
        string? streetAddress, string? postalCode, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        profile.StreetAddress = Text(streetAddress, "Street address", 100);
        profile.PostalCode = Text(postalCode, "Postal code", 20);
        await db.SaveChangesAsync(ct);
        return await ProfileQueries.LoadAsync(db, ct);
    }

    // Full replace of the preferences. Target locations aren't here: they're Location.IsTarget, set with saveLocation.
    [Error<InvalidProfileException>]
    public static async Task<Profile> UpdateProfilePreferencesAsync(
        List<string> jobRoleNames, List<WorkMode> workModes, List<EmploymentType> employmentTypes,
        int? minSalary, string? salaryCurrency, int? noticePeriodMonths, AppDbContext db, CancellationToken ct)
    {
        if (minSalary < 0)
            throw new InvalidProfileException("The minimum salary can't be negative.");
        var currency = Clean(salaryCurrency)?.ToUpperInvariant();
        if (currency is not null && !Currencies.Contains(currency))
            throw new InvalidProfileException($"\"{salaryCurrency}\" is not an ISO 4217 currency code, e.g. NOK.");
        if (minSalary is not null && currency is null)
            throw new InvalidProfileException("A minimum salary needs a currency.");
        if (noticePeriodMonths is < 0 or > 12)
            throw new InvalidProfileException("The notice period must be 0–12 months.");
        if (jobRoleNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidProfileException("Job role name is required.");

        var profile = await db.Profiles
            .Include(p => p.JobRoles).Include(p => p.WorkModes).Include(p => p.EmploymentTypes)
            .SingleAsync(ct);
        profile.MinSalary = minSalary;
        profile.SalaryCurrency = currency;
        profile.NoticePeriodMonths = noticePeriodMonths;

        var roleNames = jobRoleNames.DistinctBy(Names.Normalize).ToList();
        var normalized = roleNames.Select(Names.Normalize).ToList();
        var roles = await db.JobRoles.Where(r => normalized.Contains(r.NormalizedName)).ToDictionaryAsync(r => r.NormalizedName, ct);
        profile.JobRoles.Clear();
        profile.JobRoles.AddRange(roleNames.Select(name => roles.GetValueOrDefault(Names.Normalize(name))
            ?? new JobRole { UserId = profile.UserId, Name = name.Trim(), NormalizedName = Names.Normalize(name) }));
        profile.WorkModes.Clear();
        profile.WorkModes.AddRange(workModes.Distinct().Select(m => new ProfileWorkMode { WorkMode = m }));
        profile.EmploymentTypes.Clear();
        profile.EmploymentTypes.AddRange(employmentTypes.Distinct().Select(t => new ProfileEmploymentType { EmploymentType = t }));

        await db.SaveChangesAsync(ct);
        return await ProfileQueries.LoadAsync(db, ct);
    }

    // Upsert: setting a skill the profile already has replaces its level.
    [Error<InvalidProfileException>]
    public static async Task<ProfileSkill> SetProfileSkillAsync(string name, int level, AppDbContext db, CancellationToken ct)
    {
        if (level is < 1 or > 5)
            throw new InvalidProfileException("Skill level must be 1–5.");
        var profile = await db.Profiles.SingleAsync(ct);
        var skill = (await FindOrCreateSkillsAsync([name], profile.UserId, db, ct))[0];
        var profileSkill = skill.Id == 0 ? null : await db.ProfileSkills.FirstOrDefaultAsync(s => s.SkillId == skill.Id, ct);
        if (profileSkill is null)
            db.ProfileSkills.Add(profileSkill = new ProfileSkill { ProfileId = profile.Id, Skill = skill });
        profileSkill.Level = level;
        await db.SaveChangesAsync(ct);
        return profileSkill;
    }

    // Only takes the skill off the profile's list: the Skill lookup row and entry tags stay.
    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "skillId")]
    public static async Task<int> RemoveProfileSkillAsync(int skillId, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileSkills.Where(s => s.SkillId == skillId).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Skill", skillId);
        return skillId;
    }

    // Upsert by name (case-insensitive), like setProfileSkill.
    [Error<InvalidProfileException>]
    public static async Task<ProfileLanguage> SetProfileLanguageAsync(string name,
        LanguageProficiency written, LanguageProficiency spoken, AppDbContext db, CancellationToken ct)
    {
        var cleaned = LanguageName(name);
        var profile = await db.Profiles.SingleAsync(ct);
        var profileLanguage = await db.ProfileLanguages.FirstOrDefaultAsync(l => l.Name == cleaned, ct);
        if (profileLanguage is null)
            db.ProfileLanguages.Add(profileLanguage = new ProfileLanguage { ProfileId = profile.Id, Name = cleaned });
        profileLanguage.Name = cleaned;
        profileLanguage.Written = written;
        profileLanguage.Spoken = spoken;
        await db.SaveChangesAsync(ct);
        return profileLanguage;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "name")]
    public static async Task<string> RemoveProfileLanguageAsync(string name, AppDbContext db, CancellationToken ct)
    {
        var cleaned = name.Trim();
        if (await db.ProfileLanguages.Where(l => l.Name == cleaned).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Language", cleaned);
        return cleaned;
    }

    internal static string LanguageName(string? name) =>
        Text(name, "Language name", ProfileLanguage.MaxNameLength)
        ?? throw new InvalidProfileException("Language name is required.");

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? Text(string? value, string field, int maxLength = MaxTextLength)
    {
        var text = Clean(value);
        if (text?.Length > maxLength)
            throw new InvalidProfileException($"{field} is longer than {maxLength} characters.");
        return text;
    }

    internal static string Required(string? value, string field, int maxLength = MaxTextLength) =>
        Text(value, field, maxLength) ?? throw new InvalidProfileException($"{field} is required.");

    internal static string? Email(string? value)
    {
        var email = Clean(value);
        if (email is not null && !(MailAddress.TryCreate(email, out var address) && address.Address == email))
            throw new InvalidProfileException($"\"{email}\" is not an email address.");
        return email;
    }

    internal static string? Markdown(string? value, string field) =>
        Text(value, field, ApplicationMutations.MaxNoteLength);

    internal static string? Url(string? value)
    {
        var text = Clean(value);
        if (text is not null && !(Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https"))
            throw new InvalidProfileException($"\"{text}\" is not an http(s) link.");
        return text;
    }

    // Entries are month precision; the UI's month picker gives a month, but any day is accepted and dropped.
    internal static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);

    internal static DateOnly? Month(DateOnly? date) => date is { } d ? Month(d) : null;

    // Found case-insensitively or created, like listing skills, so the profile and listings share skill ids.
    internal static async Task<List<Skill>> FindOrCreateSkillsAsync(
        IEnumerable<string> names, string userId, AppDbContext db, CancellationToken ct)
    {
        var distinct = names.Select(n => Required(n, "Skill name")).DistinctBy(Names.Normalize).ToList();
        var normalized = distinct.Select(Names.Normalize).ToList();
        var existing = await db.Skills.Where(s => normalized.Contains(s.NormalizedName)).ToDictionaryAsync(s => s.NormalizedName, ct);
        return [.. distinct.Select(name => existing.GetValueOrDefault(Names.Normalize(name))
            ?? db.Skills.Local.FirstOrDefault(s => s.UserId == userId && s.NormalizedName == Names.Normalize(name))
            ?? new Skill { UserId = userId, Name = name, NormalizedName = Names.Normalize(name) })];
    }
}
