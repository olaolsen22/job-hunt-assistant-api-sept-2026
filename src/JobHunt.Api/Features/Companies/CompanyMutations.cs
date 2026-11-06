using JobHunt.Api.Data;
using JobHunt.Api.Features.Contacts;
using JobHunt.Api.Features.Industries;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Skills;
// Hot Chocolate also has a global Location type (a position in a query document).
using JobHunt.Api.Features.Locations;
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Companies;

// Same shape as the AI's company rows, so the UI can pass parsed JSON straight through.
// The research fields default to empty so name-only rows keep working.
public record CompanyInput(
    string Name, string? IndustryName, List<LocationInput> Locations,
    string? OrgNumber = null, string? WebsiteUrl = null, string? CareersUrl = null, string? LinkedInUrl = null,
    CompanyKind? Kind = null, CompanySize? SizeBand = null, int? FoundedYear = null, string? Description = null,
    WorkMode? WorkMode = null, Language? WorkingLanguage = null, List<string>? Skills = null,
    List<ContactInput>? Contacts = null)
{
    internal CompanyDetailsInput Details() => new(OrgNumber, WebsiteUrl, CareersUrl, LinkedInUrl,
        Kind, SizeBand, FoundedYear, Description, WorkMode, WorkingLanguage, Skills ?? [], Contacts);
}

// Contacts are only filled or added, never replaced (ContactLookup); edit a person through updateContact.
public record CompanyDetailsInput(
    string? OrgNumber, string? WebsiteUrl, string? CareersUrl, string? LinkedInUrl,
    CompanyKind? Kind, CompanySize? SizeBand, int? FoundedYear, string? Description,
    WorkMode? WorkMode, Language? WorkingLanguage, List<string> Skills, List<ContactInput>? Contacts = null);

// Created is false for an existing company (same org.nr or name). Filled: its empty fields took values from the row.
public record CompanyResult(Company Company, bool Created, bool Filled);

public class InvalidCompanyException(string message) : Exception(message);

public class CompanyNotFoundException(int id) : Exception($"Company {id} was not found.");

[MutationType]
public static class CompanyMutations
{
    // Well above the ~30 rows a pasted quick-add list produces, far below what one transaction minds.
    private const int MaxBatchSize = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxSkills = 50;
    private const int MaxSkillNameLength = 100;
    private const int MaxContacts = 20;

