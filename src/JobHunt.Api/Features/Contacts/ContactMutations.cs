using JobHunt.Api.Data;
using JobHunt.Api.Features.Companies;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Contacts;

// Same shape as the AI's contact rows (plus notes), so parsed JSON passes straight through.
public record ContactInput(string Name, string? Title, string? Email, string? Phone, string? Url, string? Notes = null);

public class InvalidContactException(string message) : Exception(message);

public class ContactNotFoundException(int id) : Exception($"Contact {id} was not found.");

public class DuplicateContactException(int existingContactId)
    : Exception($"This company already has a contact with that name ({existingContactId}).")
{
    public int ExistingContactId { get; } = existingContactId;
}

[MutationType]
public static class ContactMutations
{
    private const int MaxFieldLength = 300;

    [Error<CompanyNotFoundException>]
    [Error<InvalidContactException>]
    [Error<DuplicateContactException>]
    public static async Task<Contact> AddContactAsync(
        int companyId, ContactInput contact, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
            throw new CompanyNotFoundException(companyId);
        var entity = new Contact { UserId = db.CurrentUserId!, CompanyId = companyId };
        await ApplyAsync(entity, contact, db, ct);
        db.Contacts.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Full replace, like updateCompany.
    [Error<ContactNotFoundException>]
    [Error<InvalidContactException>]
    [Error<DuplicateContactException>]
    public static async Task<Contact> UpdateContactAsync(
        int id, ContactInput contact, AppDbContext db, CancellationToken ct)
    {
        var entity = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ContactNotFoundException(id);
        await ApplyAsync(entity, contact, db, ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Links to listings go with it (cascade); the listings stay.
    [Error<ContactNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteContactAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.Contacts.Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ContactNotFoundException(id);
        return id;
    }

    private static async Task ApplyAsync(Contact entity, ContactInput input, AppDbContext db, CancellationToken ct)
    {
        if (Problem(input) is { } problem) throw new InvalidContactException(problem);
        var normalizedName = Names.Normalize(input.Name);
        var existingId = await db.Contacts
            .Where(c => c.CompanyId == entity.CompanyId && c.NormalizedName == normalizedName && c.Id != entity.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (existingId is not null)
            throw new DuplicateContactException(existingId.Value);

        entity.Name = input.Name.Trim();
        entity.NormalizedName = normalizedName;
        entity.Title = Inputs.Clean(input.Title);
        entity.Email = Inputs.Clean(input.Email);
        entity.Phone = Inputs.Clean(input.Phone);
        entity.Url = Inputs.Clean(input.Url);
        entity.Notes = Inputs.Clean(input.Notes);
    }

    // Null when valid. Listing and company input use it too, each throwing its own error type.
    internal static string? Problem(ContactInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Contact name is required.";
        string?[] fields = [input.Name, input.Title, input.Email, input.Phone, input.Url];
        if (fields.Any(f => f?.Trim().Length > MaxFieldLength))
            return $"Contact \"{input.Name.Trim()}\": fields are limited to {MaxFieldLength} characters.";
        // Only a sanity check: ads write emails in all sorts of ways ("kari (at) acme.no").
        if (Inputs.Clean(input.Email) is { } email && !email.Contains('@'))
            return $"Contact \"{input.Name.Trim()}\": email must contain @.";
        if (Inputs.Clean(input.Url) is { } url && !Inputs.TryHttpUrl(url, out _))
            return $"Contact \"{input.Name.Trim()}\": url must be an http(s) link.";
        if (input.Notes?.Length > Inputs.MaxMarkdownLength)
            return $"Contact notes are longer than {Inputs.MaxMarkdownLength} characters.";
        return null;
    }

    // The first problem in a batch, repeated names included.
    internal static string? ProblemIn(IReadOnlyCollection<ContactInput> contacts)
    {
        if (contacts.Select(Problem).FirstOrDefault(p => p is not null) is { } problem) return problem;
        var repeated = contacts.GroupBy(c => Names.Normalize(c.Name)).FirstOrDefault(g => g.Count() > 1);
        return repeated is null ? null : $"Contact \"{repeated.First().Name.Trim()}\" is listed twice.";
    }
}

[ObjectType<Contact>]
public static partial class ContactNode
{
    static partial void Configure(IObjectTypeDescriptor<Contact> descriptor)
    {
        descriptor.Ignore(c => c.UserId);
        descriptor.Ignore(c => c.NormalizedName);
        descriptor.Field(c => c.Interactions).UseSorting();
    }
}
