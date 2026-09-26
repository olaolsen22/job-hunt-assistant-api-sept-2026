using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Locations;

// A city in a country, or a whole country when City is null ("Norway, anywhere" for remote jobs).
public class Location
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string? City { get; set; }
    // "" rather than NULL for country-only rows: SQLite treats NULLs as distinct in a unique index,
    // so NULL would allow duplicate "Norway, anywhere" rows.
    public string NormalizedCity { get; set; } = "";
    // ISO 3166-1 alpha-2 ("NO"), not a name: the UI shows it in any language with Intl.DisplayNames.
    public string CountryCode { get; set; } = null!;
    // A place you want to work; drives the dashboard's "jobs in your target cities".
    public bool IsTarget { get; set; }
}

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => new { l.UserId, l.CountryCode, l.NormalizedCity }).IsUnique();
    }
}
