using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Profiles;

// A person who can vouch for the user. A row (not JSON) so a CV can pick some of them by id later.
public class ProfileReference
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Relationship { get; set; } = null!;
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    // 0-based and consecutive; the mutations renumber on move and delete.
    public int Position { get; set; }
}

public class ProfileReferenceConfiguration : IEntityTypeConfiguration<ProfileReference>
{
    public void Configure(EntityTypeBuilder<ProfileReference> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Profile).WithMany(p => p.References).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.ProfileId, r.Position });
    }
}
