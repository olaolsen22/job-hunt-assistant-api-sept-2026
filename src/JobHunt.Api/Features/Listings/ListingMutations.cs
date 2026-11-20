using JobHunt.Api.Data;
using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Contacts;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Listings;

// Company, job role and skills come in by name and are found (case-insensitively) or created.
public record ListingInput(
    string Title,
    string CompanyName,
    string JobRoleName,
    Seniority? Seniority,
    EmploymentType? EmploymentType,
    Schedule? Schedule,
    int? PositionPercent,
    WorkMode? WorkMode,
    string? Description,
    // Both required, except that an unsolicited application has neither (the UI links the company's CareersUrl).
    string? Url,
    DateOnly? PostedOn,
    [property: DefaultValue(DeadlineType.Unspecified)] DeadlineType DeadlineType,
    DateOnly? DeadlineOn,
    Language? PostingLanguage,
    List<ListingSkillInput> Skills,
    List<ListingLanguageInput> Languages,
    List<LocationInput> Locations,
    [property: DefaultValue(false)] bool IsAnalyzed,
    // Null leaves the links as they are, so clients that don't know about contacts don't wipe them.
    List<ContactInput>? Contacts = null,
    // Omitted keeps the program, so clients that don't know about it don't wipe it; an explicit null removes it.
    Optional<TrainingProgramInput?> TrainingProgram = default,
    string? JobRoleCategoryName = null,
    // True when this save applies a fresh re-analysis (the prompts change over time); stamps ReanalyzedAt.
    [property: DefaultValue(false)] bool Reanalyzed = false,
    [property: DefaultValue(ListingKind.Vacancy)] ListingKind Kind = ListingKind.Vacancy,
    // Description in the posting's language. Omitted keeps it, so clients that don't know about it don't wipe it;
    // dropped whenever the posting is English or has no language.
    Optional<string?> OriginalDescription = default);

// The end employer comes in by name like the listing's company; null when the ad doesn't name one.
public record TrainingProgramInput(
    string? EndCompanyName,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    bool? IsPaid,
    [property: DefaultValue(JobOfferType.Unspecified)] JobOfferType JobOffer);

public record ListingSkillInput(
    string Name,
    [property: DefaultValue(3)] int Level,
    int? MinYears,
    [property: DefaultValue(true)] bool IsRequired,
    // A suggestion by name: it only fills a skill without a category, never replaces one the user set.
    string? CategoryName = null);

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

// PreviousAttempts: closed listings with this URL. PossibleDuplicates: same company and title, other URL, or
// another active open application there. Saved anyway; the user decides.
public record CreateListingPayload(Listing Listing, List<Listing> PreviousAttempts, List<Listing> PossibleDuplicates);

public class ListingInUseException(int id) : Exception($"Listing {id} has been applied to and can't be deleted. Archive it instead.");

[MutationType]
public static class ListingMutations
{
    // Parsed descriptions run to about 1000 characters plus the requirements list; pasted ones can be longer.
    private const int MaxDescriptionLength = 20_000;

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