    // All or nothing. A row matching a company (org.nr, then name) only fills blanks, so research can be re-pasted.
    [Error<InvalidCompanyException>]
    [Error<InvalidLocationException>]
    [UseMutationConvention(PayloadFieldName = "results")]
    public static async Task<List<CompanyResult>> CreateCompaniesAsync(
        List<CompanyInput> companies, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (companies.Count is 0 or > MaxBatchSize)
            throw new InvalidCompanyException($"Send between 1 and {MaxBatchSize} companies.");
        if (companies.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            throw new InvalidCompanyException("Every company needs a name.");
        if (companies.Any(c => c.Name.Trim().Length > Inputs.MaxNameLength))
            throw new InvalidCompanyException($"Company names are limited to {Inputs.MaxNameLength} characters.");

        // Resolved for every row, even ones that turn out to exist, so a bad country code always fails the batch.
        var now = time.GetUtcNow().UtcDateTime;
        var lookups = new CompanyLookups(db);
        var rows = new List<(CompanyInput Input, string NormalizedName, CompanyDetailsInput Details,
            Industry? Industry, List<Location> Locations, List<Skill> Skills)>();
        foreach (var input in companies)
        {
            var details = Clean(input.Details(), now.Year);
            rows.Add((input, Names.Normalize(input.Name), details,
                await lookups.IndustryAsync(input.IndustryName, ct),
                await lookups.LocationsAsync(input.Locations, ct),
                await lookups.SkillsAsync(details.Skills, ct)));
        }

        var names = rows.Select(r => r.NormalizedName).Distinct().ToList();
        var orgNumbers = rows.Select(r => r.Details.OrgNumber).OfType<string>().Distinct().ToList();
        var existing = await db.Companies
            .Include(c => c.Industry).Include(c => c.Locations).ThenInclude(l => l.Location)
            .Include(c => c.Skills).ThenInclude(s => s.Skill)
            // All of them, so the returned company lists everyone, not just the people this batch named.
            .Include(c => c.Contacts)
            .Where(c => names.Contains(c.NormalizedName) || orgNumbers.Contains(c.OrgNumber!))
            .ToListAsync(ct);
        var byName = existing.ToDictionary(c => c.NormalizedName);
        var byOrgNumber = existing.Where(c => c.OrgNumber is not null).ToDictionary(c => c.OrgNumber!);

        var results = new List<CompanyResult>();
        foreach (var row in rows)
        {
            var org = row.Details.OrgNumber;
            if ((org is not null && byOrgNumber.TryGetValue(org, out var company))
                || byName.TryGetValue(row.NormalizedName, out company))
            {
                // An org.nr reaching here is unused (or it would have matched above), so filling it can't clash.
                var filled = Fill(company, row.Details, row.Industry, row.Locations, row.Skills)
                    | await FillContactsAsync(company, row.Details, db, ct);
                if (filled)
                {
                    company.ResearchedAt = now;
                    if (company.OrgNumber is not null) byOrgNumber[company.OrgNumber] = company;
                }
                results.Add(new CompanyResult(company, false, filled));
                continue;
            }
            company = new Company
            {
                UserId = db.CurrentUserId!,
                Name = row.Input.Name.Trim(),
                NormalizedName = row.NormalizedName,
            };
            var researched = Fill(company, row.Details, row.Industry, row.Locations, row.Skills);
            db.Companies.Add(company);
            researched |= await FillContactsAsync(company, row.Details, db, ct);
            if (researched) company.ResearchedAt = now;
            byName[row.NormalizedName] = company;
            if (org is not null) byOrgNumber[org] = company;
            results.Add(new CompanyResult(company, true, false));
        }
        await db.SaveChangesAsync(ct);
        return results;
    }

    // Full replace of the research fields. Separate from updateCompany so clients that don't send them can't wipe them.
    [Error<CompanyNotFoundException>]
    [Error<InvalidCompanyException>]
    public static async Task<Company> UpdateCompanyDetailsAsync(
        int id, CompanyDetailsInput details, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        details = Clean(details, time.GetUtcNow().Year);
        var company = await db.Companies.Include(c => c.Skills).ThenInclude(s => s.Skill).Include(c => c.Contacts)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        if (details.OrgNumber is { } org
            && await db.Companies.AnyAsync(c => c.OrgNumber == org && c.Id != id, ct))
            throw new InvalidCompanyException($"Another company already has org.nr {org}.");

        company.OrgNumber = details.OrgNumber;
        company.WebsiteUrl = details.WebsiteUrl;
        company.CareersUrl = details.CareersUrl;
        company.LinkedInUrl = details.LinkedInUrl;
        company.Kind = details.Kind;
        company.SizeBand = details.SizeBand;
        company.FoundedYear = details.FoundedYear;
        company.Description = details.Description;
        company.WorkMode = details.WorkMode;
        company.WorkingLanguage = details.WorkingLanguage;

        // Diff like updateCompany's locations: re-adding a kept skill would clash with the join row being deleted.
        var wanted = await new CompanyLookups(db).SkillsAsync(details.Skills, ct);
        company.Skills.RemoveAll(cs => !wanted.Contains(cs.Skill));
        foreach (var skill in wanted.Where(w => company.Skills.All(cs => cs.Skill != w)))
            company.Skills.Add(new CompanySkill { Skill = skill });
        await FillContactsAsync(company, details, db, ct);

        await db.SaveChangesAsync(ct);
        return company;
    }

    private static async Task<bool> FillContactsAsync(
        Company company, CompanyDetailsInput details, AppDbContext db, CancellationToken ct) =>
        details.Contacts is { Count: > 0 } contacts && (await ContactLookup.UpsertAsync(company, contacts, db, ct)).Changed;

    // Sets only what is empty on the company; returns whether anything changed.
    private static bool Fill(Company company, CompanyDetailsInput details, Industry? industry,
        List<Location> locations, List<Skill> skills)
    {
        var filled = false;
        company.Industry = Blank(company.Industry, industry, ref filled);
        company.OrgNumber = Blank(company.OrgNumber, details.OrgNumber, ref filled);
        company.WebsiteUrl = Blank(company.WebsiteUrl, details.WebsiteUrl, ref filled);
        company.CareersUrl = Blank(company.CareersUrl, details.CareersUrl, ref filled);
        company.LinkedInUrl = Blank(company.LinkedInUrl, details.LinkedInUrl, ref filled);
        company.Kind = Blank(company.Kind, details.Kind, ref filled);
        company.SizeBand = Blank(company.SizeBand, details.SizeBand, ref filled);
        company.FoundedYear = Blank(company.FoundedYear, details.FoundedYear, ref filled);
        company.Description = Blank(company.Description, details.Description, ref filled);
        company.WorkMode = Blank(company.WorkMode, details.WorkMode, ref filled);
        company.WorkingLanguage = Blank(company.WorkingLanguage, details.WorkingLanguage, ref filled);
        // Lists count as blank only when empty, so a hand-picked set is never merged with research.
        if (company.Locations.Count == 0 && locations.Count > 0)
        {
            company.Locations.AddRange(locations.Select(l => new CompanyLocation { Location = l }));
            filled = true;
        }
        if (company.Skills.Count == 0 && skills.Count > 0)
        {
            company.Skills.AddRange(skills.Select(s => new CompanySkill { Skill = s }));
            filled = true;
        }
        return filled;
    }

    private static T Blank<T>(T current, T incoming, ref bool filled)
    {
        if (current is not null || incoming is null) return current;
        filled = true;
        return incoming;
    }

    // Trims, blanks to null, and validates; shared by createCompanies rows and updateCompanyDetails.
    private static CompanyDetailsInput Clean(CompanyDetailsInput details, int currentYear)
    {
        var orgNumber = Inputs.Clean(details.OrgNumber)?.Replace(" ", "");
        if (orgNumber is not null && !IsOrgNumber(orgNumber))
            throw new InvalidCompanyException($"\"{details.OrgNumber!.Trim()}\" is not a valid Norwegian org.nr.");
        if (details.FoundedYear is < 1800 || details.FoundedYear > currentYear)
            throw new InvalidCompanyException($"Founded year must be between 1800 and {currentYear}.");
        var description = Inputs.Clean(details.Description);
        if (description?.Length > MaxDescriptionLength)
            throw new InvalidCompanyException($"The description is longer than {MaxDescriptionLength} characters.");

        var skills = details.Skills.Select(Inputs.Clean).OfType<string>().DistinctBy(Names.Normalize).ToList();
        if (skills.Count > MaxSkills)
            throw new InvalidCompanyException($"A company can list at most {MaxSkills} skills.");
        if (skills.FirstOrDefault(s => s.Length > MaxSkillNameLength) is { } longSkill)
            throw new InvalidCompanyException($"Skill \"{longSkill[..20]}…\" is longer than {MaxSkillNameLength} characters.");

        var contacts = details.Contacts ?? [];
        if (contacts.Count > MaxContacts)
            throw new InvalidCompanyException($"A company can bring at most {MaxContacts} contacts at a time.");
        foreach (var contact in contacts)
        {
            try { ContactMutations.Validate(contact); }
            catch (InvalidContactException e) { throw new InvalidCompanyException(e.Message); }
        }
        var repeated = contacts.GroupBy(c => Names.Normalize(c.Name)).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new InvalidCompanyException($"Contact \"{repeated.First().Name.Trim()}\" is listed twice.");

        return details with
        {
            OrgNumber = orgNumber,
            WebsiteUrl = Url(details.WebsiteUrl, "Website"),
            CareersUrl = Url(details.CareersUrl, "Careers page"),
            LinkedInUrl = Url(details.LinkedInUrl, "LinkedIn"),
            Description = description,
            Skills = skills,
        };
    }

    // http(s) only: the UI renders these as links, and a javascript: URL would run on click.
    private static string? Url(string? value, string field)
    {
        var url = Inputs.Clean(value);
        if (url is not null && !Inputs.TryHttpUrl(url, out _))
            throw new InvalidCompanyException($"{field} must be an http(s) link.");
        return url;
    }

    // Brønnøysund mod-11: weights 3 2 7 6 5 4 3 2 on the first eight digits; remainder 1 is never issued.
    internal static bool IsOrgNumber(string value)
    {
        if (value.Length != 9 || !value.All(char.IsAsciiDigit)) return false;
        int[] weights = [3, 2, 7, 6, 5, 4, 3, 2];
        var remainder = weights.Select((w, i) => w * (value[i] - '0')).Sum() % 11;
        var check = remainder == 0 ? 0 : 11 - remainder;
        return check != 10 && check == value[8] - '0';
    }

    [Error<CompanyNotFoundException>]
    public static async Task<Company> SetCompanyFavoriteAsync(
        int id, bool isFavorite, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        // ??= keeps the original time when favoriting twice, so the shortlist order doesn't jump.
        if (isFavorite)
            company.FavoritedAt ??= time.GetUtcNow().UtcDateTime;
        else
            company.FavoritedAt = null;
        await db.SaveChangesAsync(ct);
        return company;
    }

    [Error<CompanyNotFoundException>]
    public static async Task<Company> SetCompanyAvoidedAsync(
        int id, bool isAvoided, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        if (isAvoided)
            company.AvoidedAt ??= time.GetUtcNow().UtcDateTime;
        else
            company.AvoidedAt = null;
        await db.SaveChangesAsync(ct);
        return company;
    }

    // Separate from updateCompany: that's a full replace, so clients that don't send notes would wipe them.
    [Error<CompanyNotFoundException>]
    [Error<InvalidCompanyException>]
    public static async Task<Company> SetCompanyNotesAsync(
        int id, string? notes, AppDbContext db, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        notes = Inputs.Clean(notes);
        if (notes?.Length > Inputs.MaxMarkdownLength)
            throw new InvalidCompanyException($"The notes are longer than {Inputs.MaxMarkdownLength} characters.");
        company.Notes = notes;
        await db.SaveChangesAsync(ct);
        return company;
    }

    // Companies are created by createListing; this is for fixing the name and replacing the industry and locations.
    [Error<CompanyNotFoundException>]
    [Error<InvalidCompanyException>]
    [Error<InvalidLocationException>]
    public static async Task<Company> UpdateCompanyAsync(
        int id, string name, string? industryName, List<LocationInput> locations, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidCompanyException("Name is required.");
        if (name.Trim().Length > Inputs.MaxNameLength)
            throw new InvalidCompanyException($"Company names are limited to {Inputs.MaxNameLength} characters.");

        var company = await db.Companies.Include(c => c.Locations).ThenInclude(l => l.Location)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        var normalizedName = Names.Normalize(name);
        if (normalizedName != company.NormalizedName
            && await db.Companies.AnyAsync(c => c.NormalizedName == normalizedName, ct))
            throw new InvalidCompanyException($"A company named \"{name.Trim()}\" already exists.");

        var lookups = new CompanyLookups(db);
        company.Name = name.Trim();
        company.NormalizedName = normalizedName;
        company.Industry = await lookups.IndustryAsync(industryName, ct);

        // Diff instead of Clear + Add: re-adding a kept location would clash with the join row being deleted.
        var wanted = await lookups.LocationsAsync(locations, ct);
        company.Locations.RemoveAll(cl => !wanted.Contains(cl.Location));
        foreach (var location in wanted.Where(w => company.Locations.All(cl => cl.Location != w)))
            company.Locations.Add(new CompanyLocation { Location = location });

        await db.SaveChangesAsync(ct);
        return company;
    }
}

// Remembers new lookups within a batch: untracked, FindOrCreate can't see them, and duplicates fail the unique index.
internal class CompanyLookups(AppDbContext db)
{
    private readonly Dictionary<string, Industry> industries = [];
    private readonly Dictionary<string, Location> locations = [];
    private readonly Dictionary<string, Skill> skills = [];

