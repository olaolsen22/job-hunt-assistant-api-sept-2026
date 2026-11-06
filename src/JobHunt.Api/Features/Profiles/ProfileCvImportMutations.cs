using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Skills;
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
    // Where the import has got to, so an error can name the field.
    private sealed class ImportPath { public string Value { get; set; } = "basics"; }

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
        var at = new ImportPath();
        try
        {
            await ApplyBasicsAsync(selection, profile, at, db, ct);
            await ApplyExperiencesAsync(selection.Experiences, profile, at, db, ct);
            await ApplyEducationsAsync(selection.Educations, profile, at, db, ct);
            ApplyCertifications(selection.Certifications, profile, at, db);
            await ApplyProjectsAsync(selection.Projects, profile, at, db, ct);
            await ApplySkillsAsync(selection.Skills, profile, at, db, ct);
            ApplyLanguages(selection.Languages, profile, at);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await ProfileQueries.LoadAsync(db, ct);
        }
        catch (InvalidProfileException ex)
        {
            throw new InvalidProfileImportException($"{at.Value}: {ex.Message}");
        }
        catch (InvalidLocationException ex)
        {
            throw new InvalidLocationException($"{at.Value}.location: {ex.Message}");
        }
    }

    private static async Task ApplyBasicsAsync(ProfileCvImportSelection selection, Profile profile, ImportPath at, AppDbContext db, CancellationToken ct)
    {
        if (selection.Basics is { } basics)
        {
            if (basics.FullName.HasValue) { at.Value = "basics.fullName"; profile.FullName = Text(basics.FullName.Value, at.Value); }
            if (basics.Headline.HasValue) { at.Value = "basics.headline"; profile.Headline = Text(basics.Headline.Value, at.Value); }
            if (basics.Summary.HasValue) { at.Value = "basics.summary"; profile.Summary = Markdown(basics.Summary.Value, at.Value); }
            if (basics.Email.HasValue) { at.Value = "basics.email"; profile.Email = Email(basics.Email.Value); }
            if (basics.Phone.HasValue) { at.Value = "basics.phone"; profile.Phone = Text(basics.Phone.Value, at.Value); }
            if (basics.Location.HasValue)
            {
                at.Value = "basics.location";
                profile.Location = basics.Location.Value is { } location ? await LocationLookup.FindOrCreateAsync(location, db, ct) : null;
                if (profile.Location is null) profile.LocationId = null;
            }
        }
        if (selection.Links.HasValue)
        {
            at.Value = "links";
            profile.Links = ValidateLinks(selection.Links.Value ?? throw new InvalidProfileImportException("links: use [] to clear the list, not null."));
        }
        if (selection.Hobbies.HasValue)
        {
            at.Value = "hobbies";
            profile.Hobbies = ValidateHobbies(selection.Hobbies.Value ?? throw new InvalidProfileImportException("hobbies: use [] to clear the list, not null."));
        }
    }

    private static async Task ApplyExperiencesAsync(List<ProfileCvExperienceSelection>? inputs, Profile profile, ImportPath at, AppDbContext db, CancellationToken ct)
    {
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            var input = inputs![i]; at.Value = $"experiences[{i}]";
            var entry = input.Id is { } id ? profile.Experiences.FirstOrDefault(e => e.Id == id)
                ?? throw new ProfileEntryNotFoundException("Experience", id)
                : new ProfileExperience { UserId = profile.UserId, ProfileId = profile.Id };
            await ProfileEntryMutations.ApplyAsync(entry, input.Employer, input.Title, input.Location,
                input.StartMonth ?? throw new InvalidProfileImportException(at.Value + ".startMonth: a reviewed month is required."),
                input.EndMonth, input.SkillNames ?? [], db, ct);
            ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, at.Value);
            if (input.Id is null) db.ProfileExperiences.Add(entry);
        }
    }

    private static async Task ApplyEducationsAsync(List<ProfileCvEducationSelection>? inputs, Profile profile, ImportPath at, AppDbContext db, CancellationToken ct)
    {
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            var input = inputs![i]; at.Value = $"educations[{i}]";
            var entry = input.Id is { } id ? profile.Educations.FirstOrDefault(e => e.Id == id)
                ?? throw new ProfileEntryNotFoundException("Education", id)
                : new ProfileEducation { UserId = profile.UserId, ProfileId = profile.Id };
            await ProfileEntryMutations.ApplyAsync(entry, input.School, input.Degree, input.Field, input.Location,
                input.StartMonth ?? throw new InvalidProfileImportException(at.Value + ".startMonth: a reviewed month is required."),
                input.EndMonth, db, ct);
            ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, at.Value);
            if (input.Id is null) db.ProfileEducations.Add(entry);
        }
    }

    private static void ApplyCertifications(List<ProfileCvCertificationSelection>? inputs, Profile profile, ImportPath at, AppDbContext db)
    {
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            var input = inputs![i]; at.Value = $"certifications[{i}]";
            var entry = input.Id is { } id ? profile.Certifications.FirstOrDefault(e => e.Id == id)
                ?? throw new ProfileEntryNotFoundException("Certification", id)
                : new ProfileCertification { UserId = profile.UserId, ProfileId = profile.Id };
            ProfileEntryMutations.Apply(entry, input.Name, input.Issuer, input.IssuedMonth, input.ExpiresMonth, input.Url);
            ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, at.Value);
            if (input.Id is null) db.ProfileCertifications.Add(entry);
        }
    }

    private static async Task ApplyProjectsAsync(List<ProfileCvProjectSelection>? inputs, Profile profile, ImportPath at, AppDbContext db, CancellationToken ct)
    {
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            var input = inputs![i]; at.Value = $"projects[{i}]";
            var entry = input.Id is { } id ? profile.Projects.FirstOrDefault(e => e.Id == id)
                ?? throw new ProfileEntryNotFoundException("Project", id)
                : new ProfileProject { UserId = profile.UserId, ProfileId = profile.Id };
            // The import format has no kind: an updated project keeps the one the user chose.
            await ProfileEntryMutations.ApplyAsync(entry, input.Name, input.Url, input.Location,
                input.StartMonth, input.EndMonth, entry.Kind, input.SkillNames ?? [], db, ct);
            ApplyBullets(entry.Bullets, input.Bullets, profile.UserId, at.Value);
            if (input.Id is null) db.ProfileProjects.Add(entry);
        }
    }

    private static async Task ApplySkillsAsync(List<ProfileCvSkill>? inputs, Profile profile, ImportPath at, AppDbContext db, CancellationToken ct)
    {
        if (inputs is null) return;
        var names = new List<string>();
        for (var i = 0; i < inputs.Count; i++)
        {
            at.Value = $"skills[{i}]";
            if (inputs[i].Level is not (>= 1 and <= 5))
                throw new InvalidProfileImportException(at.Value + ".level: choose a reviewed level from 1 to 5.");
            names.Add(Required(inputs[i].Name, "Skill name"));
        }

        // Looked up together, so a long skill list costs two queries instead of two per row.
        var skills = (await FindOrCreateSkillsAsync(names, db, ct)).ToDictionary(s => s.NormalizedName);
        var categories = await SkillCategoryPrompt.ByNormalizedNameAsync(inputs.Select(s => s.CategoryName), db, ct);
        foreach (var input in inputs)
        {
            var skill = skills[Names.Normalize(input.Name!)];
            if (skill.CategoryId is null && skill.Category is null && input.CategoryName is { } categoryName)
                skill.Category = categories.GetValueOrDefault(Names.Normalize(categoryName));
            var level = db.ProfileSkills.Local.FirstOrDefault(s => s.ProfileId == profile.Id
                && (s.Skill == skill || (skill.Id != 0 && s.SkillId == skill.Id)));
            if (level is null) db.ProfileSkills.Add(level = new ProfileSkill { ProfileId = profile.Id, Skill = skill });
            level.Level = input.Level!.Value;
        }
    }

    private static void ApplyLanguages(List<ProfileCvLanguage>? inputs, Profile profile, ImportPath at)
    {
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            var input = inputs![i]; at.Value = $"languages[{i}]";
            if (input.Written is null || input.Spoken is null)
                throw new InvalidProfileImportException(at.Value + ": choose reviewed written and spoken levels.");
            var name = LanguageName(input.Name);
            var language = profile.Languages.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (language is null) profile.Languages.Add(language = new ProfileLanguage { ProfileId = profile.Id, Name = name });
            language.Written = input.Written.Value;
            language.Spoken = input.Spoken.Value;
        }
    }

    private static void ApplyBullets(List<ProfileBullet> existing, List<ProfileCvBulletSelection>? selected, string userId, string path)
    {
        if (selected is null) return;
        if (existing.Count + selected.Count(b => b.Id is null) > ProfileBulletMutations.MaxPerEntry)
            throw new InvalidProfileImportException($"{path}.bullets: an entry can have at most {ProfileBulletMutations.MaxPerEntry} bullets.");
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
        Check(selection.Languages, "languages", e => e.Name?.Trim().ToUpperInvariant(), 20);
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
