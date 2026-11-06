using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

// Only an argument to addProfileBullet; never stored. A bullet's own foreign key says which entry it belongs to.
public enum ProfileEntryKind { Experience, Education, Certification, Project }

// Four mutations shared by every entry kind: the bodies would be identical per kind.
[MutationType]
public static class ProfileBulletMutations
{
    internal const int MaxTextLength = 500;
    internal const int MaxPerEntry = 20;

    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    public static async Task<ProfileBullet> AddProfileBulletAsync(
        ProfileEntryKind kind, int entryId, string text, AppDbContext db, CancellationToken ct)
    {
        var cleanText = ProfileMutations.Required(text, "Bullet", MaxTextLength);
        // The user filter hides other users' entries, so a foreign id is "not found" like an unknown one.
        var userId = await (kind switch
        {
            ProfileEntryKind.Experience => db.ProfileExperiences.Where(e => e.Id == entryId).Select(e => e.UserId),
            ProfileEntryKind.Education => db.ProfileEducations.Where(e => e.Id == entryId).Select(e => e.UserId),
            ProfileEntryKind.Certification => db.ProfileCertifications.Where(c => c.Id == entryId).Select(c => c.UserId),
            _ => db.ProfileProjects.Where(p => p.Id == entryId).Select(p => p.UserId),
        }).FirstOrDefaultAsync(ct) ?? throw new ProfileEntryNotFoundException(kind.ToString(), entryId);

        var bullet = new ProfileBullet
        {
            UserId = userId,
            Text = cleanText,
            ExperienceId = kind == ProfileEntryKind.Experience ? entryId : null,
            EducationId = kind == ProfileEntryKind.Education ? entryId : null,
            CertificationId = kind == ProfileEntryKind.Certification ? entryId : null,
            ProjectId = kind == ProfileEntryKind.Project ? entryId : null,
        };
        var count = await SameEntry(bullet, db).CountAsync(ct);
        if (count >= MaxPerEntry)
            throw new InvalidProfileException($"An entry can have at most {MaxPerEntry} bullets.");
        bullet.Position = count;
        db.ProfileBullets.Add(bullet);
        await db.SaveChangesAsync(ct);
        return bullet;
    }

    // Keeps the id and position: editing text must never change what a bullet is.
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    public static async Task<ProfileBullet> UpdateProfileBulletAsync(
        int id, string text, AppDbContext db, CancellationToken ct)
    {
        var bullet = await db.ProfileBullets.Include(b => b.Translations).FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Bullet", id);
        bullet.Text = ProfileMutations.Required(text, "Bullet", MaxTextLength);
        await db.SaveChangesAsync(ct);
        return bullet;
    }

    // Returns the entry's whole list, because every bullet between the old and new place changes position.
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    [UseMutationConvention(PayloadFieldName = "bullets")]
    public static async Task<List<ProfileBullet>> MoveProfileBulletAsync(
        int id, int position, AppDbContext db, CancellationToken ct)
    {
        var bullet = await db.ProfileBullets.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Bullet", id);
        var bullets = await SameEntry(bullet, db).Include(b => b.Translations).ToListAsync(ct);
        ProfileQueries.SortBullets(bullets);
        if (position < 0 || position >= bullets.Count)
            throw new InvalidProfileException($"Position must be 0–{bullets.Count - 1}.");
        Positions.Move(bullets, bullet, position, (b, i) => b.Position = i);
        await db.SaveChangesAsync(ct);
        return bullets;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileBulletAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var bullet = await db.ProfileBullets.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Bullet", id);
        var rest = await SameEntry(bullet, db).Where(b => b.Id != id).ToListAsync(ct);
        ProfileQueries.SortBullets(rest);
        Positions.Renumber(rest, (b, i) => b.Position = i);
        db.ProfileBullets.Remove(bullet);
        await db.SaveChangesAsync(ct);
        return id;
    }

    // Exactly one of the four foreign keys is set, so equal-or-both-null on all four means the same entry.
    private static IQueryable<ProfileBullet> SameEntry(ProfileBullet bullet, AppDbContext db) =>
        db.ProfileBullets.Where(b => b.ExperienceId == bullet.ExperienceId && b.EducationId == bullet.EducationId
            && b.CertificationId == bullet.CertificationId && b.ProjectId == bullet.ProjectId);
}