        // Guarded: EF compares a null URL as IS NULL, which would match every unsolicited application.
        var previousAttempts = entity.Url is null ? [] : await WithDetails(db)
            .Where(l => l.Url == entity.Url && l.Id != entity.Id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var openApplication = entity.Kind != ListingKind.Vacancy;
        var possibleDuplicates = await WithDetails(db)
            .Where(l => l.CompanyId == entity.CompanyId && l.Id != entity.Id && (entity.Url == null || l.Url != entity.Url)
                && (l.NormalizedTitle == entity.NormalizedTitle
                    || (openApplication && l.Kind != ListingKind.Vacancy && !ApplicationStatuses.Final.Contains(l.Status))))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);
        return new CreateListingPayload(entity, previousAttempts, possibleDuplicates);
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
        // ??= keeps the original time when favoriting twice, so the favorites order doesn't jump.
        if (isFavorite)
            entity.FavoritedAt ??= time.GetUtcNow().UtcDateTime;
        else
            entity.FavoritedAt = null;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Independent of favorites and application status; leaves UpdatedAt unchanged.
    [Error<ListingNotFoundException>]
    public static async Task<Listing> SetListingShortlistedAsync(
        int id, bool isShortlisted, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await FindWithDetailsAsync(id, db, ct);
        if (isShortlisted)
            entity.ShortlistedAt ??= time.GetUtcNow().UtcDateTime;
        else
            entity.ShortlistedAt = null;
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

    // Shared with undoStatusChange, which must not reopen a listing while another attempt is open.
    internal static async Task<int?> OpenListingIdWithUrlAsync(Listing listing, AppDbContext db, CancellationToken ct) =>
        listing.Url is null ? null : await db.Listings
            .Where(l => l.Url == listing.Url && l.Id != listing.Id && !ApplicationStatuses.Final.Contains(l.Status))
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<Listing> WithDetails(AppDbContext db) =>
        db.Listings
            .Include(l => l.Company)
            .Include(l => l.JobRole).ThenInclude(r => r.Category)
            .Include(l => l.Skills).ThenInclude(s => s.Skill)
            .Include(l => l.Languages)
            .Include(l => l.Locations).ThenInclude(l => l.Location)
            .Include(l => l.Documents)
            .Include(l => l.StatusHistory)
            .Include(l => l.Interviews)
            .Include(l => l.Contacts).ThenInclude(c => c.Contact)
            .Include(l => l.TrainingProgram).ThenInclude(t => t!.EndCompany)
            // One query per collection: joining six collections in one would multiply their rows together.
            .AsSplitQuery();

    private static async Task ApplyAsync(
        Listing entity, ListingInput input, DateTime now, AppDbContext db, CancellationToken ct)
    {
        Validate(input, DateOnly.FromDateTime(now));
        var suggestedCategory = await SuggestedCategoryAsync(input.JobRoleCategoryName, db, ct);
        ApplyFields(entity, input, now);
        // One open listing per URL, archived ones included. Closed listings are earlier attempts.
        if (!ApplicationStatuses.Final.Contains(entity.Status)
            && await OpenListingIdWithUrlAsync(entity, db, ct) is { } existingId)
            throw new DuplicateListingException(existingId);

        entity.Company = await FindOrCreateCompanyAsync(input.CompanyName, entity.UserId, db, ct);
        await ApplyContactsAsync(entity, input.Contacts, db, ct);
        if (input.TrainingProgram.HasValue)
            await ApplyTrainingProgramAsync(entity, input.TrainingProgram.Value, db, ct);
        await ApplyJobRoleAsync(entity, input.JobRoleName, suggestedCategory, db, ct);
        await ApplySkillsAsync(entity, input.Skills, db, ct);

        entity.Languages.Clear();
        entity.Languages.AddRange(input.Languages.Select(l =>
            new ListingLanguage { Language = l.Language, Level = l.Level, IsRequired = l.IsRequired }));
        entity.Locations.Clear();
        foreach (var location in input.Locations)
            entity.Locations.Add(new ListingLocation { Location = await LocationLookup.FindOrCreateAsync(location, db, ct) });
    }

    private static async Task<JobRoleCategory?> SuggestedCategoryAsync(string? name, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalized = Names.Normalize(name);
        return await db.JobRoleCategories.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct)
            ?? throw new InvalidListingException("Choose an existing job role category.");
    }

