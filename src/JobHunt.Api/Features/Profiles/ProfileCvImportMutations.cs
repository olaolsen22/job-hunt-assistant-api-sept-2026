using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Locations;
using Microsoft.EntityFrameworkCore;
using static JobHunt.Api.Features.Profiles.ProfileMutations;

namespace JobHunt.Api.Features.Profiles;

public record ProfileCvBasicsSelection(
    Optional<string?> FullName = default, Optional<string?> Headline = default, Optional<string?> Summary = default,
    Optional<string?> Email = default, Optional<string?> Phone = default, Optional<LocationInput?> Location = default);
public record ProfileCvBulletSelection(string Text, int? Id = null);
public record ProfileCvExperienceSelection(string Employer, string Title, DateOnly? StartMonth,
    DateOnly? EndMonth = null, LocationInput? Location = null, List<string>? SkillNames = null,
    List<ProfileCvBulletSelection>? Bullets = null, int? Id = null);
public record ProfileCvEducationSelection(string School, DateOnly? StartMonth, string? Degree = null,
    string? Field = null, DateOnly? EndMonth = null, LocationInput? Location = null,
    List<ProfileCvBulletSelection>? Bullets = null, int? Id = null);
public record ProfileCvCertificationSelection(string Name, string? Issuer = null, DateOnly? IssuedMonth = null,
    DateOnly? ExpiresMonth = null, string? Url = null, List<ProfileCvBulletSelection>? Bullets = null, int? Id = null);
public record ProfileCvProjectSelection(string Name, string? Url = null, LocationInput? Location = null,
    DateOnly? StartMonth = null, DateOnly? EndMonth = null, List<string>? SkillNames = null,
    List<ProfileCvBulletSelection>? Bullets = null, int? Id = null);
public record ProfileCvImportSelection(Language ContentLanguage, ProfileCvBasicsSelection? Basics = null,
    Optional<List<ProfileLinkInput>?> Links = default, Optional<List<string>?> Hobbies = default,
    List<ProfileCvExperienceSelection>? Experiences = null, List<ProfileCvEducationSelection>? Educations = null,
    List<ProfileCvCertificationSelection>? Certifications = null, List<ProfileCvProjectSelection>? Projects = null,
    List<ProfileCvSkill>? Skills = null, List<ProfileCvLanguage>? Languages = null);

