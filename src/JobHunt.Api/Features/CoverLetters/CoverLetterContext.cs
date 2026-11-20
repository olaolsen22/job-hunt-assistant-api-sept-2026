using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
using JobHunt.Api.Features.WritingSamples;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.CoverLetters;

// A complete letter without a body: names, contact details and dates never come from the model or the paste.
public record CoverLetterPromptContext(string Json, Language Language, ListingKind Kind, CoverLetterContent Header);

// Shared by the built-in and external paths, so both send the model exactly the same allowlisted data.
public static class CoverLetterContext
{
    public const int MaxContextLength = 100_000;
    private const int SampleBudget = 12_000;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private record Sources(Listing Listing, Profile Profile, TailoredCvContent? Cv, CoverLetterDraft? Letter,
        Language Language, CoverLetterContent Header);

    public static async Task<CoverLetterContent> HeaderAsync(int listingId, AppDbContext db, TimeProvider time,
        CancellationToken ct) => (await LoadAsync(listingId, db, time, ct)).Header;

    public static async Task<CoverLetterPromptContext> BuildAsync(int listingId, AppDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var (listing, profile, cv, letter, language, header) = await LoadAsync(listingId, db, time, ct);
        var samples = await db.WritingSamples.AsNoTracking().ToListAsync(ct);
        var context = new
        {
            Listing = new
            {
                listing.Kind, listing.Title, Company = listing.Company.Name, Industry = listing.Company.Industry?.Name,
                JobRole = listing.JobRole.Name, listing.Url, listing.Seniority, listing.EmploymentType,
                listing.WorkMode, Locations = listing.Locations.Select(l => new { l.Location.City, l.Location.CountryCode }),
                // In the letter's language, so a Norwegian letter can echo the ad's own wording.
                Description = listing.DescriptionIn(language),
                Skills = listing.Skills.Select(s => new { s.Skill.Name, s.Level, s.MinYears, s.IsRequired }),
                Languages = listing.Languages.Select(l => new { l.Language, l.Level, l.IsRequired }),
                Recipient = header.RecipientName,
            },
            // Published facts about the employer: the basis of an open application, motivation for any other.
            CompanyResearch = new
            {
                listing.Company.Kind, listing.Company.SizeBand, listing.Company.Description, listing.Company.WebsiteUrl,
                listing.Company.CareersUrl, Skills = listing.Company.Skills.Select(s => s.Skill.Name),
            },
            // The user's own research and impressions: often the best source of a specific, truthful motivation.
            Notes = new
            {
                Company = listing.Company.Notes,
                Contacts = listing.Contacts.Where(c => c.Contact.Notes is not null)
                    .Select(c => new { c.Contact.Name, c.Contact.Title, c.Contact.Notes }),
                Application = listing.StatusHistory.Where(s => s.Note is not null).OrderBy(s => s.OccurredAt)
                    .Select(s => new { s.Status, s.Note }),
            },
            Candidate = cv is null ? FromProfile(profile, language) : FromCv(cv),
            Availability = new
            {
                profile.NoticePeriodMonths,
                // The letter mentions relocation only when this differs from the listing's locations.
                Location = profile.Location is { } home ? new { home.City, home.CountryCode } : null,
                letter?.RelocationLeadWeeks,
            },
            Story = new
            {
                profile.Story.Origin, profile.Story.Upbringing, profile.Story.CareerJourney,
                profile.Story.Motivation, profile.Story.Values, profile.Hobbies,
            },
            letter?.Brief,
            WritingSamples = WritingSample.ForPrompt(samples, language, SampleBudget)
                .Select(s => new { s.Title, s.Language, s.Kind, s.Text }),
        };
        var json = JsonSerializer.Serialize(context, JsonOptions);
        if (json.Length > MaxContextLength)
            throw new InvalidCoverLetterException($"Prompt context must be at most {MaxContextLength} characters.");
        return new(json, language, listing.Kind, header);
    }