    private static void ApplyFields(Listing entity, ListingInput input, DateTime now)
    {
        entity.Kind = input.Kind;
        entity.Title = input.Title.Trim();
        entity.NormalizedTitle = Names.Normalize(entity.Title);
        entity.Seniority = input.Seniority;
        entity.EmploymentType = input.EmploymentType;
        entity.Schedule = input.Schedule;
        entity.PositionPercent = input.PositionPercent;
        entity.WorkMode = input.WorkMode;
        entity.Description = input.Description;
        entity.Url = string.IsNullOrWhiteSpace(input.Url) ? null : ListingUrls.Normalize(new Uri(input.Url.Trim()));
        entity.PostedOn = input.PostedOn;
        entity.DeadlineType = input.DeadlineType;
        entity.DeadlineOn = input.DeadlineOn;
        entity.PostingLanguage = input.PostingLanguage;
        if (input.OriginalDescription.HasValue)
            entity.OriginalDescription = string.IsNullOrWhiteSpace(input.OriginalDescription.Value)
                ? null : input.OriginalDescription.Value.Trim();
        if (input.PostingLanguage is null or Language.English)
            entity.OriginalDescription = null;
        // ??= keeps the first time, so re-saving an analyzed listing doesn't reorder the list.
        if (input.IsAnalyzed)
            entity.AnalyzedAt ??= now;
        else
            entity.AnalyzedAt = null;
        if (input.Reanalyzed)
            entity.ReanalyzedAt = now;
    }

    private static async Task ApplyContactsAsync(
        Listing entity, List<ContactInput>? contacts, AppDbContext db, CancellationToken ct)
    {
        // Contacts belong to the company, so a listing moved to another company can't keep the old ones.
        if (entity.CompanyId != entity.Company.Id)
            entity.Contacts.Clear();
        if (contacts is null) return;
        var (found, _) = await ContactLookup.UpsertAsync(entity.Company, contacts, db, ct);
        entity.Contacts.Clear();
        entity.Contacts.AddRange(found.Select(c => new ListingContact { Contact = c }));
    }

    private static async Task ApplyJobRoleAsync(
        Listing entity, string name, JobRoleCategory? suggestedCategory, AppDbContext db, CancellationToken ct)
    {
        var normalized = Names.Normalize(name);
        entity.JobRole = await db.JobRoles.Include(r => r.Category).FirstOrDefaultAsync(r => r.NormalizedName == normalized, ct)
            ?? new JobRole { UserId = entity.UserId, Name = name.Trim(), NormalizedName = normalized };
        if (entity.JobRole.CategoryId is null && entity.JobRole.Category is null && suggestedCategory is not null)
            entity.JobRole.Category = suggestedCategory;
    }

    private static async Task ApplySkillsAsync(
        Listing entity, List<ListingSkillInput> inputs, AppDbContext db, CancellationToken ct)
    {
        var skills = (await SkillLookup.FindOrCreateAsync(inputs.Select(s => s.Name.Trim()), db, ct))
            .ToDictionary(s => s.NormalizedName);
        var categories = await SkillCategoryPrompt.ByNormalizedNameAsync(inputs.Select(s => s.CategoryName), db, ct);
        entity.Skills.Clear();
        foreach (var skill in inputs)
        {
            var lookup = skills[Names.Normalize(skill.Name)];
            if (lookup.CategoryId is null && lookup.Category is null && skill.CategoryName is { } categoryName)
                lookup.Category = categories.GetValueOrDefault(Names.Normalize(categoryName));
            entity.Skills.Add(new ListingSkill
            {
                Skill = lookup,
                Level = skill.Level,
                MinYears = skill.MinYears,
                IsRequired = skill.IsRequired,
            });
        }
    }