    public async Task<Industry?> IndustryAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalizedName = Names.Normalize(name);
        if (industries.TryGetValue(normalizedName, out var known)) return known;

        var industry = await db.Industries.FirstOrDefaultAsync(i => i.NormalizedName == normalizedName, ct)
            ?? new Industry { UserId = db.CurrentUserId!, Name = name.Trim(), NormalizedName = normalizedName };
        return industries[normalizedName] = industry;
    }

    // Names arrive cleaned and distinct.
    public async Task<List<Skill>> SkillsAsync(List<string> names, CancellationToken ct)
    {
        var missing = names.Where(n => !skills.ContainsKey(Names.Normalize(n))).ToList();
        foreach (var skill in await SkillLookup.FindOrCreateAsync(missing, db, ct))
            skills[skill.NormalizedName] = skill;
        return [.. names.Select(name => skills[Names.Normalize(name)])];
    }

    // Repeats within one list collapse into one location.
    public async Task<List<Location>> LocationsAsync(IEnumerable<LocationInput> inputs, CancellationToken ct)
    {
        var result = new List<Location>();
        foreach (var input in inputs)
        {
            var key = LocationLookup.Key(input);
            if (!locations.TryGetValue(key, out var location))
                location = locations[key] = await LocationLookup.FindOrCreateAsync(input, db, ct);
            if (!result.Contains(location)) result.Add(location);
        }
        return result;
    }
}
