using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// What both CV paths send about a listing and its employer.
internal static class TailoredCvListingContext
{
    public static Task<Listing?> LoadAsync(int listingId, AppDbContext db, CancellationToken ct) =>
        db.Listings.AsNoTracking()
            .Include(l => l.Company).ThenInclude(c => c.Skills).ThenInclude(s => s.Skill).Include(l => l.JobRole)
            .Include(l => l.Skills).ThenInclude(s => s.Skill)
            .Include(l => l.Languages)
            .FirstOrDefaultAsync(l => l.Id == listingId, ct);

    // The description in the CV's language, so a Norwegian CV can echo the ad's own wording.
    public static object ForAnalysis(Listing listing, Language language) => new
    {
        listing.Kind, listing.Title, Company = listing.Company.Name, JobRole = listing.JobRole.Name,
        Description = listing.DescriptionIn(language), Skills = Skills(listing), Languages = Languages(listing),
        CompanyResearch = Research(listing),
    };

    // With the url, because only an external chat can browse.
    public static object ForExternalPrompt(Listing listing, Language language) => new
    {
        listing.Kind, listing.Title, Company = listing.Company.Name, JobRole = listing.JobRole.Name, listing.Url,
        Description = listing.DescriptionIn(language), Skills = Skills(listing), Languages = Languages(listing),
        CompanyResearch = Research(listing),
    };

    private static object Skills(Listing listing) =>
        listing.Skills.Select(s => new { s.Skill.Name, s.Level, s.MinYears, s.IsRequired });

    private static object Languages(Listing listing) =>
        listing.Languages.Select(l => new { l.Language, l.Level, l.IsRequired });

    private static object Research(Listing listing) => new
    {
        listing.Company.Kind, listing.Company.Description, Skills = listing.Company.Skills.Select(s => s.Skill.Name),
    };
}

// The JSON the built-in analysis sends for one section.
internal static class TailoredCvAnalysisContext
{
    public static object Build(Listing listing, Profile profile, TailoredCvSection section, int? sourceEntryId,
        Language language, List<string>? cvBullets, DateOnly today)
    {
        if (section is not (TailoredCvSection.Experience or TailoredCvSection.Education or TailoredCvSection.Project)
            && sourceEntryId is not null)
            throw new InvalidTailoredCvAnalysisException("sourceEntryId is only valid for an experience, education or project section.");
        var job = TailoredCvListingContext.ForAnalysis(listing, language);
        return section switch
        {
            TailoredCvSection.Experience => BuildExperienceContext(job, profile, sourceEntryId, language),
            TailoredCvSection.Education => BuildEducationContext(job, profile, sourceEntryId, language),
            TailoredCvSection.Project => BuildProjectContext(job, profile, sourceEntryId, language),
            TailoredCvSection.Skills => new
            {
                Listing = job,
                ProfileSkills = profile.Skills.Select(s => new
                {
                    s.SkillId, s.Skill.Name, s.Level,
                    UsedInWork = profile.Experiences.Any(e => e.Skills.Any(k => k.Id == s.SkillId)),
                    UsedInProjects = profile.Projects.Any(p => p.Skills.Any(k => k.Id == s.SkillId)),
                }),
            },
            TailoredCvSection.Summary => new
            {
                Listing = job,
                CvBullets = cvBullets,
                Profile = new
                {
                    Headline = profile.Translations.FirstOrDefault(t => t.Language == language)?.Headline ?? profile.Headline,
                    Summary = profile.Translations.FirstOrDefault(t => t.Language == language)?.Summary ?? profile.Summary,
                    Skills = profile.Skills.Select(s => new { Name = s.Skill.NameIn(language), s.Level }),
                    // Counted here, since the summary states years; education is the evidence for juniors.
                    ExperienceMonths = ProfileDataLoaders.ExperienceMonths(
                        profile.Experiences.Select(e => (e.StartMonth, e.EndMonth)), today),
                    Experience = profile.Experiences.Select(e => new
                    {
                        Title = e.TitleIn(language), e.Employer, e.StartMonth, e.EndMonth,
                        Months = ProfileDataLoaders.ExperienceMonths([(e.StartMonth, e.EndMonth)], today),
                        Skills = e.Skills.Select(s => s.Name),
                        Bullets = e.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
                    }),
                    Education = profile.Educations.Select(e => new
                    {
                        e.School, Degree = e.DegreeIn(language), Field = e.FieldIn(language), e.StartMonth, e.EndMonth,
                        Bullets = e.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
                    }),
                    Projects = profile.Projects.Select(p => new
                    {
                        p.Name, p.Kind, Skills = p.Skills.Select(s => s.Name),
                        Bullets = p.Bullets.Select(b => TailoredCvLanguageText.Bullet(b, language)),
                    }),
                },
            },
            _ => throw new InvalidTailoredCvAnalysisException("Choose an experience, education, project, skills, or summary section."),
        };
    }

    private static object BuildExperienceContext(object job, Profile profile, int? sourceEntryId, Language language)
    {
        if (sourceEntryId is null)
            throw new InvalidTailoredCvAnalysisException("An experience section requires sourceEntryId.");
        var experience = profile.Experiences.FirstOrDefault(e => e.Id == sourceEntryId.Value)
            ?? throw new ProfileEntryNotFoundException("Experience", sourceEntryId.Value);
        return new
        {
            Listing = job,
            Experience = new
            {
                experience.Id, experience.Employer, Title = experience.TitleIn(language), experience.StartMonth, experience.EndMonth,
                Skills = experience.Skills.Select(s => new { s.Id, Name = s.NameIn(language) }),
                Bullets = experience.Bullets.Select(b => new { b.Id, Text = TailoredCvLanguageText.Bullet(b, language) }),
            },
        };
    }

    private static object BuildEducationContext(object job, Profile profile, int? sourceEntryId, Language language)
    {
        if (sourceEntryId is null)
            throw new InvalidTailoredCvAnalysisException("An education section requires sourceEntryId.");
        var education = profile.Educations.FirstOrDefault(e => e.Id == sourceEntryId.Value)
            ?? throw new ProfileEntryNotFoundException("Education", sourceEntryId.Value);
        return new
        {
            Listing = job,
            Education = new
            {
                education.Id, education.School, Degree = education.DegreeIn(language), Field = education.FieldIn(language),
                education.StartMonth, education.EndMonth,
                Bullets = education.Bullets.Select(b => new { b.Id, Text = TailoredCvLanguageText.Bullet(b, language) }),
            },
        };
    }

    private static object BuildProjectContext(object job, Profile profile, int? sourceEntryId, Language language)
    {
        if (sourceEntryId is null)
            throw new InvalidTailoredCvAnalysisException("A project section requires sourceEntryId.");
        var project = profile.Projects.FirstOrDefault(p => p.Id == sourceEntryId.Value)
            ?? throw new ProfileEntryNotFoundException("Project", sourceEntryId.Value);
        return new
        {
            Listing = job,
            Project = new
            {
                project.Id, project.Name, project.Kind, project.StartMonth, project.EndMonth,
                Skills = project.Skills.Select(s => new { s.Id, Name = s.NameIn(language) }),
                Bullets = project.Bullets.Select(b => new { b.Id, Text = TailoredCvLanguageText.Bullet(b, language) }),
            },
        };
    }
}
