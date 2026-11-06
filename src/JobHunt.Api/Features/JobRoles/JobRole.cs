using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.JobRoles;

// The core role behind a title, in English: "Backend-utvikler (.NET)" is "Backend Developer".
public class JobRole
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public int? CategoryId { get; set; }
    public JobRoleCategory? Category { get; set; }
}

public class JobRoleConfiguration : IEntityTypeConfiguration<JobRole>
{
    public void Configure(EntityTypeBuilder<JobRole> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.UserId, r.NormalizedName }).IsUnique();
        builder.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.CategoryId);
    }
}
