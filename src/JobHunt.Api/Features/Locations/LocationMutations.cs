using System.Globalization;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Locations;

// City is optional: { countryCode: "NO" } alone means anywhere in Norway.
public record LocationInput(string? City, string CountryCode);

public class InvalidLocationException(string message) : Exception(message);

[MutationType]
public static class LocationMutations
{
    // Find-or-create, so you can target a city before any listing mentions it. isTarget: false un-targets it.
    [Error<InvalidLocationException>]
    public static async Task<Location> SaveLocationAsync(
        string? city, string countryCode, bool isTarget, AppDbContext db, CancellationToken ct)
    {
        var location = await LocationLookup.FindOrCreateAsync(new LocationInput(city, countryCode), db, ct);
        location.IsTarget = isTarget;
        if (location.Id == 0) db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return location;
    }
}

// Shared by listings, companies and saveLocation, which all take locations as { city, countryCode }.
public static class LocationLookup
{
    // Returns the existing location, or a new unsaved one that the caller attaches.
    public static async Task<Location> FindOrCreateAsync(LocationInput input, AppDbContext db, CancellationToken ct)
    {
        var countryCode = input.CountryCode.Trim().ToUpperInvariant();
        if (!IsCountryCode(countryCode))
            throw new InvalidLocationException($"\"{input.CountryCode}\" is not an ISO 3166 country code, e.g. NO.");

        var city = string.IsNullOrWhiteSpace(input.City) ? null : input.City.Trim();
        var normalizedCity = city is null ? "" : Names.Normalize(city);
        return await db.Locations.FirstOrDefaultAsync(
                l => l.CountryCode == countryCode && l.NormalizedCity == normalizedCity, ct)
            ?? new Location
            {
                UserId = db.CurrentUserId!,
                City = city,
                NormalizedCity = normalizedCity,
                CountryCode = countryCode,
            };
    }

    public static string Key(LocationInput input) =>
        $"{input.CountryCode.Trim().ToUpperInvariant()}|{(input.City is null ? "" : Names.Normalize(input.City))}";

    // Framework region data (ICU), so there's no country list to maintain.
    private static bool IsCountryCode(string code)
    {
        if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper)) return false;
        try
        {
            return new RegionInfo(code).TwoLetterISORegionName == code;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
