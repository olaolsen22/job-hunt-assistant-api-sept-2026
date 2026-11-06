using JobHunt.Api.Data;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
using static JobHunt.Api.Features.Profiles.ProfileMutations;

namespace JobHunt.Api.Features.Profiles;

// Add/update/delete per entry, so editing one job doesn't resend the whole history. Update is a full replace.
[MutationType]
public static class ProfileEntryMutations
{
    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileExperience> AddProfileExperienceAsync(
        string employer, string title, LocationInput? location, DateOnly startMonth, DateOnly? endMonth,
        List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        var experience = new ProfileExperience { UserId = profile.UserId, ProfileId = profile.Id };
        await ApplyAsync(experience, employer, title, location, startMonth, endMonth, skillNames, db, ct);
        db.ProfileExperiences.Add(experience);
        await db.SaveChangesAsync(ct);
        return experience;
    }

    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileExperience> UpdateProfileExperienceAsync(
        int id, string employer, string title, LocationInput? location, DateOnly startMonth, DateOnly? endMonth,
        List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        var experience = await db.ProfileExperiences.Include(e => e.Skills).Include(e => e.Bullets).Include(e => e.Translations)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Experience", id);
        await ApplyAsync(experience, employer, title, location, startMonth, endMonth, skillNames, db, ct);
        await db.SaveChangesAsync(ct);
        ProfileQueries.SortBullets(experience.Bullets);
        return experience;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileExperienceAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileExperiences.Where(e => e.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Experience", id);
        return id;
    }

    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileEducation> AddProfileEducationAsync(
        string school, string? degree, string? field, LocationInput? location, DateOnly startMonth, DateOnly? endMonth,
        AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        var education = new ProfileEducation { UserId = profile.UserId, ProfileId = profile.Id };
        await ApplyAsync(education, school, degree, field, location, startMonth, endMonth, db, ct);
        db.ProfileEducations.Add(education);
        await db.SaveChangesAsync(ct);
        return education;
    }

    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileEducation> UpdateProfileEducationAsync(
        int id, string school, string? degree, string? field, LocationInput? location, DateOnly startMonth, DateOnly? endMonth,
        AppDbContext db, CancellationToken ct)
    {
        var education = await db.ProfileEducations.Include(e => e.Bullets).Include(e => e.Translations).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Education", id);
        await ApplyAsync(education, school, degree, field, location, startMonth, endMonth, db, ct);
        await db.SaveChangesAsync(ct);
        ProfileQueries.SortBullets(education.Bullets);
        return education;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileEducationAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileEducations.Where(e => e.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Education", id);
        return id;
    }

    [Error<InvalidProfileException>]
    public static async Task<ProfileCertification> AddProfileCertificationAsync(
        string name, string? issuer, DateOnly? issuedMonth, DateOnly? expiresMonth, string? url,
        AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        var certification = new ProfileCertification { UserId = profile.UserId, ProfileId = profile.Id };
        Apply(certification, name, issuer, issuedMonth, expiresMonth, url);
        db.ProfileCertifications.Add(certification);
        await db.SaveChangesAsync(ct);
        return certification;
    }

    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    public static async Task<ProfileCertification> UpdateProfileCertificationAsync(
        int id, string name, string? issuer, DateOnly? issuedMonth, DateOnly? expiresMonth, string? url,
        AppDbContext db, CancellationToken ct)
    {
        var certification = await db.ProfileCertifications.Include(c => c.Bullets).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Certification", id);
        Apply(certification, name, issuer, issuedMonth, expiresMonth, url);
        await db.SaveChangesAsync(ct);
        ProfileQueries.SortBullets(certification.Bullets);
        return certification;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileCertificationAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileCertifications.Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Certification", id);
        return id;
    }

    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileProject> AddProfileProjectAsync(
        string name, string? url, LocationInput? location, DateOnly? startMonth, DateOnly? endMonth,
        ProjectKind? kind, List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        var project = new ProfileProject { UserId = profile.UserId, ProfileId = profile.Id };
        await ApplyAsync(project, name, url, location, startMonth, endMonth, kind, skillNames, db, ct);
        db.ProfileProjects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    [Error<InvalidLocationException>]
    public static async Task<ProfileProject> UpdateProfileProjectAsync(
        int id, string name, string? url, LocationInput? location, DateOnly? startMonth, DateOnly? endMonth,
        ProjectKind? kind, List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        var project = await db.ProfileProjects.Include(p => p.Skills).Include(p => p.Bullets).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Project", id);
        await ApplyAsync(project, name, url, location, startMonth, endMonth, kind, skillNames, db, ct);
        await db.SaveChangesAsync(ct);
        ProfileQueries.SortBullets(project.Bullets);
        return project;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileProjectAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileProjects.Where(p => p.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Project", id);
        return id;
    }

    internal static async Task ApplyAsync(
        ProfileExperience experience, string employer, string title, LocationInput? location, DateOnly startMonth,
        DateOnly? endMonth, List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        experience.Employer = Required(employer, "Employer");
        experience.Title = Required(title, "Title");
        // Leaving it out clears it. Sets the FK because the unloaded navigation wouldn't register a change to null.
        if (location is null)
            experience.LocationId = null;
        else
            experience.Location = await LocationLookup.FindOrCreateAsync(location, db, ct);
        (experience.StartMonth, experience.EndMonth) = (Month(startMonth), Month(endMonth));
        CheckOrder(experience.StartMonth, experience.EndMonth, "The end month");
        var skills = await FindOrCreateSkillsAsync(skillNames, db, ct);
        experience.Skills.Clear();
        experience.Skills.AddRange(skills);
        AddToProfileSkills(experience.ProfileId, skills, await ProfileSkillIdsAsync(skills, db, ct), db);
    }

    internal static async Task ApplyAsync(
        ProfileEducation education, string school, string? degree, string? field, LocationInput? location,
        DateOnly startMonth, DateOnly? endMonth, AppDbContext db, CancellationToken ct)
    {
        education.School = Required(school, "School");
        education.Degree = Text(degree, "Degree");
        education.Field = Text(field, "Field");
        if (location is null)
            education.LocationId = null;
        else
            education.Location = await LocationLookup.FindOrCreateAsync(location, db, ct);
        (education.StartMonth, education.EndMonth) = (Month(startMonth), Month(endMonth));
        CheckOrder(education.StartMonth, education.EndMonth, "The end month");
    }

    internal static void Apply(
        ProfileCertification certification, string name, string? issuer, DateOnly? issuedMonth, DateOnly? expiresMonth,
        string? url)
    {
        certification.Name = Required(name, "Name");
        certification.Issuer = Text(issuer, "Issuer");
        (certification.IssuedMonth, certification.ExpiresMonth) = (Month(issuedMonth), Month(expiresMonth));
        CheckOrder(certification.IssuedMonth, certification.ExpiresMonth, "The expiry month");
        certification.Url = Url(url);
    }

    internal static async Task ApplyAsync(
        ProfileProject project, string name, string? url, LocationInput? location, DateOnly? startMonth,
        DateOnly? endMonth, ProjectKind? kind, List<string> skillNames, AppDbContext db, CancellationToken ct)
    {
        project.Name = Required(name, "Name");
        project.Url = Url(url);
        project.Kind = kind;
        if (location is null)
            project.LocationId = null;
        else
            project.Location = await LocationLookup.FindOrCreateAsync(location, db, ct);
        (project.StartMonth, project.EndMonth) = (Month(startMonth), Month(endMonth));
        CheckOrder(project.StartMonth, project.EndMonth, "The end month");
        var skills = await FindOrCreateSkillsAsync(skillNames, db, ct);
        project.Skills.Clear();
        project.Skills.AddRange(skills);
        AddToProfileSkills(project.ProfileId, skills, await ProfileSkillIdsAsync(skills, db, ct), db);
    }

    private static async Task<List<int>> ProfileSkillIdsAsync(List<Skill> skills, AppDbContext db, CancellationToken ct)
    {
        var ids = skills.Select(s => s.Id).ToList();
        return await db.ProfileSkills.Where(s => ids.Contains(s.SkillId)).Select(s => s.SkillId).ToListAsync(ct);
    }

    // A new profile skill starts at the lowest level. Existing levels stay, and untagging never removes one.
    private static void AddToProfileSkills(int profileId, List<Skill> skills, List<int> existingIds, AppDbContext db)
    {
        foreach (var skill in skills.Where(s => (s.Id == 0 || !existingIds.Contains(s.Id))
            && !db.ProfileSkills.Local.Any(p => p.ProfileId == profileId && (p.Skill == s || (s.Id != 0 && p.SkillId == s.Id)))))
            db.ProfileSkills.Add(new ProfileSkill { ProfileId = profileId, Skill = skill, Level = 1 });
    }

    private static void CheckOrder(DateOnly? start, DateOnly? end, string what)
    {
        if (end < start)
            throw new InvalidProfileException($"{what} is before the start.");
    }
}
