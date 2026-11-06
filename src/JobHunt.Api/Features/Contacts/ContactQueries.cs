using JobHunt.Api.Data;
using HotChocolate.Data.Sorting;

namespace JobHunt.Api.Features.Contacts;

[QueryType]
public static class ContactQueries
{
    // Everyone across companies: the people to reach out to, and follow-ups due via where: { followUpOn: { lte: … } }.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Contact> GetContacts(string? search, AppDbContext db, ISortingContext sorting)
    {
        // Same as companies: a default order only without a client `order`.
        sorting.Handled(false);
        IQueryable<Contact> contacts = sorting.IsDefined ? db.Contacts : db.Contacts.OrderBy(c => c.Name);
        if (search is null) return contacts;
        var name = Names.Normalize(search);
        return contacts.Where(c => c.NormalizedName.Contains(name));
    }

    // One person with their interactions; nested lists on the paged query would blow the cost limit.
    [UseFirstOrDefault]
    [UseProjection]
    public static IQueryable<Contact> GetContactById(int id, AppDbContext db) =>
        db.Contacts.Where(c => c.Id == id);
}
