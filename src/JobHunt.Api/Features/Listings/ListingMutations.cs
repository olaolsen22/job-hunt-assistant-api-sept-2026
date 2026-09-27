using JobHunt.Api.Data;
using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Listings;

// Company, job role and skills come in by name and are found (case-insensitively) or created,
// which fits an autocomplete input now and the LLM's output later.
public record ListingInput(
    string Title,
    string CompanyName,
    string JobRoleName,
    Seniority? Seniority,
    EmploymentType? EmploymentType,
    Schedule? Schedule,
    WorkMode? WorkMode,
    string? Description,
    string Url,
    DateOnly PostedOn,
    [property: DefaultValue(DeadlineType.Unspecified)] DeadlineType DeadlineType,
    DateOnly? DeadlineOn,
    Language? PostingLanguage,
    List<ListingSkillInput> Skills,
    List<ListingLanguageInput> Languages,
    List<LocationInput> Locations,
    [property: DefaultValue(false)] bool IsAnalyzed);

public record ListingSkillInput(
    string Name,
    [property: DefaultValue(3)] int Level,
    int? MinYears,
    [property: DefaultValue(true)] bool IsRequired);

public record ListingLanguageInput(
    Language Language,
    CefrLevel? Level,
    [property: DefaultValue(true)] bool IsRequired);

public class InvalidListingException(string message) : Exception(message);

public class ListingNotFoundException(int id) : Exception($"Listing {id} was not found.");

public class DuplicateListingException(int existingListingId)
    : Exception($"A listing with this URL already exists ({existingListingId}).")
{
    public int ExistingListingId { get; } = existingListingId;
}

// Same company and role or title but a different URL: often the same job on another board, but can be
// a second opening, so it's saved and the matches are returned for the user to judge.
public record CreateListingPayload(Listing Listing, List<Listing> PossibleDuplicates);

public class ListingInUseException(int id) : Exception($"Listing {id} has been applied to and can't be deleted. Archive it instead.");

[MutationType]
public static class ListingMutations
{
    [Error<InvalidListingException>]
    [Error<InvalidLocationException>]
    [Error<DuplicateListingException>]
    public static async Task<CreateListingPayload> CreateListingAsync(
        ListingInput listing, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var entity = new Listing { UserId = db.CurrentUserId!, CreatedAt = now, UpdatedAt = now };
        await ApplyAsync(entity, listing, now, db, ct);
        db.Listings.Add(entity);
        await db.SaveChangesAsync(ct);

        // Few listings per company, so the title match runs in memory with the same Names.Normalize as lookups.
        var title = Names.Normalize(entity.Title);
        var sameCompany = await WithDetails(db)
            .Where(l => l.CompanyId == entity.CompanyId && l.Id != entity.Id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);
        var possibleDuplicates = sameCompany
            .Where(l => l.JobRoleId == entity.JobRoleId || Names.Normalize(l.Title) == title)
            .ToList();
        return new CreateListingPayload(entity, possibleDuplicates);
    }