    private static async Task<Company> FindOrCreateCompanyAsync(
        string name, string userId, AppDbContext db, CancellationToken ct)
    {
        var normalized = Names.Normalize(name);
        return await db.Companies.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct)
            ?? new Company { UserId = userId, Name = name.Trim(), NormalizedName = normalized };
    }

    private static async Task ApplyTrainingProgramAsync(
        Listing entity, TrainingProgramInput? input, AppDbContext db, CancellationToken ct)
    {
        if (input is null)
        {
            entity.TrainingProgram = null;
            return;
        }

        var program = entity.TrainingProgram ??= new TrainingProgram();
        program.StartsOn = input.StartsOn;
        program.EndsOn = input.EndsOn;
        program.IsPaid = input.IsPaid;
        program.JobOffer = input.JobOffer;
        // Same name as the provider would otherwise create that new company twice in one save.
        program.EndCompany = string.IsNullOrWhiteSpace(input.EndCompanyName) ? null
            : Names.Normalize(input.EndCompanyName) == entity.Company.NormalizedName ? entity.Company
            : await FindOrCreateCompanyAsync(input.EndCompanyName, entity.UserId, db, ct);
    }

    private static void Validate(ListingInput input, DateOnly today)
    {
        ValidateText(input);
        ValidateKindAndDates(input, today);
        if (input.PositionPercent is < 1 or > 100)
            throw new InvalidListingException("Position percent must be 1–100.");
        if ((input.PositionPercent, input.Schedule) is (100, Schedule.PartTime) or (< 100, Schedule.FullTime))
            throw new InvalidListingException("Position percent doesn't match the schedule: 100 % is full time, less is part time.");
        if (input.TrainingProgram.Value is { StartsOn: { } startsOn, EndsOn: { } endsOn } && startsOn > endsOn)
            throw new InvalidListingException("Training program can't end before it starts.");
        ValidateLists(input);
    }

    private static void ValidateText(ListingInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new InvalidListingException("Title is required.");
        if (string.IsNullOrWhiteSpace(input.CompanyName))
            throw new InvalidListingException("Company name is required.");
        if (string.IsNullOrWhiteSpace(input.JobRoleName))
            throw new InvalidListingException("Job role is required.");
        CheckNameLength(input.Title, "Title");
        CheckNameLength(input.CompanyName, "Company name");
        CheckNameLength(input.JobRoleName, "Job role");
        CheckNameLength(input.TrainingProgram.Value?.EndCompanyName, "End company name");
        if (input.Description?.Length > MaxDescriptionLength)
            throw new InvalidListingException($"Description is longer than {MaxDescriptionLength} characters.");
        if (input.OriginalDescription.Value?.Length > MaxDescriptionLength)
            throw new InvalidListingException($"Original description is longer than {MaxDescriptionLength} characters.");
    }

    private static void ValidateKindAndDates(ListingInput input, DateOnly today)
    {
        if (input.Kind == ListingKind.Unsolicited)
        {
            // Nothing was posted. A URL would make two applications to one careers page block each other.
            if (!string.IsNullOrWhiteSpace(input.Url))
                throw new InvalidListingException("An unsolicited application has no URL; the company's careers URL covers it.");
            if (input.PostedOn is not null)
                throw new InvalidListingException("An unsolicited application has no posted date.");
            if (input.DeadlineType != DeadlineType.Unspecified)
                throw new InvalidListingException("An unsolicited application has no deadline.");
        }
        else
        {
            if (!Inputs.TryHttpUrl(input.Url, out _))
                throw new InvalidListingException("Url must be an http(s) link.");
            if (input.PostedOn is null)
                throw new InvalidListingException("Posted date is required.");
        }
        // One day of slack: today is UTC here, and just after midnight in Norway it's still yesterday in UTC.
        if (input.PostedOn > today.AddDays(1))
            throw new InvalidListingException("Posted date can't be in the future.");
        if ((input.DeadlineType == DeadlineType.Date) != (input.DeadlineOn is not null))
            throw new InvalidListingException("Set deadlineOn if and only if deadlineType is DATE.");
    }

    private static void ValidateLists(ListingInput input)
    {
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

        if (ContactMutations.ProblemIn(input.Contacts ?? []) is { } contactProblem)
            throw new InvalidListingException(contactProblem);
    }

    private static void CheckNameLength(string? value, string field)
    {
        if (value?.Trim().Length > Inputs.MaxNameLength)
            throw new InvalidListingException($"{field} is longer than {Inputs.MaxNameLength} characters.");
    }
}
