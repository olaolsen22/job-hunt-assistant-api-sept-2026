using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

[QueryType]
public static class ProfileQueries
{
    public static Task<Profile> GetProfileAsync(AppDbContext db, CancellationToken ct) => LoadAsync(db, ct);

    // Loads the whole profile rather than projecting: it's a few dozen rows, and the lists need a fixed server-side
    // order that projection can't apply to nested lists.
    internal static async Task<Profile> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles
            .Include(p => p.Location)
            .Include(p => p.JobRoles)
            .Include(p => p.WorkModes)
            .Include(p => p.EmploymentTypes)
            .Include(p => p.Experiences).ThenInclude(e => e.Skills)
            .Include(p => p.Experiences).ThenInclude(e => e.Location)
            .Include(p => p.Experiences).ThenInclude(e => e.Bullets).ThenInclude(b => b.Translations)
            .Include(p => p.Educations).ThenInclude(e => e.Bullets).ThenInclude(b => b.Translations)
            .Include(p => p.Educations).ThenInclude(e => e.Location)
            .Include(p => p.Certifications).ThenInclude(c => c.Bullets).ThenInclude(b => b.Translations)
            .Include(p => p.Projects).ThenInclude(p => p.Skills)
            .Include(p => p.Projects).ThenInclude(p => p.Location)
            .Include(p => p.Projects).ThenInclude(p => p.Bullets).ThenInclude(b => b.Translations)
            .Include(p => p.Skills).ThenInclude(s => s.Skill)
            .Include(p => p.Languages)
            .Include(p => p.References)
            .Include(p => p.Translations)
            .AsSplitQuery()
            .SingleAsync(ct);

        // Current entries first (no end month), then the most recent.
        profile.Experiences = [.. profile.Experiences
            .OrderBy(e => e.EndMonth is not null).ThenByDescending(e => e.EndMonth).ThenByDescending(e => e.StartMonth)];
        profile.Educations = [.. profile.Educations
            .OrderBy(e => e.EndMonth is not null).ThenByDescending(e => e.EndMonth).ThenByDescending(e => e.StartMonth)];
        profile.Projects = [.. profile.Projects
            .OrderBy(p => p.StartMonth is null).ThenBy(p => p.EndMonth is not null)
            .ThenByDescending(p => p.EndMonth).ThenByDescending(p => p.StartMonth)];
        profile.Certifications = [.. profile.Certifications
            .OrderBy(c => c.IssuedMonth is null).ThenByDescending(c => c.IssuedMonth)];
        profile.Skills = [.. profile.Skills.OrderByDescending(s => s.Level).ThenBy(s => s.Skill.Name)];
        profile.Languages = [.. profile.Languages.OrderByDescending(l => l.Spoken).ThenByDescending(l => l.Written).ThenBy(l => l.Name)];
        profile.References = [.. profile.References.OrderBy(r => r.Position).ThenBy(r => r.Id)];
        profile.JobRoles = [.. profile.JobRoles.OrderBy(r => r.Name)];
        foreach (var bullets in profile.Experiences.Select(e => e.Bullets)
                     .Concat(profile.Educations.Select(e => e.Bullets))
                     .Concat(profile.Certifications.Select(c => c.Bullets))
                     .Concat(profile.Projects.Select(p => p.Bullets)))
            SortBullets(bullets);
        return profile;
    }

    // In place, so the tracked collection stays the same list. Id breaks ties if two share a position.
    internal static void SortBullets(List<ProfileBullet> bullets) =>
        bullets.Sort((a, b) => a.Position != b.Position ? a.Position.CompareTo(b.Position) : a.Id.CompareTo(b.Id));
}

[ObjectType<Profile>]
public static partial class ProfileNode
{
    static partial void Configure(IObjectTypeDescriptor<Profile> descriptor)
    {
        descriptor.Ignore(p => p.UserId);
        descriptor.Ignore(p => p.LocationId);
        descriptor.Ignore(p => p.WorkModes);
        descriptor.Ignore(p => p.EmploymentTypes);
        descriptor.Ignore(p => p.Translations);
    }

    public static IEnumerable<ProfileTranslation> GetTranslations([Parent] Profile profile) =>
        profile.Translations.OrderBy(t => t.Language);

    public static IEnumerable<WorkMode> GetWorkModes([Parent] Profile profile) =>
        profile.WorkModes.Select(w => w.WorkMode).Order();

    public static IEnumerable<EmploymentType> GetEmploymentTypes([Parent] Profile profile) =>
        profile.EmploymentTypes.Select(t => t.EmploymentType).Order();

    // Target cities live on the location lookup (saveLocation), so there's one switch for them, not two.
    public static async Task<List<Location>> GetTargetLocationsAsync(AppDbContext db, CancellationToken ct) =>
        await db.Locations.Where(l => l.IsTarget).OrderBy(l => l.CountryCode).ThenBy(l => l.NormalizedCity).ToListAsync(ct);
}

