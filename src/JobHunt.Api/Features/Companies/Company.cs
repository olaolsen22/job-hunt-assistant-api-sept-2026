using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Industries;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Companies;

public class Company
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public int? IndustryId { get; set; }
    public Industry? Industry { get; set; }
    // Null = not favorited. A time rather than a bool so the shortlist can sort by when you favorited.
    public DateTime? FavoritedAt { get; set; }
    // Offices, in no particular role: a listing's own locations can differ.
    public List<CompanyLocation> Locations { get; set; } = [];
}

public class CompanyLocation
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.HasOne(c => c.Industry).WithMany().HasForeignKey(c => c.IndustryId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class CompanyLocationConfiguration : IEntityTypeConfiguration<CompanyLocation>
{
    public void Configure(EntityTypeBuilder<CompanyLocation> builder)
    {
        builder.HasKey(l => new { l.CompanyId, l.LocationId });
        builder.HasOne(l => l.Company).WithMany(c => c.Locations).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Location).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
