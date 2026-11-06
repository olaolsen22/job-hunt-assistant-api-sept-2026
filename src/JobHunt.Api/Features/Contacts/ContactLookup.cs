using JobHunt.Api.Data;
using JobHunt.Api.Features.Companies;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Contacts;

// Contacts from a parsed ad or company research: found by name within the company or created. A match only gets its
// empty fields filled, so a parse can't overwrite what the user edited, and nobody is ever removed.
internal static class ContactLookup
{
    // Inputs arrive validated, with no name twice. Changed says whether anyone was added or filled.
    public static async Task<(List<Contact> Contacts, bool Changed)> UpsertAsync(
        Company company, List<ContactInput> inputs, AppDbContext db, CancellationToken ct)
    {
        var names = inputs.Select(c => Names.Normalize(c.Name)).ToList();
        // Loading tracks them into company.Contacts, which also holds contacts an earlier row of the same batch added.
        if (company.Id != 0)
            await db.Contacts.Where(c => c.CompanyId == company.Id && names.Contains(c.NormalizedName)).LoadAsync(ct);

        var changed = false;
        var result = new List<Contact>();
        foreach (var input in inputs)
        {
            var name = Names.Normalize(input.Name);
            var contact = company.Contacts.FirstOrDefault(c => c.NormalizedName == name);
            if (contact is not null)
            {
                changed |= Fill(contact.Title, Inputs.Clean(input.Title), v => contact.Title = v);
                changed |= Fill(contact.Email, Inputs.Clean(input.Email), v => contact.Email = v);
                changed |= Fill(contact.Phone, Inputs.Clean(input.Phone), v => contact.Phone = v);
                changed |= Fill(contact.Url, Inputs.Clean(input.Url), v => contact.Url = v);
                changed |= Fill(contact.Notes, Inputs.Clean(input.Notes), v => contact.Notes = v);
            }
            else
            {
                contact = new Contact
                {
                    UserId = company.UserId,
                    Company = company,
                    Name = input.Name.Trim(),
                    NormalizedName = name,
                    Title = Inputs.Clean(input.Title),
                    Email = Inputs.Clean(input.Email),
                    Phone = Inputs.Clean(input.Phone),
                    Url = Inputs.Clean(input.Url),
                    Notes = Inputs.Clean(input.Notes),
                };
                db.Contacts.Add(contact);
                changed = true;
            }
            result.Add(contact);
        }
        return (result, changed);
    }

    private static bool Fill(string? current, string? incoming, Action<string> set)
    {
        if (current is not null || incoming is null) return false;
        set(incoming);
        return true;
    }
}
