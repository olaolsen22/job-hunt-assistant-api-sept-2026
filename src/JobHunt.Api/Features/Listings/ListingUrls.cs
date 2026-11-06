namespace JobHunt.Api.Features.Listings;

internal static class ListingUrls
{
    // Stored normalized so the unique index catches tracking params, #fragments and trailing slashes.
    // Other params stay: some boards put the ad id in the query (?finnkode=123).
    public static string Normalize(Uri url)
    {
        var query = string.Join('&', url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTrackingParam(p.Split('=')[0])));
        return $"{url.Scheme}://{url.Authority}{url.AbsolutePath.TrimEnd('/')}{(query == "" ? "" : "?" + query)}";
    }

    private static bool IsTrackingParam(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
        || key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
        || key.Equals("gclid", StringComparison.OrdinalIgnoreCase);
}
