using JobHunt.Api.Data;
using JobHunt.Api.Features.Industries;
// Hot Chocolate also has a global Location type (a position in a query document).
using JobHunt.Api.Features.Locations;
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Companies;

// Same shape as the AI's company rows, so the UI can pass parsed JSON straight through.
public record CompanyInput(string Name, string? IndustryName, List<LocationInput> Locations);

// Created is false when the name already existed: that company is returned untouched.
public record CompanyResult(Company Company, bool Created);

public class InvalidCompanyException(string message) : Exception(message);

public class CompanyNotFoundException(int id) : Exception($"Company {id} was not found.");

[MutationType]
public static class CompanyMutations
{
    // Well above the ~30 rows a pasted quick-add list produces, far below what one transaction minds.
    private const int MaxBatchSize = 100;

    // All or nothing: every row is validated and resolved before anything is added, then one SaveChanges.
    // An existing name (or a repeat in the batch) returns that company as is; update it with updateCompany.
    [Error<InvalidCompanyException>]
    [Error<InvalidLocationException>]
    [UseMutationConvention(PayloadFieldName = "results")]
    public static async Task<List<CompanyResult>> CreateCompaniesAsync(
        List<CompanyInput> companies, AppDbContext db, CancellationToken ct)
    {
        if (companies.Count is 0 or > MaxBatchSize)
            throw new InvalidCompanyException($"Send between 1 and {MaxBatchSize} companies.");
        if (companies.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            throw new InvalidCompanyException("Every company needs a name.");

        // Resolved for every row, even ones that turn out to exist, so a bad country code always fails the batch.
        var lookups = new CompanyLookups(db);
        var rows = new List<(CompanyInput Input, string NormalizedName, Industry? Industry, List<Location> Locations)>();
        foreach (var input in companies)
            rows.Add((input, Names.Normalize(input.Name),
                await lookups.IndustryAsync(input.IndustryName, ct),
                await lookups.LocationsAsync(input.Locations, ct)));

        var names = rows.Select(r => r.NormalizedName).Distinct().ToList();
        var known = await db.Companies
            .Include(c => c.Industry).Include(c => c.Locations).ThenInclude(l => l.Location)
            .Where(c => names.Contains(c.NormalizedName))
            .ToDictionaryAsync(c => c.NormalizedName, ct);

        var results = new List<CompanyResult>();
        foreach (var row in rows)
        {
            if (known.TryGetValue(row.NormalizedName, out var company))
            {
                results.Add(new CompanyResult(company, false));
                continue;
            }
            company = new Company
            {
                UserId = db.CurrentUserId!,
                Name = row.Input.Name.Trim(),
                NormalizedName = row.NormalizedName,
                Industry = row.Industry,
                Locations = row.Locations.Select(l => new CompanyLocation { Location = l }).ToList(),
            };
            db.Companies.Add(company);
            known[row.NormalizedName] = company;
            results.Add(new CompanyResult(company, true));
        }
        await db.SaveChangesAsync(ct);
        return results;
    }

    [Error<CompanyNotFoundException>]
    public static async Task<Company> SetCompanyFavoriteAsync(
        int id, bool isFavorite, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        // ??= keeps the original time when favoriting twice, so the shortlist order doesn't jump.
        if (isFavorite)
            company.FavoritedAt ??= time.GetUtcNow().UtcDateTime;
        else
            company.FavoritedAt = null;
        await db.SaveChangesAsync(ct);
        return company;
    }

    // Companies are created by createListing; this is for fixing the name and replacing the industry and locations.
    [Error<CompanyNotFoundException>]
    [Error<InvalidCompanyException>]
    [Error<InvalidLocationException>]
    public static async Task<Company> UpdateCompanyAsync(
        int id, string name, string? industryName, List<LocationInput> locations, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidCompanyException("Name is required.");

        var company = await db.Companies.Include(c => c.Locations).ThenInclude(l => l.Location)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CompanyNotFoundException(id);
        var normalizedName = Names.Normalize(name);
        if (normalizedName != company.NormalizedName
            && await db.Companies.AnyAsync(c => c.NormalizedName == normalizedName, ct))
            throw new InvalidCompanyException($"A company named \"{name.Trim()}\" already exists.");

        var lookups = new CompanyLookups(db);
        company.Name = name.Trim();
        company.NormalizedName = normalizedName;
        company.Industry = await lookups.IndustryAsync(industryName, ct);

        // Diff instead of Clear + Add: re-adding a kept location would clash with the join row being deleted.
        var wanted = await lookups.LocationsAsync(locations, ct);
        company.Locations.RemoveAll(cl => !wanted.Contains(cl.Location));
        foreach (var location in wanted.Where(w => company.Locations.All(cl => cl.Location != w)))
            company.Locations.Add(new CompanyLocation { Location = location });

        await db.SaveChangesAsync(ct);
        return company;
    }
}

// Finds or creates industries and locations by name. FindOrCreate only sees saved rows, so each lookup is
// remembered here: two rows naming the same new industry must share one instance or the unique index fails on save.
internal class CompanyLookups(AppDbContext db)
{
    private readonly Dictionary<string, Industry> industries = [];
    private readonly Dictionary<string, Location> locations = [];

    public async Task<Industry?> IndustryAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalizedName = Names.Normalize(name);
        if (industries.TryGetValue(normalizedName, out var known)) return known;

        var industry = await db.Industries.FirstOrDefaultAsync(i => i.NormalizedName == normalizedName, ct)
            ?? new Industry { UserId = db.CurrentUserId!, Name = name.Trim(), NormalizedName = normalizedName };
        return industries[normalizedName] = industry;
    }

    // Repeats within one list collapse into one location.
    public async Task<List<Location>> LocationsAsync(IEnumerable<LocationInput> inputs, CancellationToken ct)
    {
        var result = new List<Location>();
        foreach (var input in inputs)
        {
            var key = LocationLookup.Key(input);
            if (!locations.TryGetValue(key, out var location))
                location = locations[key] = await LocationLookup.FindOrCreateAsync(input, db, ct);
            if (!result.Contains(location)) result.Add(location);
        }
        return result;
    }
}
