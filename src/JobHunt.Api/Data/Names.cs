namespace JobHunt.Api.Data;

public static class Names
{
    // Backs the NormalizedName lookup columns, so "equinor" and "Equinor" are one company.
    // Upper-cased in C# because SQLite's NOCASE only folds ASCII, not æøå.
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