    // Replaces the whole listing, including its skills and languages.
    [Error<ListingNotFoundException>]
    [Error<InvalidListingException>]
    [Error<InvalidLocationException>]
    [Error<DuplicateListingException>]
    public static async Task<Listing> UpdateListingAsync(
        int id, ListingInput listing, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await FindWithDetailsAsync(id, db, ct);
        var now = time.GetUtcNow().UtcDateTime;
        await ApplyAsync(entity, listing, now, db, ct);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Not an edit of the listing itself, so UpdatedAt stays as is.
    [Error<ListingNotFoundException>]
    public static async Task<Listing> SetListingFavoriteAsync(
        int id, bool isFavorite, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await FindWithDetailsAsync(id, db, ct);
        // ??= keeps the original time when favoriting twice, so the shortlist order doesn't jump.
        if (isFavorite)
            entity.FavoritedAt ??= time.GetUtcNow().UtcDateTime;
        else
            entity.FavoritedAt = null;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Not an edit of the listing itself, so UpdatedAt stays as is.
    [Error<ListingNotFoundException>]
    public static async Task<Listing> SetListingArchivedAsync(
        int id, bool isArchived, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await FindWithDetailsAsync(id, db, ct);
        if (isArchived)
            entity.ArchivedAt ??= time.GetUtcNow().UtcDateTime;
        else
            entity.ArchivedAt = null;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    [Error<ListingNotFoundException>]
    [Error<ListingInUseException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteListingAsync(int id, AppDbContext db, CancellationToken ct)
    {
        // Deleting would lose the record of the application.
        if (await db.Listings.AnyAsync(l => l.Id == id && l.Status != ApplicationStatus.Saved, ct))
            throw new ListingInUseException(id);
        // The user filter applies here too, so another user's listing counts as not found.
        if (await db.Listings.Where(l => l.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ListingNotFoundException(id);
        return id;
    }

    // Loads everything the payload's listing can expose, so a client selecting company or skills gets real data.
    internal static async Task<Listing> FindWithDetailsAsync(int id, AppDbContext db, CancellationToken ct) =>
        await WithDetails(db).FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new ListingNotFoundException(id);

    private static IQueryable<Listing> WithDetails(AppDbContext db) =>
        db.Listings
            .Include(l => l.Company)
            .Include(l => l.JobRole)
            .Include(l => l.Skills).ThenInclude(s => s.Skill)
            .Include(l => l.Languages)
            .Include(l => l.Locations).ThenInclude(l => l.Location)
            .Include(l => l.Documents)
            .Include(l => l.StatusHistory)
            .Include(l => l.Interviews)
            // One query per collection: joining six collections in one would multiply their rows together.
            .AsSplitQuery();

    private static async Task ApplyAsync(
        Listing entity, ListingInput input, DateTime now, AppDbContext db, CancellationToken ct)
    {
        Validate(input, DateOnly.FromDateTime(now));

        entity.Title = input.Title.Trim();
        entity.Seniority = input.Seniority;
        entity.EmploymentType = input.EmploymentType;
        entity.Schedule = input.Schedule;
        entity.WorkMode = input.WorkMode;
        entity.Description = input.Description;
        entity.Url = NormalizeUrl(new Uri(input.Url.Trim()));
        // Archived listings count too: re-adding an ad you already applied to is still a duplicate.
        var existingId = await db.Listings
            .Where(l => l.Url == entity.Url && l.Id != entity.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is not null)
            throw new DuplicateListingException(existingId.Value);
        entity.PostedOn = input.PostedOn;
        entity.DeadlineType = input.DeadlineType;
        entity.DeadlineOn = input.DeadlineOn;
        entity.PostingLanguage = input.PostingLanguage;
        // ??= keeps the first time, so re-saving an analyzed listing doesn't reorder the list.
        if (input.IsAnalyzed)
            entity.AnalyzedAt ??= now;
        else
            entity.AnalyzedAt = null;

        var companyName = Names.Normalize(input.CompanyName);
        entity.Company = await db.Companies.FirstOrDefaultAsync(c => c.NormalizedName == companyName, ct)
            ?? new Company { UserId = entity.UserId, Name = input.CompanyName.Trim(), NormalizedName = companyName };

        var jobRoleName = Names.Normalize(input.JobRoleName);
        entity.JobRole = await db.JobRoles.FirstOrDefaultAsync(r => r.NormalizedName == jobRoleName, ct)
            ?? new JobRole { UserId = entity.UserId, Name = input.JobRoleName.Trim(), NormalizedName = jobRoleName };

        var skillNames = input.Skills.Select(s => Names.Normalize(s.Name)).ToList();
        var skills = await db.Skills
            .Where(s => skillNames.Contains(s.NormalizedName))
            .ToDictionaryAsync(s => s.NormalizedName, ct);
        entity.Skills.Clear();
        foreach (var skill in input.Skills)
        {
            var name = Names.Normalize(skill.Name);
            entity.Skills.Add(new ListingSkill
            {
                Skill = skills.GetValueOrDefault(name)
                    ?? new Skill { UserId = entity.UserId, Name = skill.Name.Trim(), NormalizedName = name },
                Level = skill.Level,
                MinYears = skill.MinYears,
                IsRequired = skill.IsRequired,
            });
        }

        entity.Languages.Clear();
        foreach (var language in input.Languages)
            entity.Languages.Add(new ListingLanguage
            {
                Language = language.Language,
                Level = language.Level,
                IsRequired = language.IsRequired,
            });

        entity.Locations.Clear();
        foreach (var location in input.Locations)
            entity.Locations.Add(new ListingLocation { Location = await LocationLookup.FindOrCreateAsync(location, db, ct) });
    }

    // Stored normalized so the unique index catches the same ad pasted with tracking params, a #fragment
    // or a trailing slash. Other params stay: some boards put the ad id in the query (?finnkode=123).
    // Uri already lowercases scheme and host and drops the default port.
    private static string NormalizeUrl(Uri url)
    {
        var query = string.Join('&', url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTrackingParam(p.Split('=')[0])));
        return $"{url.Scheme}://{url.Authority}{url.AbsolutePath.TrimEnd('/')}{(query == "" ? "" : "?" + query)}";
    }

    private static bool IsTrackingParam(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
        || key.Equals("gclid", StringComparison.OrdinalIgnoreCase);

    private static void Validate(ListingInput input, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new InvalidListingException("Title is required.");
        if (string.IsNullOrWhiteSpace(input.CompanyName))
            throw new InvalidListingException("Company name is required.");
        if (string.IsNullOrWhiteSpace(input.JobRoleName))
            throw new InvalidListingException("Job role is required.");
        // http(s) only: the UI renders this as a link, and a javascript: URL would run on click.
        if (!Uri.TryCreate(input.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidListingException("Url must be an http(s) link.");
        // One day of slack: today is UTC here, and just after midnight in Norway it's still yesterday in UTC.
        if (input.PostedOn > today.AddDays(1))
            throw new InvalidListingException("Posted date can't be in the future.");
        if ((input.DeadlineType == DeadlineType.Date) != (input.DeadlineOn is not null))
            throw new InvalidListingException("Set deadlineOn if and only if deadlineType is DATE.");

        foreach (var skill in input.Skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Name))
                throw new InvalidListingException("Skill name is required.");
            if (skill.Level is < 1 or > 5)
                throw new InvalidListingException($"Skill \"{skill.Name.Trim()}\": level must be 1–5.");
            if (skill.MinYears is < 1)
                throw new InvalidListingException($"Skill \"{skill.Name.Trim()}\": minYears must be at least 1.");
        }

        var duplicateSkill = input.Skills.GroupBy(s => Names.Normalize(s.Name)).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSkill is not null)
            throw new InvalidListingException($"Skill \"{duplicateSkill.First().Name.Trim()}\" is listed twice.");

        var duplicateLanguage = input.Languages.GroupBy(l => l.Language).FirstOrDefault(g => g.Count() > 1);
        if (duplicateLanguage is not null)
            throw new InvalidListingException($"Language {duplicateLanguage.Key} is listed twice.");

        if (input.Locations.GroupBy(LocationLookup.Key).Any(g => g.Count() > 1))
            throw new InvalidListingException("A location is listed twice.");
    }
}