[MutationType]
public static class ProfileCvImportMutations
{
    [Error<InvalidProfileImportException>]
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidLocationException>]
    public static async Task<Profile> ImportProfileCvAsync(
        ProfileCvImportSelection selection, AppDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var profile = await ProfileQueries.LoadAsync(db, ct);
        if (selection.ContentLanguage != Language.English || profile.ContentLanguage != Language.English)
            throw new InvalidProfileImportException("contentLanguage: English import requires an English-primary profile; import never switches its language.");

        ValidateTargets(selection);
        var path = "basics";
        try
        {
            if (selection.Basics is { } basics)
            {
                if (basics.FullName.HasValue) { path = "basics.fullName"; profile.FullName = Text(basics.FullName.Value, path); }
                if (basics.Headline.HasValue) { path = "basics.headline"; profile.Headline = Text(basics.Headline.Value, path); }
                if (basics.Summary.HasValue) { path = "basics.summary"; profile.Summary = Markdown(basics.Summary.Value, path); }
                if (basics.Email.HasValue) { path = "basics.email"; profile.Email = Email(basics.Email.Value); }
                if (basics.Phone.HasValue) { path = "basics.phone"; profile.Phone = Text(basics.Phone.Value, path); }
                if (basics.Location.HasValue)
                {
                    path = "basics.location";
                    profile.Location = basics.Location.Value is { } location ? await LocationLookup.FindOrCreateAsync(location, db, ct) : null;
                    if (profile.Location is null) profile.LocationId = null;
                }
            }
            if (selection.Links.HasValue)
            {
                path = "links";
                profile.Links = ValidateLinks(selection.Links.Value ?? throw new InvalidProfileImportException("links: use [] to clear the list, not null."));
            }
            if (selection.Hobbies.HasValue)
            {
                path = "hobbies";
                profile.Hobbies = ValidateHobbies(selection.Hobbies.Value ?? throw new InvalidProfileImportException("hobbies: use [] to clear the list, not null."));
            }

            for (var i = 0; i < (selection.Experiences?.Count ?? 0); i++)
            {
                var input = selection.Experiences![i]; path = $"experiences[{i}]";
                var entry = input.Id is { } id ? profile.Experiences.FirstOrDefault(e => e.Id == id)
                    ?? throw new ProfileEntryNotFoundException("Experience", id)
                    : new ProfileExperience { UserId = profile.UserId, ProfileId = profile.Id };
                await ProfileEntryMutations.ApplyAsync(entry, input.Employer, input.Title, input.Location,
                    input.StartMonth ?? throw new InvalidProfileImportException(path + ".startMonth: a reviewed month is required."),
                    input.EndMonth, input.SkillNames ?? [], db, ct);
                ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, path);
                if (input.Id is null) db.ProfileExperiences.Add(entry);
            }
            for (var i = 0; i < (selection.Educations?.Count ?? 0); i++)
            {
                var input = selection.Educations![i]; path = $"educations[{i}]";
                var entry = input.Id is { } id ? profile.Educations.FirstOrDefault(e => e.Id == id)
                    ?? throw new ProfileEntryNotFoundException("Education", id)
                    : new ProfileEducation { UserId = profile.UserId, ProfileId = profile.Id };
                await ProfileEntryMutations.ApplyAsync(entry, input.School, input.Degree, input.Field, input.Location,
                    input.StartMonth ?? throw new InvalidProfileImportException(path + ".startMonth: a reviewed month is required."),
                    input.EndMonth, db, ct);
                ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, path);
                if (input.Id is null) db.ProfileEducations.Add(entry);
            }
            for (var i = 0; i < (selection.Certifications?.Count ?? 0); i++)
            {
                var input = selection.Certifications![i]; path = $"certifications[{i}]";
                var entry = input.Id is { } id ? profile.Certifications.FirstOrDefault(e => e.Id == id)
                    ?? throw new ProfileEntryNotFoundException("Certification", id)
                    : new ProfileCertification { UserId = profile.UserId, ProfileId = profile.Id };
                ProfileEntryMutations.Apply(entry, input.Name, input.Issuer, input.IssuedMonth, input.ExpiresMonth, input.Url);
                ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, path);
                if (input.Id is null) db.ProfileCertifications.Add(entry);
            }
            for (var i = 0; i < (selection.Projects?.Count ?? 0); i++)
            {
                var input = selection.Projects![i]; path = $"projects[{i}]";
                var entry = input.Id is { } id ? profile.Projects.FirstOrDefault(e => e.Id == id)
                    ?? throw new ProfileEntryNotFoundException("Project", id)
                    : new ProfileProject { UserId = profile.UserId, ProfileId = profile.Id };
                await ProfileEntryMutations.ApplyAsync(entry, input.Name, input.Url, input.Location,
                    input.StartMonth, input.EndMonth, input.SkillNames ?? [], db, ct);
                ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, path);
                if (input.Id is null) db.ProfileProjects.Add(entry);
            }
            for (var i = 0; i < (selection.Skills?.Count ?? 0); i++)
            {
                var input = selection.Skills![i]; path = $"skills[{i}]";
                if (input.Level is not (>= 1 and <= 5)) throw new InvalidProfileImportException(path + ".level: choose a reviewed level from 1 to 5.");
                var skill = (await FindOrCreateSkillsAsync([Required(input.Name, "Skill name")], profile.UserId, db, ct))[0];
                var level = db.ProfileSkills.Local.FirstOrDefault(s => s.ProfileId == profile.Id
                    && (s.Skill == skill || (skill.Id != 0 && s.SkillId == skill.Id)));
                if (level is null) db.ProfileSkills.Add(level = new ProfileSkill { ProfileId = profile.Id, Skill = skill });
                level.Level = input.Level.Value;
            }
            for (var i = 0; i < (selection.Languages?.Count ?? 0); i++)
            {
                var input = selection.Languages![i]; path = $"languages[{i}]";
                if (input.Level is null) throw new InvalidProfileImportException(path + ".level: choose a reviewed CEFR level.");
                var language = profile.Languages.FirstOrDefault(l => l.Language == input.Language);
                if (language is null) profile.Languages.Add(language = new ProfileLanguage { ProfileId = profile.Id, Language = input.Language });
                language.Level = input.Level.Value;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await ProfileQueries.LoadAsync(db, ct);
        }
        catch (InvalidProfileException ex)
        {
            throw new InvalidProfileImportException($"{path}: {ex.Message}");
        }
        catch (InvalidLocationException ex)
        {
            throw new InvalidLocationException($"{path}.location: {ex.Message}");
        }
    }

    private static void ApplyBullets(List<ProfileBullet> existing, List<ProfileCvBulletSelection>? selected, string userId, string path)
    {
        if (selected is null) return;
        if (existing.Count + selected.Count(b => b.Id is null) > ProfileBulletMutations.MaxPerEntry)
            throw new InvalidProfileImportException(path + ".bullets: an entry can have at most 20 bullets.");
        for (var i = 0; i < selected.Count; i++)
        {
            var input = selected[i];
            var text = Required(input.Text, $"{path}.bullets[{i}].text", ProfileBulletMutations.MaxTextLength);
            if (input.Id is { } id)
            {
                var bullet = existing.FirstOrDefault(b => b.Id == id) ?? throw new ProfileEntryNotFoundException("Bullet", id);
                bullet.Text = text;
            }
            else existing.Add(new ProfileBullet { UserId = userId, Text = text, Position = existing.Count });
        }
    }

    private static void ValidateTargets(ProfileCvImportSelection selection)
    {
        Check(selection.Experiences, "experiences", e => e.Id);
        Check(selection.Educations, "educations", e => e.Id);
        Check(selection.Certifications, "certifications", e => e.Id);
        Check(selection.Projects, "projects", e => e.Id);
        Check(selection.Skills, "skills", e => e.Name is null ? null : Names.Normalize(e.Name), 200);
        Check(selection.Languages, "languages", e => e.Language.ToString(), 2);
        var bullets = (selection.Experiences?.SelectMany(e => e.Bullets ?? []) ?? [])
            .Concat(selection.Educations?.SelectMany(e => e.Bullets ?? []) ?? [])
            .Concat(selection.Certifications?.SelectMany(e => e.Bullets ?? []) ?? [])
            .Concat(selection.Projects?.SelectMany(e => e.Bullets ?? []) ?? []).ToList();
        Check(bullets, "bullets", b => b.Id, 8000);
    }

    private static void Check<T, TKey>(List<T>? list, string path, Func<T, TKey?> key, int limit = 100)
    {
        if (list is null) return;
        if (list.Count > limit) throw new InvalidProfileImportException($"{path}: maximum {limit} selections.");
        var seen = new HashSet<TKey>();
        foreach (var item in list)
            if (key(item) is { } id && !seen.Add(id))
                throw new InvalidProfileImportException($"{path}: repeated target.");
    }
}
