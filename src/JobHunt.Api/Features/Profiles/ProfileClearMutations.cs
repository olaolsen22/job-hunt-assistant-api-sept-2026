using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

// CV content and the cover letter story: personal details, links, preferences and the content language are kept.
public enum ProfileSection { Summary, Experiences, Educations, Certifications, Projects, Skills, Languages, References, Hobbies, Story }

[MutationType]
public static class ProfileClearMutations
{
    // One transaction, so a wipe never stops halfway. Bullets go with their entries; CV drafts are snapshots and stay.
    [Error<InvalidProfileException>]
    public static async Task<Profile> ClearProfileSectionsAsync(
        List<ProfileSection> sections, AppDbContext db, CancellationToken ct)
    {
        if (sections.Count == 0)
            throw new InvalidProfileException("Choose at least one section to clear.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var profile = await db.Profiles.SingleAsync(ct);
        if (sections.Contains(ProfileSection.Summary))
        {
            profile.Headline = null;
            profile.Summary = null;
            // A translation only holds the headline and summary.
            await db.ProfileTranslations.ExecuteDeleteAsync(ct);
        }
        if (sections.Contains(ProfileSection.Hobbies)) profile.Hobbies = [];
        if (sections.Contains(ProfileSection.Story)) profile.Story = new();
        if (sections.Contains(ProfileSection.Experiences)) await db.ProfileExperiences.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.Educations)) await db.ProfileEducations.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.Certifications)) await db.ProfileCertifications.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.Projects)) await db.ProfileProjects.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.Skills)) await db.ProfileSkills.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.Languages)) await db.ProfileLanguages.ExecuteDeleteAsync(ct);
        if (sections.Contains(ProfileSection.References)) await db.ProfileReferences.ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await ProfileQueries.LoadAsync(db, ct);
    }
}