[ObjectType<ProfileExperience>]
public static partial class ProfileExperienceNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileExperience> descriptor)
    {
        descriptor.Ignore(e => e.UserId);
        descriptor.Ignore(e => e.ProfileId);
        descriptor.Ignore(e => e.Profile);
        descriptor.Ignore(e => e.LocationId);
    }
}

[ObjectType<ProfileEducation>]
public static partial class ProfileEducationNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileEducation> descriptor)
    {
        descriptor.Ignore(e => e.UserId);
        descriptor.Ignore(e => e.ProfileId);
        descriptor.Ignore(e => e.Profile);
        descriptor.Ignore(e => e.LocationId);
    }
}

[ObjectType<ProfileCertification>]
public static partial class ProfileCertificationNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileCertification> descriptor)
    {
        descriptor.Ignore(c => c.UserId);
        descriptor.Ignore(c => c.ProfileId);
        descriptor.Ignore(c => c.Profile);
    }
}

[ObjectType<ProfileProject>]
public static partial class ProfileProjectNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileProject> descriptor)
    {
        descriptor.Ignore(p => p.UserId);
        descriptor.Ignore(p => p.ProfileId);
        descriptor.Ignore(p => p.Profile);
        descriptor.Ignore(p => p.LocationId);
    }
}

[ObjectType<ProfileReference>]
public static partial class ProfileReferenceNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileReference> descriptor)
    {
        descriptor.Ignore(r => r.UserId);
        descriptor.Ignore(r => r.ProfileId);
        descriptor.Ignore(r => r.Profile);
    }
}

[ObjectType<ProfileBullet>]
public static partial class ProfileBulletNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileBullet> descriptor)
    {
        descriptor.Ignore(b => b.UserId);
        descriptor.Ignore(b => b.ExperienceId);
        descriptor.Ignore(b => b.EducationId);
        descriptor.Ignore(b => b.CertificationId);
        descriptor.Ignore(b => b.ProjectId);
        descriptor.Ignore(b => b.Translations);
    }

    public static IEnumerable<ProfileBulletTranslation> GetTranslations([Parent] ProfileBullet bullet) =>
        bullet.Translations.OrderBy(t => t.Language);
}

[ObjectType<ProfileBulletTranslation>]
public static partial class ProfileBulletTranslationNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileBulletTranslation> descriptor)
    {
        descriptor.Ignore(t => t.UserId);
        descriptor.Ignore(t => t.BulletId);
        descriptor.Ignore(t => t.Bullet);
    }
}

[ObjectType<ProfileTranslation>]
public static partial class ProfileTranslationNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileTranslation> descriptor)
    {
        descriptor.Ignore(t => t.UserId);
        descriptor.Ignore(t => t.ProfileId);
        descriptor.Ignore(t => t.Profile);
    }
}

[ObjectType<ProfileSkill>]
public static partial class ProfileSkillNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileSkill> descriptor)
    {
        descriptor.Ignore(s => s.ProfileId);
        descriptor.Ignore(s => s.Profile);
        descriptor.Ignore(s => s.SkillId);
    }

    public static async Task<int> GetExperienceMonthsAsync(
        [Parent] ProfileSkill skill, IExperienceMonthsBySkillIdDataLoader experienceMonths, CancellationToken ct) =>
        await experienceMonths.LoadAsync(skill.SkillId, ct);
}

internal static class ProfileDataLoaders
{
    // Computed rather than stored, so it can't go stale when an entry changes. Merged in C#: interval merging isn't
    // practical in SQLite, and a profile has a handful of jobs.
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetExperienceMonthsBySkillIdAsync(
        IReadOnlyList<int> skillIds, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ranges = await db.ProfileExperiences
            .SelectMany(e => e.Skills.Where(s => skillIds.Contains(s.Id)), (e, s) => new { SkillId = s.Id, e.StartMonth, e.EndMonth })
            .ToListAsync(ct);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return ranges
            .GroupBy(r => r.SkillId)
            .ToDictionary(g => g.Key, g => CountMonths(g.Select(r => (MonthIndex(r.StartMonth), MonthIndex(r.EndMonth ?? today)))));
    }

    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;

    // Months covered by the ranges (both ends inclusive), overlaps counted once.
    internal static int CountMonths(IEnumerable<(int Start, int End)> ranges)
    {
        var total = 0;
        var coveredUntil = int.MinValue;
        foreach (var (start, end) in ranges.OrderBy(r => r.Start))
        {
            var from = Math.Max(start, coveredUntil + 1);
            if (end >= from) total += end - from + 1;
            coveredUntil = Math.Max(coveredUntil, end);
        }
        return total;
    }
}

[ObjectType<ProfileLanguage>]
public static partial class ProfileLanguageNode
{
    static partial void Configure(IObjectTypeDescriptor<ProfileLanguage> descriptor)
    {
        descriptor.Ignore(l => l.ProfileId);
        descriptor.Ignore(l => l.Profile);
    }
}
