using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using static JobHunt.Api.Features.Profiles.ProfileMutations;

namespace JobHunt.Api.Features.Profiles;

[MutationType]
public static class ProfileReferenceMutations
{
    private const int MaxReferences = 10;
    private const int MaxNoteLength = 500;

    [Error<InvalidProfileException>]
    public static async Task<ProfileReference> AddProfileReferenceAsync(
        string name, string relationship, string? company, string? email, string? phone, string? note,
        AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        var reference = new ProfileReference { UserId = profile.UserId, ProfileId = profile.Id };
        Apply(reference, name, relationship, company, email, phone, note);
        var count = await db.ProfileReferences.CountAsync(ct);
        if (count >= MaxReferences)
            throw new InvalidProfileException($"A profile can have at most {MaxReferences} references.");
        reference.Position = count;
        db.ProfileReferences.Add(reference);
        await db.SaveChangesAsync(ct);
        return reference;
    }

    // Keeps the id and position, like updateProfileBullet.
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    public static async Task<ProfileReference> UpdateProfileReferenceAsync(
        int id, string name, string relationship, string? company, string? email, string? phone, string? note,
        AppDbContext db, CancellationToken ct)
    {
        var reference = await db.ProfileReferences.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new ProfileEntryNotFoundException("Reference", id);
        Apply(reference, name, relationship, company, email, phone, note);
        await db.SaveChangesAsync(ct);
        return reference;
    }

    // Returns the whole list, because every reference between the old and new place changes position.
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    [UseMutationConvention(PayloadFieldName = "references")]
    public static async Task<List<ProfileReference>> MoveProfileReferenceAsync(
        int id, int position, AppDbContext db, CancellationToken ct)
    {
        var references = await SortedAsync(db, ct);
        var reference = references.Find(r => r.Id == id) ?? throw new ProfileEntryNotFoundException("Reference", id);
        if (position < 0 || position >= references.Count)
            throw new InvalidProfileException($"Position must be 0–{references.Count - 1}.");
        Positions.Move(references, reference, position, (r, i) => r.Position = i);
        await db.SaveChangesAsync(ct);
        return references;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileReferenceAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var references = await SortedAsync(db, ct);
        var reference = references.Find(r => r.Id == id) ?? throw new ProfileEntryNotFoundException("Reference", id);
        references.Remove(reference);
        Positions.Renumber(references, (r, i) => r.Position = i);
        db.ProfileReferences.Remove(reference);
        await db.SaveChangesAsync(ct);
        return id;
    }

    private static void Apply(
        ProfileReference reference, string name, string relationship, string? company, string? email, string? phone,
        string? note)
    {
        reference.Name = Required(name, "Name");
        reference.Relationship = Required(relationship, "Relationship");
        reference.Company = Text(company, "Company");
        reference.Email = Email(email);
        reference.Phone = Text(phone, "Phone");
        reference.Note = Text(note, "Note", MaxNoteLength);
    }

    // The user filter limits this to the caller's references, so a foreign id is "not found".
    private static Task<List<ProfileReference>> SortedAsync(AppDbContext db, CancellationToken ct) =>
        db.ProfileReferences.OrderBy(r => r.Position).ThenBy(r => r.Id).ToListAsync(ct);
}