    private static async Task<Sources> LoadAsync(int listingId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking()
            .Include(l => l.Company).ThenInclude(c => c.Industry)
            .Include(l => l.Company).ThenInclude(c => c.Skills).ThenInclude(s => s.Skill)
            .Include(l => l.JobRole)
            .Include(l => l.Skills).ThenInclude(s => s.Skill)
            .Include(l => l.Languages)
            .Include(l => l.Locations).ThenInclude(l => l.Location)
            .Include(l => l.StatusHistory)
            .Include(l => l.Contacts).ThenInclude(c => c.Contact)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new ListingNotFoundException(listingId);
        var letter = await db.CoverLetterDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct);
        var cv = (await db.TailoredCvDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct))?.Content;
        var profile = await ProfileQueries.LoadAsync(db, ct);
        // The letter follows its own saved language, else the CV it goes with, else the posting.
        var language = letter?.Content.Language ?? cv?.Language ?? listing.PostingLanguage ?? Language.English;
        var contact = listing.Contacts.OrderBy(c => c.ContactId).Select(c => c.Contact.Name).FirstOrDefault();
        var norwegian = language == Language.Norwegian;

        // The tailored CV's contact lines are the ones the user already reviewed for this application.
        var header = new CoverLetterContent(
            Language: language,
            FullName: cv?.FullName ?? profile.FullName,
            Address: SenderAddress(profile, cv, language),
            Phone: cv?.Phone ?? profile.Phone,
            Email: cv?.Email ?? profile.Email,
            Date: DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime),
            Organization: listing.Company.Name,
            RecipientName: contact,
            Subject: listing.Kind != ListingKind.Vacancy
                ? norwegian ? $"Åpen søknad – {listing.Title}" : $"Open application – {listing.Title}"
                : norwegian ? $"Søknad på stilling som {listing.Title}" : $"Application for the position of {listing.Title}",
            // Norwegian letters go straight from subject to body, as in the published examples.
            Greeting: norwegian ? null : contact is null ? "Dear hiring team," : $"Dear {contact},",
            Closing: norwegian ? "Vennlig hilsen" : "Kind regards");
        return new(listing, profile, cv, letter, language, header);
    }

    // The profile's postal address when there is one: a CV address is usually just "City, Country".
    private static string? SenderAddress(Profile profile, TailoredCvContent? cv, Language language)
    {
        if (profile.StreetAddress is not null || profile.PostalCode is not null)
        {
            var town = string.Join(" ", new[] { profile.PostalCode, profile.Location?.City }.OfType<string>());
            return string.Join("\n", new[] { profile.StreetAddress, town }.Where(l => !string.IsNullOrEmpty(l)));
        }
        var fallback = cv?.Address ?? (profile.Location is { } home ? TailoredCvDraftValidation.FormatPlace(home, language) : null);
        return fallback is null ? null : string.Join("\n", CoverLetterValidation.AddressLines(fallback));
    }

    // The CV actually being sent, so the letter backs it up. Contact details stay out.
    private static object FromCv(TailoredCvContent cv) => new
    {
        cv.Headline, cv.Summary,
        Experiences = cv.Experiences?.Select(e => new
        {
            e.Employer, e.Title, e.StartMonth, e.EndMonth, e.Paragraph, e.Technologies, Bullets = e.Bullets.Select(b => b.Text),
        }),
        Educations = cv.Educations?.Select(e => new
        {
            e.School, e.Degree, e.Field, e.StartMonth, e.EndMonth, e.Paragraph, Bullets = e.Bullets.Select(b => b.Text),
        }),
        Projects = cv.Projects?.Select(p => new
        {
            p.Name, p.Kind, p.StartMonth, p.EndMonth, p.Paragraph, p.Technologies, Bullets = p.Bullets.Select(b => b.Text),
        }),
        Skills = cv.Skills?.Select(s => s.Name),
        Languages = cv.Languages?.Select(l => new { l.Name, l.Written, l.Spoken }),
    };

    // Explicit allowlist: date of birth, nationality, work authorization and contact details never go out.
    private static object FromProfile(Profile profile, Language language)
    {
        var translation = profile.Translations.FirstOrDefault(t => t.Language == language);
        return new
        {
            Headline = translation?.Headline ?? profile.Headline,
            Summary = translation?.Summary ?? profile.Summary,
            Experiences = profile.Experiences.Select(e => new
            {
                e.Employer, Title = e.TitleIn(language), e.StartMonth, e.EndMonth,
                Technologies = e.Skills.Select(s => s.NameIn(language)),
                Bullets = e.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
            }),
            Educations = profile.Educations.Select(e => new
            {
                e.School, Degree = e.DegreeIn(language), Field = e.FieldIn(language), e.StartMonth, e.EndMonth,
                Bullets = e.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
            }),
            Projects = profile.Projects.Select(p => new
            {
                p.Name, p.Kind, p.StartMonth, p.EndMonth, Technologies = p.Skills.Select(s => s.NameIn(language)),
                Bullets = p.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
            }),
            Skills = profile.Skills.Select(s => s.Skill.NameIn(language)),
            Languages = profile.Languages.Select(l => new { l.Name, l.Written, l.Spoken }),
        };
    }
}
