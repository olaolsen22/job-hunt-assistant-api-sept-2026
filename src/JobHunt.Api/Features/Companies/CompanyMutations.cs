using JobHunt.Api.Data;
using JobHunt.Api.Features.Locations;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Companies;

public class InvalidCompanyException(string message) : Exception(message);

public class CompanyNotFoundException(int id) : Exception($"Company {id} was not found.");

[MutationType]
public static class CompanyMutations
{
    // Companies are created by createListing; this is for fixing the name and adding the location.
    [Error<CompanyNotFoundException>]
    [Error<InvalidCompanyException>]
    [Error<InvalidLocationException>]
    public static async Task<Company> UpdateCompanyAsync(
        int id, string name, LocationInput? location, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidCompanyException("Name is required.");

        var company = await db.Companies.Include(c => c.Location).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        var normalizedName = Names.Normalize(name);
        if (normalizedName != company.NormalizedName
            && await db.Companies.AnyAsync(c => c.NormalizedName == normalizedName, ct))
            throw new InvalidCompanyException($"A company named \"{name.Trim()}\" already exists.");

        company.Name = name.Trim();
        company.NormalizedName = normalizedName;
        company.Location = location is null ? null : await LocationLookup.FindOrCreateAsync(location, db, ct);
        await db.SaveChangesAsync(ct);
        return company;
    }
}
