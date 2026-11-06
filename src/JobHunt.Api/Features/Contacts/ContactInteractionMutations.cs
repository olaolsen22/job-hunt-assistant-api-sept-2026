using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Contacts;

public record ContactInteractionInput(
    DateTimeOffset OccurredAt, ContactDirection Direction, ContactChannel Channel, string? Note, DateOnly? FollowUpOn);

public class InvalidContactInteractionException(string message) : Exception(message);

public class ContactInteractionNotFoundException(int id) : Exception($"Interaction {id} was not found.");

[MutationType]
public static class ContactInteractionMutations
{
    [Error<ContactNotFoundException>]
    [Error<InvalidContactInteractionException>]
    public static async Task<ContactInteraction> AddContactInteractionAsync(
        int contactId, ContactInteractionInput interaction, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var contact = await db.Contacts.Include(c => c.Interactions).FirstOrDefaultAsync(c => c.Id == contactId, ct)
            ?? throw new ContactNotFoundException(contactId);
        var now = time.GetUtcNow().UtcDateTime;
        var entity = new ContactInteraction { UserId = contact.UserId, CreatedAt = now };
        Apply(entity, interaction, now);
        contact.Interactions.Add(entity);
        contact.Refresh();
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Full replace.
    [Error<ContactInteractionNotFoundException>]
    [Error<InvalidContactInteractionException>]
    public static async Task<ContactInteraction> UpdateContactInteractionAsync(
        int id, ContactInteractionInput interaction, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await db.ContactInteractions.Include(i => i.Contact).ThenInclude(c => c.Interactions)
                .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new ContactInteractionNotFoundException(id);
        Apply(entity, interaction, time.GetUtcNow().UtcDateTime);
        entity.Contact.Refresh();
        await db.SaveChangesAsync(ct);
        return entity;
    }

    [Error<ContactInteractionNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteContactInteractionAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var entity = await db.ContactInteractions.Include(i => i.Contact).ThenInclude(c => c.Interactions)
                .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new ContactInteractionNotFoundException(id);
        entity.Contact.Interactions.Remove(entity);
        db.ContactInteractions.Remove(entity);
        entity.Contact.Refresh();
        await db.SaveChangesAsync(ct);
        return id;
    }

    private static void Apply(ContactInteraction entity, ContactInteractionInput input, DateTime now)
    {
        var occurredAt = input.OccurredAt.UtcDateTime;
        if (occurredAt > now)
            throw new InvalidContactInteractionException("The date can't be in the future.");
        // Compared in UTC, like every stored date; a follow-up on the same day is fine.
        if (input.FollowUpOn < DateOnly.FromDateTime(occurredAt))
            throw new InvalidContactInteractionException("The follow-up date can't be before the interaction.");
        var note = Inputs.Clean(input.Note);
        if (note?.Length > Inputs.MaxMarkdownLength)
            throw new InvalidContactInteractionException($"The note is longer than {Inputs.MaxMarkdownLength} characters.");

        entity.OccurredAt = occurredAt;
        entity.Direction = input.Direction;
        entity.Channel = input.Channel;
        entity.Note = note;
        entity.FollowUpOn = input.FollowUpOn;
    }
}

[ObjectType<ContactInteraction>]
public static partial class ContactInteractionNode
{
    static partial void Configure(IObjectTypeDescriptor<ContactInteraction> descriptor)
    {
        descriptor.Ignore(i => i.UserId);
    }
}
