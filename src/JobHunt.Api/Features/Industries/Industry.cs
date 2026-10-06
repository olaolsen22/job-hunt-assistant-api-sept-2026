using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Industries;

// The business a company is in ("Energy", "Shipping"). A lookup rather than free text so companies can be
// filtered by it, and so AI output settles on the names you already have.
public class Industry
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
}

public class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    public void Configure(EntityTypeBuilder<Industry> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.UserId, i.NormalizedName }).IsUnique();
    }
}
