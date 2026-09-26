using JobHunt.Api.Features.Auth;
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
    // Head office. A listing's own locations can differ.
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.HasOne(c => c.Location).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}
